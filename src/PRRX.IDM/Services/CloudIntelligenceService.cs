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
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PRRX.IDM.Models;

namespace PRRX.IDM.Services
{
    public interface ICloudIntelligenceService
    {
        string BaseUrl { get; }
        Task<CloudHealthResult?> CheckHealthAsync(CancellationToken cancellationToken = default);
        Task<FileReputationResult> GetFileReputationAsync(string sha256Hash, CancellationToken cancellationToken = default);
        Task<bool> ReportReputationAsync(string sha256Hash, string fileName, long fileSizeBytes, string vote = "safe", CancellationToken cancellationToken = default);
        Task<DomainHealthResult> GetDomainHealthAsync(string domain, CancellationToken cancellationToken = default);
        Task<LanPeersResult> GetLanPeersAsync(string sha256Hash, string? localLanIp = null, CancellationToken cancellationToken = default);
        Task<bool> AnnounceLanPeerAsync(string peerId, string sha256Hash, string? localLanIp = null, int port = 6881, int completedChunks = 0, int totalChunks = 0, CancellationToken cancellationToken = default);
        Task<MirrorsResult> GetMirrorsAsync(string sha256Hash, CancellationToken cancellationToken = default);
        string ComputeSha256(byte[] data);
        string ComputeSha256(string input);
        Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken = default);
        string GetLocalLanIp();
        string ExtractOrComputeSha256(string url, string? fileName = null);
    }

    public class CloudIntelligenceService : ICloudIntelligenceService
    {
        public const string DefaultBaseUrl = "https://prrx-api.sayurusenavirathna70.workers.dev";
        private const int DefaultTimeoutMs = 1800;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly HttpClient SharedHttpClient = CreateDefaultHttpClient();

        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public string BaseUrl => _baseUrl;

        public CloudIntelligenceService(string? baseUrl = null, HttpClient? httpClient = null)
        {
            _baseUrl = !string.IsNullOrWhiteSpace(baseUrl) ? baseUrl.TrimEnd('/') : DefaultBaseUrl;
            _httpClient = httpClient ?? SharedHttpClient;
        }

        private static HttpClient CreateDefaultHttpClient()
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = false, // Bypass Windows WPAD proxy auto-detect latency
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                ConnectTimeout = TimeSpan.FromSeconds(2),
                AutomaticDecompression = DecompressionMethods.All
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(4)
            };

            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PRRX-IDM", "1.3.0"));
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return client;
        }

        public async Task<CloudHealthResult?> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CreateLinkedTimeout(cancellationToken);
            try
            {
                var uri = $"{_baseUrl}/api/health";
                using var response = await _httpClient.GetAsync(uri, timeoutCts.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
                    return await JsonSerializer.DeserializeAsync<CloudHealthResult>(stream, JsonOptions, timeoutCts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CloudIntelligence] Health check error: {ex.Message}");
            }

            return new CloudHealthResult { Status = "offline" };
        }

        public async Task<FileReputationResult> GetFileReputationAsync(string sha256Hash, CancellationToken cancellationToken = default)
        {
            var cleanHash = NormalizeHash(sha256Hash);
            if (string.IsNullOrWhiteSpace(cleanHash))
            {
                return CreateDefaultReputation(cleanHash);
            }

            using var timeoutCts = CreateLinkedTimeout(cancellationToken);
            try
            {
                var uri = $"{_baseUrl}/api/reputation?hash={Uri.EscapeDataString(cleanHash)}";
                using var response = await _httpClient.GetAsync(uri, timeoutCts.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
                    var result = await JsonSerializer.DeserializeAsync<FileReputationResult>(stream, JsonOptions, timeoutCts.Token).ConfigureAwait(false);
                    if (result != null)
                    {
                        if (string.IsNullOrWhiteSpace(result.Sha256))
                        {
                            result.Sha256 = cleanHash;
                        }
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CloudIntelligence] Reputation check failed for {cleanHash}: {ex.Message}");
            }

            return CreateDefaultReputation(cleanHash);
        }

        public async Task<bool> ReportReputationAsync(
            string sha256Hash,
            string fileName,
            long fileSizeBytes,
            string vote = "safe",
            CancellationToken cancellationToken = default)
        {
            var cleanHash = NormalizeHash(sha256Hash);
            if (string.IsNullOrWhiteSpace(cleanHash))
            {
                return false;
            }

            using var timeoutCts = CreateLinkedTimeout(cancellationToken);
            try
            {
                var uri = $"{_baseUrl}/api/reputation/report";
                var payload = new ReputationReportRequest
                {
                    Sha256 = cleanHash,
                    FileName = Path.GetFileName(fileName ?? "file.bin"),
                    FileSize = Math.Max(0, fileSizeBytes),
                    Vote = string.Equals(vote, "malware", StringComparison.OrdinalIgnoreCase) ? "malware" : "safe"
                };

                var json = JsonSerializer.Serialize(payload, JsonOptions);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await _httpClient.PostAsync(uri, content, timeoutCts.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
                    var res = await JsonSerializer.DeserializeAsync<ReputationReportResponse>(stream, JsonOptions, timeoutCts.Token).ConfigureAwait(false);
                    return res?.Success == true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CloudIntelligence] Reputation report failed: {ex.Message}");
            }

            return false;
        }

        public async Task<DomainHealthResult> GetDomainHealthAsync(string domain, CancellationToken cancellationToken = default)
        {
            var cleanDomain = CleanDomain(domain);
            if (string.IsNullOrWhiteSpace(cleanDomain))
            {
                return new DomainHealthResult { Domain = domain ?? string.Empty, Status = "unknown" };
            }

            using var timeoutCts = CreateLinkedTimeout(cancellationToken);
            try
            {
                var uri = $"{_baseUrl}/api/domain/health?domain={Uri.EscapeDataString(cleanDomain)}";
                using var response = await _httpClient.GetAsync(uri, timeoutCts.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
                    var result = await JsonSerializer.DeserializeAsync<DomainHealthResult>(stream, JsonOptions, timeoutCts.Token).ConfigureAwait(false);
                    if (result != null)
                    {
                        if (string.IsNullOrWhiteSpace(result.Domain)) result.Domain = cleanDomain;
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CloudIntelligence] Domain health check failed for {cleanDomain}: {ex.Message}");
            }

            return new DomainHealthResult { Domain = cleanDomain, Status = "unknown" };
        }

        public async Task<LanPeersResult> GetLanPeersAsync(string sha256Hash, string? localLanIp = null, CancellationToken cancellationToken = default)
        {
            var cleanHash = NormalizeHash(sha256Hash);
            if (string.IsNullOrWhiteSpace(cleanHash))
            {
                return new LanPeersResult { TotalPeers = 0, Peers = new List<LanPeerItem>() };
            }

            var lanIp = !string.IsNullOrWhiteSpace(localLanIp) ? localLanIp : GetLocalLanIp();

            using var timeoutCts = CreateLinkedTimeout(cancellationToken);
            try
            {
                var uri = $"{_baseUrl}/api/p2p/peers?hash={Uri.EscapeDataString(cleanHash)}&lan_ip={Uri.EscapeDataString(lanIp)}";
                using var response = await _httpClient.GetAsync(uri, timeoutCts.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
                    var result = await JsonSerializer.DeserializeAsync<LanPeersResult>(stream, JsonOptions, timeoutCts.Token).ConfigureAwait(false);
                    if (result != null) return result;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CloudIntelligence] Get LAN peers failed: {ex.Message}");
            }

            return new LanPeersResult { TotalPeers = 0, Peers = new List<LanPeerItem>() };
        }

        public async Task<bool> AnnounceLanPeerAsync(
            string peerId,
            string sha256Hash,
            string? localLanIp = null,
            int port = 6881,
            int completedChunks = 0,
            int totalChunks = 0,
            CancellationToken cancellationToken = default)
        {
            var cleanHash = NormalizeHash(sha256Hash);
            if (string.IsNullOrWhiteSpace(cleanHash))
            {
                return false;
            }

            var lanIp = !string.IsNullOrWhiteSpace(localLanIp) ? localLanIp : GetLocalLanIp();

            using var timeoutCts = CreateLinkedTimeout(cancellationToken);
            try
            {
                var uri = $"{_baseUrl}/api/p2p/announce";
                var payload = new PeerAnnounceRequest
                {
                    PeerId = string.IsNullOrWhiteSpace(peerId) ? Environment.MachineName : peerId,
                    Sha256 = cleanHash,
                    LanIp = lanIp,
                    Port = port > 0 ? port : 6881,
                    CompletedChunks = Math.Max(0, completedChunks),
                    TotalChunks = Math.Max(0, totalChunks)
                };

                var json = JsonSerializer.Serialize(payload, JsonOptions);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await _httpClient.PostAsync(uri, content, timeoutCts.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
                    var res = await JsonSerializer.DeserializeAsync<PeerAnnounceResponse>(stream, JsonOptions, timeoutCts.Token).ConfigureAwait(false);
                    return res?.Success == true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CloudIntelligence] Announce LAN peer failed: {ex.Message}");
            }

            return false;
        }

        public async Task<MirrorsResult> GetMirrorsAsync(string sha256Hash, CancellationToken cancellationToken = default)
        {
            var cleanHash = NormalizeHash(sha256Hash);
            if (string.IsNullOrWhiteSpace(cleanHash))
            {
                return new MirrorsResult { Sha256 = string.Empty, Mirrors = new List<string>() };
            }

            using var timeoutCts = CreateLinkedTimeout(cancellationToken);
            try
            {
                var uri = $"{_baseUrl}/api/mirrors?hash={Uri.EscapeDataString(cleanHash)}";
                using var response = await _httpClient.GetAsync(uri, timeoutCts.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
                    var result = await JsonSerializer.DeserializeAsync<MirrorsResult>(stream, JsonOptions, timeoutCts.Token).ConfigureAwait(false);
                    if (result != null) return result;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CloudIntelligence] Get mirrors failed: {ex.Message}");
            }

            return new MirrorsResult { Sha256 = cleanHash, Mirrors = new List<string>() };
        }

        public string ComputeSha256(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                data = Array.Empty<byte>();
            }
            var hashBytes = SHA256.HashData(data);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        public string ComputeSha256(string input)
        {
            if (string.IsNullOrEmpty(input)) input = string.Empty;
            return ComputeSha256(Encoding.UTF8.GetBytes(input));
        }

        public async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
            {
                return string.Empty;
            }

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 131072, useAsync: true);
                var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                return Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CloudIntelligence] File SHA-256 computation failed for {filePath}: {ex.Message}");
                return string.Empty;
            }
        }

        public string ExtractOrComputeSha256(string url, string? fileName = null)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;

            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    // 1. Check for SHA-256 in query string parameters (e.g. hash=, sha256=, checksum=, digest=)
                    if (!string.IsNullOrWhiteSpace(uri.Query))
                    {
                        var query = uri.Query.TrimStart('?');
                        var parts = query.Split('&');
                        foreach (var p in parts)
                        {
                            var kv = p.Split('=');
                            if (kv.Length == 2)
                            {
                                var key = kv[0].ToLowerInvariant();
                                if (key is "sha256" or "hash" or "checksum" or "sha" or "filehash" or "digest")
                                {
                                    var val = Uri.UnescapeDataString(kv[1]).Trim();
                                    if (Regex.IsMatch(val, "^[0-9a-fA-F]{64}$"))
                                    {
                                        return val.ToLowerInvariant();
                                    }
                                }
                            }
                        }
                    }

                    // 2. Check path segments (evaluated even when uri.Query is empty)
                    foreach (var seg in uri.Segments)
                    {
                        var cleanSeg = seg.Trim('/');
                        if (Regex.IsMatch(cleanSeg, "^[0-9a-fA-F]{64}$"))
                        {
                            return cleanSeg.ToLowerInvariant();
                        }
                    }
                }
            }
            catch { }

            // 3. Fallback: Fast deterministic fingerprint computed on canonical URL + optional clean fileName
            var canonical = url.Trim();
            if (!string.IsNullOrWhiteSpace(fileName) &&
                !fileName.StartsWith("download_", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fileName, "download.bin", StringComparison.OrdinalIgnoreCase))
            {
                canonical = $"{canonical}|{fileName.Trim().ToLowerInvariant()}";
            }
            return ComputeSha256(canonical);
        }

        public string GetLocalLanIp()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .OrderByDescending(ni => ni.GetIPProperties().GatewayAddresses.Any(g => g.Address != null && !IPAddress.IsLoopback(g.Address) && !g.Address.Equals(IPAddress.Any)))
                    .ThenByDescending(ni => !(ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                                              ni.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
                                              ni.Description.Contains("vEthernet", StringComparison.OrdinalIgnoreCase) ||
                                              ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
                                              ni.Description.Contains("TAP", StringComparison.OrdinalIgnoreCase) ||
                                              ni.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase) ||
                                              ni.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)))
                    .ThenByDescending(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                            ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);

                foreach (var ni in interfaces)
                {
                    var props = ni.GetIPProperties();
                    foreach (var ip in props.UnicastAddresses)
                    {
                        if (IsPrivateIPv4(ip.Address))
                        {
                            return ip.Address.ToString();
                        }
                    }
                }

                // Secondary pass: any non-loopback IPv4 with a default gateway
                foreach (var ni in interfaces)
                {
                    var props = ni.GetIPProperties();
                    if (props.GatewayAddresses.Any(g => g.Address != null && !IPAddress.IsLoopback(g.Address) && !g.Address.Equals(IPAddress.Any)))
                    {
                        foreach (var ip in props.UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip.Address))
                            {
                                return ip.Address.ToString();
                            }
                        }
                    }
                }
            }
            catch { }

            return "127.0.0.1";
        }

        private static bool IsPrivateIPv4(IPAddress ip)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip))
                return false;

            var bytes = ip.GetAddressBytes();
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12 (172.16.0.0 to 172.31.255.255)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;

            return false;
        }

        private static CancellationTokenSource CreateLinkedTimeout(CancellationToken userToken)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(userToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(DefaultTimeoutMs));
            return cts;
        }

        private static string NormalizeHash(string? hash)
        {
            if (string.IsNullOrWhiteSpace(hash)) return string.Empty;
            return hash.Trim().ToLowerInvariant();
        }

        private static string CleanDomain(string? domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return string.Empty;
            var d = domain.Trim().ToLowerInvariant();
            if (d.Contains("://"))
            {
                if (Uri.TryCreate(d, UriKind.Absolute, out var uri))
                {
                    return uri.Host;
                }
            }
            if (Uri.TryCreate("http://" + d, UriKind.Absolute, out var uriWithHttp))
            {
                return uriWithHttp.Host;
            }

            var slashIdx = d.IndexOf('/');
            if (slashIdx > 0) d = d[..slashIdx];
            var colonIdx = d.IndexOf(':');
            if (colonIdx > 0) d = d[..colonIdx];
            return d;
        }

        private static FileReputationResult CreateDefaultReputation(string hash)
        {
            return new FileReputationResult
            {
                Found = false,
                Sha256 = hash,
                Verdict = "unrated",
                SafetyScore = 100,
                Message = "New file - No community reports yet"
            };
        }
    }
}
