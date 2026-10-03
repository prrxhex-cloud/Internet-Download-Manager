// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PRRX.IDM.Models;

namespace PRRX.IDM.Services
{
    public interface ICloudResolverService
    {
        string BaseUrl { get; }
        bool CanResolve(string url);
        Task<CloudResolvedMedia?> ResolveMediaAsync(string url, string? format = null, CancellationToken ct = default);
        Task<MediaProbeResult?> ResolveMediaProbeAsync(string url, CancellationToken ct = default);
        Task<string?> ResolveDirectDownloadUrlAsync(string url, CancellationToken ct = default);
        Task<ApkSearchResult> SearchApkAsync(string query, string provider = "all", CancellationToken ct = default);
        Task<string?> ResolveApkDownloadUrlAsync(string apkLinkOrPackage, CancellationToken ct = default);
        Task<bool> CheckServiceHealthAsync(CancellationToken ct = default);
    }

    public class CloudResolverService : ICloudResolverService
    {
        public const string DefaultBaseUrl = "https://prrx-api.sayurusenavirathna70.workers.dev";
        private static readonly HttpClient SharedHttpClient = CreateDefaultHttpClient();

        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        private static readonly Regex CloudResolvableDomains = new(
            @"(youtube\.com|youtu\.be|tiktok\.com|facebook\.com|fb\.watch|instagram\.com|pinterest\.com|pin\.it|twitter\.com|x\.com|drive\.google\.com|mediafire\.com|pixeldrain\.com|usersdrive\.com|spotify\.com|sinhanada\.net|slmix\.lk|paperhub)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public string BaseUrl => _baseUrl;

        public CloudResolverService(string? baseUrl = null, HttpClient? httpClient = null)
        {
            _baseUrl = !string.IsNullOrWhiteSpace(baseUrl) ? baseUrl.TrimEnd('/') : DefaultBaseUrl;
            _httpClient = httpClient ?? SharedHttpClient;
        }

        private static HttpClient CreateDefaultHttpClient()
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = false, // Direct fast connect
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                ConnectTimeout = TimeSpan.FromSeconds(3),
                AutomaticDecompression = DecompressionMethods.All
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PRRX-IDM", "1.8.0"));
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return client;
        }

        public bool CanResolve(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            try
            {
                return CloudResolvableDomains.IsMatch(url);
            }
            catch
            {
                return false;
            }
        }

