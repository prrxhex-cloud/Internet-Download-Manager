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
        bool IsYouTubeUrl(string url);
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
            @"(youtube\.com|youtu\.be|tiktok\.com|facebook\.com|fb\.watch|instagram\.com|pinterest\.com|pin\.it|twitter\.com|x\.com|drive\.google\.com|mediafire\.com|pixeldrain\.com|usersdrive\.com|spotify\.com|sinhanada\.net|slmix\.lk|paperhub|pastpapers|an1\.com|happymod\.com|apkpure\.com|uptodown\.com|\.apk($|\?)|\.pdf($|\?))",
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
                ConnectTimeout = TimeSpan.FromSeconds(5),
                AutomaticDecompression = DecompressionMethods.All,
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true,
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
                }
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
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

        public bool IsYouTubeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
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
                if (!status || response.StatusCode != HttpStatusCode.OK)
                {
                    return new CloudResolvedMedia
                    {
                        Success = false,
                        FallbackRequired = true,
                        Url = url,
                        ErrorMessage = root.TryGetProperty("error", out var eEl) ? eEl.GetString() : "Resolution unsuccessful"
                    };
                }

                string? extractedDirectUrl = null;
                string? extractedTitle = null;
                string? extractedThumbnail = null;
                string? extractedSize = null;

                // Check root-level properties (Google Drive, Pixeldrain, UsersDrive, etc.)
                if (root.TryGetProperty("download_url", out var rootDlUrl)) extractedDirectUrl = rootDlUrl.GetString();
                else if (root.TryGetProperty("download_url_alt", out var rootDlUrlAlt)) extractedDirectUrl = rootDlUrlAlt.GetString();
                else if (root.TryGetProperty("download", out var rootDl)) extractedDirectUrl = rootDl.GetString();
                else if (root.TryGetProperty("url", out var rootUrl)) extractedDirectUrl = rootUrl.GetString();
                else if (root.TryGetProperty("link", out var rootLink)) extractedDirectUrl = rootLink.GetString();
                else if (root.TryGetProperty("direct", out var rootDirect)) extractedDirectUrl = rootDirect.GetString();

                if (root.TryGetProperty("title", out var rootTitle)) extractedTitle = rootTitle.GetString();
                else if (root.TryGetProperty("file_name", out var rootFileName)) extractedTitle = rootFileName.GetString();
                else if (root.TryGetProperty("name", out var rootName)) extractedTitle = rootName.GetString();

                if (root.TryGetProperty("thumbnail", out var rootThumb)) extractedThumbnail = rootThumb.GetString();
                else if (root.TryGetProperty("image", out var rootImg)) extractedThumbnail = rootImg.GetString();
                else if (root.TryGetProperty("preview_url", out var rootPrev)) extractedThumbnail = rootPrev.GetString();

                if (root.TryGetProperty("size", out var rootSz)) extractedSize = rootSz.GetString();
                else if (root.TryGetProperty("file_size", out var rootFileSz)) extractedSize = rootFileSz.GetString();

                // Check result property (can be Object, String, or Array)
                if (root.TryGetProperty("result", out var resEl))
                {
                    if (resEl.ValueKind == JsonValueKind.Object)
                    {
                        if (string.IsNullOrWhiteSpace(extractedDirectUrl))
                        {
                            if (resEl.TryGetProperty("download", out var dlEl)) extractedDirectUrl = dlEl.GetString();
                            else if (resEl.TryGetProperty("download_url", out var dlUrlEl)) extractedDirectUrl = dlUrlEl.GetString();
                            else if (resEl.TryGetProperty("url", out var uEl)) extractedDirectUrl = uEl.GetString();
                            else if (resEl.TryGetProperty("link", out var lkEl)) extractedDirectUrl = lkEl.GetString();
                            else if (resEl.TryGetProperty("direct", out var dtEl)) extractedDirectUrl = dtEl.GetString();
                            else if (resEl.TryGetProperty("video", out var vEl)) extractedDirectUrl = vEl.GetString();
                            else if (resEl.TryGetProperty("audio", out var aEl)) extractedDirectUrl = aEl.GetString();
                            else if (resEl.TryGetProperty("stream", out var sEl)) extractedDirectUrl = sEl.GetString();
                        }

                        if (string.IsNullOrWhiteSpace(extractedTitle))
                        {
                            if (resEl.TryGetProperty("title", out var titleEl)) extractedTitle = titleEl.GetString();
                            else if (resEl.TryGetProperty("name", out var nEl)) extractedTitle = nEl.GetString();
                        }

                        if (string.IsNullOrWhiteSpace(extractedThumbnail))
                        {
                            if (resEl.TryGetProperty("thumbnail", out var thumbEl)) extractedThumbnail = thumbEl.GetString();
                            else if (resEl.TryGetProperty("image", out var imgEl)) extractedThumbnail = imgEl.GetString();
                            else if (resEl.TryGetProperty("best", out var bestEl) && bestEl.TryGetProperty("url", out var bestUrlEl)) extractedThumbnail = bestUrlEl.GetString();
                        }

                        if (string.IsNullOrWhiteSpace(extractedSize))
                        {
                            if (resEl.TryGetProperty("size", out var sizeEl)) extractedSize = sizeEl.GetString();
                            else if (resEl.TryGetProperty("filesize", out var fsEl)) extractedSize = fsEl.GetString();
                        }
                    }
                    else if (resEl.ValueKind == JsonValueKind.String)
                    {
                        if (string.IsNullOrWhiteSpace(extractedDirectUrl)) extractedDirectUrl = resEl.GetString();
                    }
                    else if (resEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var arrItem in resEl.EnumerateArray())
                        {
                            if (arrItem.ValueKind == JsonValueKind.Object)
                            {
                                if (string.IsNullOrWhiteSpace(extractedDirectUrl))
                                {
                                    if (arrItem.TryGetProperty("download", out var dlEl)) extractedDirectUrl = dlEl.GetString();
                                    else if (arrItem.TryGetProperty("download_url", out var dlUrlEl)) extractedDirectUrl = dlUrlEl.GetString();
                                    else if (arrItem.TryGetProperty("url", out var uEl)) extractedDirectUrl = uEl.GetString();
                                    else if (arrItem.TryGetProperty("link", out var lkEl)) extractedDirectUrl = lkEl.GetString();
                                }
                                if (string.IsNullOrWhiteSpace(extractedTitle) && arrItem.TryGetProperty("title", out var tEl)) extractedTitle = tEl.GetString();
                                if (string.IsNullOrWhiteSpace(extractedThumbnail) && arrItem.TryGetProperty("image", out var iEl)) extractedThumbnail = iEl.GetString();
                                if (!string.IsNullOrWhiteSpace(extractedDirectUrl)) break;
                            }
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(extractedDirectUrl))
                {
                    return new CloudResolvedMedia
                    {
                        Success = false,
                        FallbackRequired = true,
                        Url = url,
                        ErrorMessage = "No stream or direct download URL found in cloud response."
                    };
                }

                var resolved = new CloudResolvedMedia
                {
                    Success = true,
                    Url = url,
                    DirectStreamUrl = extractedDirectUrl,
                    Title = extractedTitle,
                    ThumbnailUrl = extractedThumbnail,
                    FormattedSize = extractedSize
                };

                // Deduce Extension & Container intelligently
                var cleanOriginal = url.Split('?')[0].TrimEnd('/');
                var cleanStream = resolved.DirectStreamUrl.Split('?')[0].TrimEnd('/');
                var lowerStream = resolved.DirectStreamUrl.ToLowerInvariant();
                var lowerOriginal = url.ToLowerInvariant();

                string? detectedExt = null;

                // 1. Check title for existing extension
                if (!string.IsNullOrWhiteSpace(resolved.Title))
                {
                    var ext = Path.GetExtension(resolved.Title).TrimStart('.').ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 5)
                    {
                        detectedExt = ext;
                    }
                }

                // 2. Check stream URL path
                if (string.IsNullOrWhiteSpace(detectedExt))
                {
                    var ext = Path.GetExtension(cleanStream).TrimStart('.').ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 5 && ext != "php" && ext != "cgi" && ext != "htm" && ext != "html")
                    {
                        detectedExt = ext;
                    }
                }

                // 3. Check original URL path
                if (string.IsNullOrWhiteSpace(detectedExt))
                {
                    var ext = Path.GetExtension(cleanOriginal).TrimStart('.').ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 5 && ext != "php" && ext != "cgi" && ext != "htm" && ext != "html")
                    {
                        detectedExt = ext;
                    }
                }

                // 4. Check audio characteristics
                if (lowerStream.Contains(".mp3") || (format != null && format.Contains("mp3")) || lowerOriginal.Contains("spotify") || lowerOriginal.Contains("sinhanada") || lowerOriginal.Contains("slmix"))
                {
                    resolved.IsAudioOnly = true;
                    detectedExt ??= "mp3";
                }
                else if (lowerStream.Contains(".m4a"))
                {
                    resolved.IsAudioOnly = true;
                    detectedExt ??= "m4a";
                }
                else if (lowerStream.Contains(".flac") || lowerStream.Contains(".wav") || lowerStream.Contains(".aac") || lowerStream.Contains(".opus"))
                {
                    resolved.IsAudioOnly = true;
                }

                // 5. Default by category
                if (string.IsNullOrWhiteSpace(detectedExt))
                {
                    if (lowerOriginal.Contains("youtube.com") || lowerOriginal.Contains("youtu.be") ||
                        lowerOriginal.Contains("tiktok.com") || lowerOriginal.Contains("facebook.com") ||
                        lowerOriginal.Contains("instagram.com") || lowerOriginal.Contains("twitter.com") ||
                        lowerOriginal.Contains("x.com") || lowerOriginal.Contains("pinterest.com"))
                    {
                        detectedExt = "mp4";
                    }
                    else if (lowerOriginal.Contains("paperhub") || lowerOriginal.Contains("pastpapers") || lowerOriginal.Contains("pdf"))
                    {
                        detectedExt = "pdf";
                    }
                    else if (lowerOriginal.Contains("apk") || lowerOriginal.Contains("happymod") || lowerOriginal.Contains("an1") || lowerOriginal.Contains("uptodown"))
                    {
                        detectedExt = "apk";
                    }
                    else
                    {
                        detectedExt = "bin";
                    }
                }

                resolved.Extension = detectedExt;

                if (string.IsNullOrWhiteSpace(resolved.Title))
                {
                    var leaf = Path.GetFileName(cleanOriginal);
                    resolved.Title = !string.IsNullOrWhiteSpace(leaf) ? Path.GetFileNameWithoutExtension(leaf) : "Cloud_Download";
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

            var thumb = resolved.ThumbnailUrl;
            if (string.IsNullOrWhiteSpace(thumb) && IsYouTubeUrl(url))
            {
                var match = Regex.Match(url, @"(?:youtu\.be\/|v\/|u\/\w\/|embed\/|watch\?v=|&v=)([^#&?]{11})", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    thumb = $"https://i.ytimg.com/vi/{match.Groups[1].Value}/hqdefault.jpg";
                }
            }

            var probe = new MediaProbeResult
            {
                Id = url,
                Title = !string.IsNullOrWhiteSpace(resolved.Title) ? resolved.Title : (IsYouTubeUrl(url) ? "YouTube Video" : "Cloud Accelerated Stream"),
                ThumbnailUrl = thumb ?? string.Empty,
                PublisherName = IsYouTubeUrl(url) ? "YouTube (Cloud SASA Accelerated)" : "Cloud Intelligence (SASA Accelerated)",
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

            if (IsYouTubeUrl(url) || !resolved.IsAudioOnly)
            {
                probe.Formats.Add(new MediaFormat
                {
                    FormatId = "cloud_mp3_stream",
                    Resolution = "320 kbps (High Quality Audio)",
                    Extension = "mp3",
                    Note = "Cloud High-Speed MP3 Audio",
                    HasVideo = false,
                    HasAudio = true
                });
            }

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
                if (provLower is "all" or "search_dl" or "unified" or "unified engine")
                {
                    tasks.Add(FetchApkItemsAsync($"{_baseUrl}/api/cloud/search/apk?mode=search_dl&query={cleanQuery}", "Unified Search & DL", ct));
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

                JsonElement? arrayElement = null;
                if (root.TryGetProperty("result", out var resEl))
                {
                    if (resEl.ValueKind == JsonValueKind.Array) arrayElement = resEl;
                    else if (resEl.ValueKind == JsonValueKind.Object && resEl.TryGetProperty("data", out var dEl) && dEl.ValueKind == JsonValueKind.Array) arrayElement = dEl;
                }
                else if (root.TryGetProperty("data", out var rootData) && rootData.ValueKind == JsonValueKind.Array)
                {
                    arrayElement = rootData;
                }

                if (arrayElement.HasValue)
                {
                    foreach (var item in arrayElement.Value.EnumerateArray())
                    {
                        var title = item.TryGetProperty("title", out var tEl) ? tEl.GetString() ?? "" :
                                    (item.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "");
                        if (string.IsNullOrWhiteSpace(title)) continue;

                        var image = item.TryGetProperty("image", out var imgEl) ? imgEl.GetString() :
                                    (item.TryGetProperty("icon", out var icEl) ? icEl.GetString() :
                                    (item.TryGetProperty("thumbnail", out var thEl) ? thEl.GetString() : null));

                        var size = item.TryGetProperty("size", out var szEl) ? szEl.GetString() :
                                   (item.TryGetProperty("filesize", out var fsEl) ? fsEl.GetString() : "Unknown");

                        var version = item.TryGetProperty("version", out var vEl) ? vEl.GetString() : "Latest";

                        var link = item.TryGetProperty("link", out var lkEl) ? lkEl.GetString() :
                                   (item.TryGetProperty("download", out var dlEl) ? dlEl.GetString() :
                                   (item.TryGetProperty("download_url", out var dluEl) ? dluEl.GetString() :
                                   (item.TryGetProperty("url", out var uEl) ? uEl.GetString() : null)));

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

                    if (root.TryGetProperty("download_url", out var dlUrlRoot))
                    {
                        var direct = dlUrlRoot.GetString();
                        if (!string.IsNullOrWhiteSpace(direct)) return direct;
                    }
                    if (root.TryGetProperty("download", out var dlRoot))
                    {
                        var direct = dlRoot.GetString();
                        if (!string.IsNullOrWhiteSpace(direct)) return direct;
                    }

                    if (root.TryGetProperty("result", out var resEl))
                    {
                        if (resEl.ValueKind == JsonValueKind.Object)
                        {
                            if (resEl.TryGetProperty("download", out var dlEl) && !string.IsNullOrWhiteSpace(dlEl.GetString())) return dlEl.GetString();
                            if (resEl.TryGetProperty("download_url", out var dluEl) && !string.IsNullOrWhiteSpace(dluEl.GetString())) return dluEl.GetString();
                            if (resEl.TryGetProperty("url", out var uEl) && !string.IsNullOrWhiteSpace(uEl.GetString())) return uEl.GetString();
                        }
                        if (resEl.ValueKind == JsonValueKind.String)
                        {
                            var s = resEl.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) return s;
                        }
                    }
                }
            }
            catch { }

            // If direct link was already an actual APK file download link, return directly
            if (apkLinkOrPackage.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) ||
                apkLinkOrPackage.Contains(".apk?", StringComparison.OrdinalIgnoreCase))
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