        public async Task<CloudResolvedMedia?> ResolveMediaAsync(string url, string? format = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            try
            {
                var encodedUrl = Uri.EscapeDataString(url.Trim());
                var requestUri = $"{_baseUrl}/api/cloud/resolve?url={encodedUrl}";
                if (!string.IsNullOrWhiteSpace(format))
                {
                    requestUri += $"&format={Uri.EscapeDataString(format)}";
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                using var response = await _httpClient.SendAsync(request, ct);

                var json = await response.Content.ReadAsStringAsync(ct);
                if (string.IsNullOrWhiteSpace(json)) return null;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Check for rate limit or fallback instructions
                bool fallbackRequired = false;
                if (root.TryGetProperty("fallbackRequired", out var fbEl) && fbEl.GetBoolean())
                {
                    fallbackRequired = true;
                }
                if (root.TryGetProperty("rateLimitExceeded", out var rlEl) && rlEl.GetBoolean())
                {
                    fallbackRequired = true;
                }

                if (fallbackRequired)
                {
                    return new CloudResolvedMedia
                    {
                        Success = false,
                        FallbackRequired = true,
                        Url = url,
                        ErrorMessage = root.TryGetProperty("error", out var errEl) ? errEl.GetString() : "Rate limited or fallback requested"
                    };
                }

                bool status = root.TryGetProperty("status", out var stEl) && (stEl.ValueKind == JsonValueKind.True || (stEl.ValueKind == JsonValueKind.String && stEl.GetString() == "true"));
                if (!status && response.StatusCode != HttpStatusCode.OK)
                {
                    return new CloudResolvedMedia
                    {
                        Success = false,
                        FallbackRequired = true,
                        Url = url,
                        ErrorMessage = root.TryGetProperty("error", out var eEl) ? eEl.GetString() : "Resolution unsuccessful"
                    };
                }

                var resolved = new CloudResolvedMedia
                {
                    Success = true,
                    Url = url
                };

                // Extract Result Object or String
                if (root.TryGetProperty("result", out var resEl))
                {
                    if (resEl.ValueKind == JsonValueKind.Object)
                    {
                        // Direct download link
                        if (resEl.TryGetProperty("download", out var dlEl))
                        {
                            resolved.DirectStreamUrl = dlEl.GetString();
                        }
                        else if (resEl.TryGetProperty("url", out var uEl))
                        {
                            resolved.DirectStreamUrl = uEl.GetString();
                        }
                        else if (resEl.TryGetProperty("link", out var lkEl))
                        {
                            resolved.DirectStreamUrl = lkEl.GetString();
                        }
                        else if (resEl.TryGetProperty("direct", out var dtEl))
                        {
                            resolved.DirectStreamUrl = dtEl.GetString();
                        }

                        // Title
                        if (resEl.TryGetProperty("title", out var titleEl))
                        {
                            resolved.Title = titleEl.GetString();
                        }

                        // Thumbnail
                        if (resEl.TryGetProperty("thumbnail", out var thumbEl))
                        {
                            resolved.ThumbnailUrl = thumbEl.GetString();
                        }
                        else if (resEl.TryGetProperty("best", out var bestEl) && bestEl.TryGetProperty("url", out var bestUrlEl))
                        {
                            resolved.ThumbnailUrl = bestUrlEl.GetString();
                        }

                        // File size
                        if (resEl.TryGetProperty("size", out var sizeEl))
                        {
                            resolved.FormattedSize = sizeEl.GetString();
                        }
                    }
                    else if (resEl.ValueKind == JsonValueKind.String)
                    {
                        resolved.DirectStreamUrl = resEl.GetString();
                    }
                }
                else if (root.TryGetProperty("download", out var rootDl))
                {
                    resolved.DirectStreamUrl = rootDl.GetString();
                }

                // If no direct URL could be found, signal fallback
                if (string.IsNullOrWhiteSpace(resolved.DirectStreamUrl))
                {
                    resolved.Success = false;
                    resolved.FallbackRequired = true;
                    return resolved;
                }

                // Deduce Extension & Container
                var lowerStream = resolved.DirectStreamUrl.ToLowerInvariant();
                if (lowerStream.Contains(".mp3") || (format != null && format.Contains("mp3")))
                {
                    resolved.Extension = "mp3";
                    resolved.IsAudioOnly = true;
                }
                else if (lowerStream.Contains(".m4a"))
                {
                    resolved.Extension = "m4a";
                    resolved.IsAudioOnly = true;
                }
                else if (lowerStream.Contains(".zip"))
                {
                    resolved.Extension = "zip";
                }
                else if (lowerStream.Contains(".apk"))
                {
                    resolved.Extension = "apk";
                }
                else if (lowerStream.Contains(".pdf"))
                {
                    resolved.Extension = "pdf";
                }
                else
                {
                    resolved.Extension = "mp4";
                }

                if (string.IsNullOrWhiteSpace(resolved.Title))
                {
                    resolved.Title = Path.GetFileNameWithoutExtension(url.Split('?')[0]);
                }

                return resolved;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CloudResolver error: {ex.Message}");
                return new CloudResolvedMedia
                {
                    Success = false,
                    FallbackRequired = true,
                    Url = url,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<MediaProbeResult?> ResolveMediaProbeAsync(string url, CancellationToken ct = default)
        {
            var resolved = await ResolveMediaAsync(url, null, ct);
            if (resolved == null || !resolved.Success || string.IsNullOrWhiteSpace(resolved.DirectStreamUrl))
            {
                return null;
            }

            var probe = new MediaProbeResult
            {
                Id = url,
                Title = !string.IsNullOrWhiteSpace(resolved.Title) ? resolved.Title : "Cloud Accelerated Stream",
                ThumbnailUrl = resolved.ThumbnailUrl ?? string.Empty,
                PublisherName = "Cloud Intelligence (SASA Accelerated)",
                Formats = new List<MediaFormat>
                {
                    new MediaFormat
                    {
                        FormatId = "cloud_cdn_stream",
                        Resolution = !string.IsNullOrWhiteSpace(resolved.FormattedSize) ? $"{resolved.FormattedSize} (Turbo Stream)" : "1080p Turbo Fiber Stream",
                        Extension = resolved.Extension ?? "mp4",
                        Note = "High-Speed Zero-Lag Direct Stream",
                        HasVideo = !resolved.IsAudioOnly,
                        HasAudio = true,
                        DirectDownloadUrl = resolved.DirectStreamUrl
                    }
                }
            };

            return probe;
        }

        public async Task<string?> ResolveDirectDownloadUrlAsync(string url, CancellationToken ct = default)
        {
            var resolved = await ResolveMediaAsync(url, null, ct);
            if (resolved != null && resolved.Success && !string.IsNullOrWhiteSpace(resolved.DirectStreamUrl))
            {
                return resolved.DirectStreamUrl;
            }
            return null;
        }

        public async Task<ApkSearchResult> SearchApkAsync(string query, string provider = "all", CancellationToken ct = default)
        {
            var result = new ApkSearchResult
            {
                Query = query,
                Success = false
            };

            if (string.IsNullOrWhiteSpace(query))
            {
                result.ErrorMessage = "Empty search query";
                return result;
            }

            try
            {
                var cleanQuery = Uri.EscapeDataString(query.Trim());
                var tasks = new List<Task<List<ApkItem>>>();

                var provLower = (provider ?? "all").ToLowerInvariant();

                if (provLower is "all" or "ultra" or "ultra store")
                {
                    tasks.Add(FetchApkItemsAsync($"{_baseUrl}/api/cloud/search/apksearch?query={cleanQuery}", "Ultra Store", ct));
                }
                if (provLower is "all" or "happymod" or "happymod mods")
                {
                    tasks.Add(FetchApkItemsAsync($"{_baseUrl}/api/cloud/apk/happymod?query={cleanQuery}", "HappyMod MODs", ct));
                }
                if (provLower is "all" or "an1" or "an1 mods")
                {
                    tasks.Add(FetchApkItemsAsync($"{_baseUrl}/api/cloud/apk/an1?query={cleanQuery}", "AN1 MODs", ct));
                }
                if (provLower is "all" or "apkpure")
                {
                    tasks.Add(FetchApkItemsAsync($"{_baseUrl}/api/cloud/apk/apkpure?query={cleanQuery}", "APKPure", ct));
                }
                if (provLower is "all" or "uptodown")
                {
                    tasks.Add(FetchApkItemsAsync($"{_baseUrl}/api/cloud/apk/uptodown?query={cleanQuery}", "Uptodown", ct));
                }

                if (tasks.Count == 0)
                {
                    tasks.Add(FetchApkItemsAsync($"{_baseUrl}/api/cloud/search/apksearch?query={cleanQuery}", "Ultra Store", ct));
                }

                var batch = await Task.WhenAll(tasks);
                foreach (var items in batch)
                {
                    if (items != null && items.Count > 0)
                    {
                        result.Items.AddRange(items);
                    }
                }

                result.Success = result.Items.Count > 0;
                if (!result.Success)
                {
                    result.ErrorMessage = "No APK packages found matching query.";
                }
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SearchApkAsync error: {ex.Message}");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.FallbackRequired = true;
                return result;
            }
        }

        private async Task<List<ApkItem>> FetchApkItemsAsync(string endpoint, string providerTag, CancellationToken ct)
        {
            var list = new List<ApkItem>();
            try
            {
                using var response = await _httpClient.GetAsync(endpoint, ct);
                if (!response.IsSuccessStatusCode) return list;

                var json = await response.Content.ReadAsStringAsync(ct);
                if (string.IsNullOrWhiteSpace(json)) return list;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("result", out var resEl) && resEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in resEl.EnumerateArray())
                    {
                        var title = item.TryGetProperty("title", out var tEl) ? tEl.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(title)) continue;

                        var image = item.TryGetProperty("image", out var imgEl) ? imgEl.GetString() : null;
                        var size = item.TryGetProperty("size", out var szEl) ? szEl.GetString() : "Unknown";
                        var version = item.TryGetProperty("version", out var vEl) ? vEl.GetString() : "Latest";
                        var link = item.TryGetProperty("link", out var lkEl) ? lkEl.GetString() : null;

                        list.Add(new ApkItem
                        {
                            Title = title,
                            IconUrl = image,
                            Size = size,
                            Version = version,
                            DownloadUrl = link,
                            DetailUrl = link,
                            Provider = providerTag
                        });
                    }
                }
            }
            catch
            {
                // Silently return empty list on per-provider failure so other stores succeed
            }
            return list;
        }

        public async Task<string?> ResolveApkDownloadUrlAsync(string apkLinkOrPackage, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apkLinkOrPackage)) return null;

            try
            {
                var clean = Uri.EscapeDataString(apkLinkOrPackage.Trim());
                var url = $"{_baseUrl}/api/cloud/download/apkdownload?url={clean}";

                using var response = await _httpClient.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("result", out var resEl))
                    {
                        if (resEl.ValueKind == JsonValueKind.Object && resEl.TryGetProperty("download", out var dlEl))
                        {
                            return dlEl.GetString();
                        }
                        if (resEl.ValueKind == JsonValueKind.String)
                        {
                            return resEl.GetString();
                        }
                    }
                }
            }
            catch { }

            // If direct link was already an APK download link, return directly
            if (apkLinkOrPackage.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) ||
                apkLinkOrPackage.Contains("/app") ||
                apkLinkOrPackage.Contains("aptoide.com"))
            {
                return apkLinkOrPackage;
            }

            return null;
        }

        public async Task<bool> CheckServiceHealthAsync(CancellationToken ct = default)
        {
            try
            {
                var res = await _httpClient.GetAsync($"{_baseUrl}/api/cloud/health", ct);
                return res.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
