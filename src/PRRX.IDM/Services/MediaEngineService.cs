// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PRRX.IDM.Models;
using PRRX.IDM.Security;

namespace PRRX.IDM.Services
{
    public class DownloadProgressReport
    {
        public double Percentage { get; set; }
        public string Speed { get; set; } = "0 KB/s";
        public string Eta { get; set; } = "--:--";
        public string TotalSize { get; set; } = string.Empty;
        public string StatusMessage { get; set; } = string.Empty;
        public bool IsError { get; set; } = false;
    }

    public class StreamProgressState
    {
        public int PassCount { get; set; } = 0;
        public double LastRawPercent { get; set; } = 0.0;
        public double HighestMappedPercent { get; set; } = 1.0;
    }

    public interface IMediaEngineService
    {
        string EngineExecutablePath { get; }
        string? FfmpegDirectoryPath { get; }
        string? Aria2cExecutablePath { get; }
        string? ActiveCookiesPath { get; }
        bool IsCookiesConfigured { get; }
        bool IsEngineAvailable { get; }
        string? LastErrorMessage { get; }
        Task<(MediaProbeResult? Result, string? ErrorMessage)> ProbeMediaAsync(
            string url, 
            CancellationToken cancellationToken = default,
            string? cookies = null,
            string? userAgent = null);
        Task<bool> DownloadVideoAsync(
            string url,
            string formatId,
            string outputDirectory,
            IProgress<DownloadProgressReport>? progress = null,
            CancellationToken cancellationToken = default,
            string? targetContainer = null,
            string? destinationFilePath = null,
            string? cookies = null,
            string? userAgent = null);
        Task<bool> ConvertToMp3Async(
            string sourceUrlOrPath,
            string bitrate,
            string outputDirectory,
            IProgress<DownloadProgressReport>? progress = null,
            CancellationToken cancellationToken = default,
            string? destinationFilePath = null,
            string? cookies = null,
            string? userAgent = null);
        Task<bool> ConvertAudioAsync(
            string sourceUrlOrPath,
            string targetFormat,
            string qualityOrBitrate,
            string outputDirectory,
            bool trimRingtone = false,
            IProgress<DownloadProgressReport>? progress = null,
            CancellationToken cancellationToken = default,
            string? destinationFilePath = null,
            string? cookies = null,
            string? userAgent = null);
        Task<(bool Success, string Message)> UpdateEngineAsync(CancellationToken cancellationToken = default);
    }

    public class MediaEngineService : IMediaEngineService
    {
        private static readonly Regex AnsiRegex = new(@"\x1B\[[^@-~]*[@-~]", RegexOptions.Compiled);
        private static readonly Regex YtdlPercentRegex = new(@"\[download\]\s+([0-9]+(?:\.[0-9]+)?)%", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex YtdlSizeRegex = new(@"of\s+(?:~?\s*)([0-9.]+\s*[kKmMgGtTpP]?[iI]?[bB])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex YtdlSpeedRegex = new(@"at\s+([0-9.]+\s*[kKmMgGtTpP]?[iI]?[bB]/s|Unknown(?:\s+speed)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex YtdlEtaRegex = new(@"ETA\s+([0-9:]+|Unknown)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex YtdlInTimeRegex = new(@"in\s+([0-9:]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex YtdlFragRegex = new(@"\(frag\s+(\d+)/(\d+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex YtdlDestRegex = new(@"\[download\]\s+Destination:\s*(.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex Aria2ProgressRegex = new(
            @"\[#[a-f0-9]+\s+(\S+)\/(\S+)\((\d+)%\).*?DL:(\S+)(?:.*?ETA:(\S+))?\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IConfigurationService? _configService;
        private readonly ITelegramLinkResolver _telegramResolver = new TelegramLinkResolver();
        private string _lastNonZeroSpeed = "Calculating...";

        public string EngineExecutablePath { get; private set; }
        public string? FfmpegDirectoryPath { get; private set; }
        public string? Aria2cExecutablePath { get; private set; }

        public string? LastErrorMessage { get; private set; }
        public string? ActiveCookiesPath => GetBestAvailableCookiesFile();
        public bool IsCookiesConfigured => !string.IsNullOrWhiteSpace(ActiveCookiesPath) && File.Exists(ActiveCookiesPath);
        public bool IsEngineAvailable => File.Exists(EngineExecutablePath);

        public static string? EnsureNetscapeCookieFormat(string? rawCookieData, string? targetUrl = null)
        {
            if (string.IsNullOrWhiteSpace(rawCookieData)) return null;

            var trimmed = rawCookieData.Trim();
            if (trimmed.StartsWith("# Netscape HTTP Cookie File", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("# HTTP Cookie File", StringComparison.OrdinalIgnoreCase))
            {
                var nLines = trimmed.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var validLines = new StringBuilder();
                validLines.AppendLine("# Netscape HTTP Cookie File");
                validLines.AppendLine("# http://curl.haxx.se/rfc/cookie_spec.html");
                validLines.AppendLine();
                int validCount = 0;
                foreach (var line in nLines)
                {
                    if (line.StartsWith("#")) continue;
                    var parts = line.Split('\t');
                    if (parts.Length >= 7)
                    {
                        validLines.AppendLine(line);
                        validCount++;
                    }
                }
                return validCount > 0 ? validLines.ToString() : null;
            }

            // Determine target domain from URL
            var domain = ".youtube.com";
            if (!string.IsNullOrWhiteSpace(targetUrl))
            {
                try
                {
                    if (Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri))
                    {
                        var h = uri.Host.TrimStart('.');
                        if (h.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase))
                        {
                            domain = ".youtube.com";
                        }
                        else if (!string.IsNullOrEmpty(h))
                        {
                            domain = "." + h;
                        }
                    }
                }
                catch { }
            }

            var sb = new StringBuilder();
            sb.AppendLine("# Netscape HTTP Cookie File");
            sb.AppendLine("# http://curl.haxx.se/rfc/cookie_spec.html");
            sb.AppendLine("# Automatically formatted by PRRX IDM");
            sb.AppendLine();

            var lines = trimmed.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool hasValidTabs = lines.Any(l => !l.StartsWith("#") && l.Contains('\t') && l.Split('\t').Length >= 7);
            if (hasValidTabs)
            {
                int validCount = 0;
                foreach (var line in lines)
                {
                    if (line.StartsWith("#")) continue;
                    var parts = line.Split('\t');
                    if (parts.Length >= 7)
                    {
                        sb.AppendLine(line);
                        validCount++;
                    }
                }
                return validCount > 0 ? sb.ToString() : null;
            }

            // Parse key-value cookie pairs: "name=val; name2=val2" or newline-separated pairs
            var pairs = trimmed.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in pairs)
            {
                var p = pair.Trim();
                if (string.IsNullOrWhiteSpace(p) || p.StartsWith("#")) continue;

                var eqIdx = p.IndexOf('=');
                if (eqIdx <= 0) continue;

                var name = p.Substring(0, eqIdx).Trim().Replace("\t", "").Replace("\r", "").Replace("\n", "");
                var val = p.Substring(eqIdx + 1).Trim().Replace("\t", "").Replace("\r", "").Replace("\n", "");

                if (string.IsNullOrEmpty(name)) continue;

                if (seen.Add(name))
                {
                    sb.AppendLine($"{domain}\tTRUE\t/\tTRUE\t2147483647\t{name}\t{val}");
                    if (domain.Contains("youtube.com"))
                    {
                        sb.AppendLine($".google.com\tTRUE\t/\tTRUE\t2147483647\t{name}\t{val}");
                    }
                }
            }

            if (seen.Count == 0) return null;
            return sb.ToString();
        }

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void SaveCachedCookies(string cookies, string? targetUrl = null)
        {
            if (string.IsNullOrWhiteSpace(cookies)) return;
            try
            {
                var netscape = EnsureNetscapeCookieFormat(cookies, targetUrl);
                if (string.IsNullOrWhiteSpace(netscape)) return;

                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PRRX.IDM");
                Directory.CreateDirectory(dir);
                var cookieFile = Path.Combine(dir, "youtube_cookies.txt");
                File.WriteAllText(cookieFile, netscape, Utf8NoBom);
            }
            catch { }
        }

        private static void ValidateOrRepairCookieFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;
                var rawBytes = File.ReadAllBytes(filePath);
                if (rawBytes.Length == 0) return;

                // Strip UTF-8 BOM if present (0xEF, 0xBB, 0xBF)
                int startOffset = 0;
                if (rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF)
                {
                    startOffset = 3;
                }

                var text = Utf8NoBom.GetString(rawBytes, startOffset, rawBytes.Length - startOffset);
                var firstLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

                if (!firstLine.StartsWith("# Netscape", StringComparison.OrdinalIgnoreCase) &&
                    !firstLine.StartsWith("# HTTP Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    var converted = EnsureNetscapeCookieFormat(text);
                    if (!string.IsNullOrWhiteSpace(converted))
                    {
                        File.WriteAllText(filePath, converted, Utf8NoBom);
                    }
                    else
                    {
                        try { File.Delete(filePath); } catch { }
                    }
                }
                else if (startOffset > 0)
                {
                    // Re-save without BOM
                    File.WriteAllText(filePath, text, Utf8NoBom);
                }
            }
            catch { }
        }

        public static string? GetBestAvailableCookiesFile()
        {
            try
            {
                var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PRRX.IDM", "youtube_cookies.txt");
                if (File.Exists(appData) && new FileInfo(appData).Length > 0)
                {
                    ValidateOrRepairCookieFile(appData);
                    if (File.Exists(appData)) return appData;
                }

                var appDataAlt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PRRX.IDM", "cookies.txt");
                if (File.Exists(appDataAlt) && new FileInfo(appDataAlt).Length > 0)
                {
                    ValidateOrRepairCookieFile(appDataAlt);
                    if (File.Exists(appDataAlt)) return appDataAlt;
                }

                var baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cookies.txt");
                if (File.Exists(baseDir) && new FileInfo(baseDir).Length > 0)
                {
                    ValidateOrRepairCookieFile(baseDir);
                    if (File.Exists(baseDir)) return baseDir;
                }

                var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "cookies.txt");
                if (File.Exists(downloads) && new FileInfo(downloads).Length > 0)
                {
                    ValidateOrRepairCookieFile(downloads);
                    if (File.Exists(downloads)) return downloads;
                }
            }
            catch { }
            return null;
        }

        private static void AttachCookiesAndUserAgent(
            ProcessStartInfo startInfo,
            string? cookies,
            string? userAgent,
            out string? tempCookiePath,
            string? targetUrl = null,
            bool bypassCookies = false)
        {
            tempCookiePath = null;
            if (bypassCookies || string.Equals(cookies, "NONE", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(userAgent))
                {
                    startInfo.ArgumentList.Add("--user-agent");
                    startInfo.ArgumentList.Add(userAgent);
                }
                return;
            }

            if (!string.IsNullOrWhiteSpace(cookies))
            {
                try
                {
                    var netscape = EnsureNetscapeCookieFormat(cookies, targetUrl);
                    if (!string.IsNullOrWhiteSpace(netscape))
                    {
                        tempCookiePath = Path.Combine(Path.GetTempPath(), $"prrx_cookie_{Guid.NewGuid():N}.txt");
                        File.WriteAllText(tempCookiePath, netscape, Utf8NoBom);
                        startInfo.ArgumentList.Add("--cookies");
                        startInfo.ArgumentList.Add(tempCookiePath);
                        SaveCachedCookies(netscape, targetUrl);
                    }
                }
                catch { }
            }
            else
            {
                var cached = GetBestAvailableCookiesFile();
                if (!string.IsNullOrWhiteSpace(cached) && File.Exists(cached))
                {
                    startInfo.ArgumentList.Add("--cookies");
                    startInfo.ArgumentList.Add(cached);
                }
            }

            if (!string.IsNullOrWhiteSpace(userAgent))
            {
                startInfo.ArgumentList.Add("--user-agent");
                startInfo.ArgumentList.Add(userAgent);
            }
        }

        public MediaEngineService(IConfigurationService? configService = null)
        {
            _configService = configService;

            var baseAppDir = AppDomain.CurrentDomain.BaseDirectory;
            var localBin = Path.Combine(baseAppDir, "bin", "yt-dlp.exe");
            var devBin = FindFileInAncestors(baseAppDir, Path.Combine("bin", "yt-dlp.exe"));

            if (File.Exists(localBin))
            {
                EngineExecutablePath = localBin;
            }
            else if (!string.IsNullOrWhiteSpace(devBin) && File.Exists(devBin))
            {
                EngineExecutablePath = devBin;
            }
            else
            {
                EngineExecutablePath = "yt-dlp";
            }

            // Locate FFmpeg
            var localFfmpeg = Path.Combine(baseAppDir, "bin");
            var devFfmpeg = FindFileInAncestors(baseAppDir, Path.Combine("bin", "ffmpeg.exe"));

            if (File.Exists(Path.Combine(localFfmpeg, "ffmpeg.exe")))
            {
                FfmpegDirectoryPath = localFfmpeg;
            }
            else if (!string.IsNullOrWhiteSpace(devFfmpeg))
            {
                FfmpegDirectoryPath = Path.GetDirectoryName(devFfmpeg);
            }
            else
            {
                FfmpegDirectoryPath = null;
            }

            // Locate Aria2c
            var localAria2 = Path.Combine(baseAppDir, "bin", "aria2c.exe");
            var devAria2 = FindFileInAncestors(baseAppDir, Path.Combine("bin", "aria2c.exe"));

            if (File.Exists(localAria2))
            {
                Aria2cExecutablePath = localAria2;
            }
            else if (!string.IsNullOrWhiteSpace(devAria2) && File.Exists(devAria2))
            {
                Aria2cExecutablePath = devAria2;
            }
            else
            {
                Aria2cExecutablePath = null;
            }
        }

        private static string? FindFileInAncestors(string startDir, string relativePath)
        {
            try
            {
                var current = new DirectoryInfo(startDir);
                for (int i = 0; i < 5 && current != null; i++)
                {
                    var candidate = Path.Combine(current.FullName, relativePath);
                    if (File.Exists(candidate)) return candidate;
                    current = current.Parent;
                }
            }
            catch { }
            return null;
        }

        private static string? _cachedNodePath;
        private static bool _nodePathChecked;

        public static string? FindNodeJsExecutable()
        {
            if (_nodePathChecked) return _cachedNodePath;
            _nodePathChecked = true;

            try
            {
                var standardPaths = new[]
                {
                    @"C:\Program Files\nodejs\node.exe",
                    @"C:\Program Files (x86)\nodejs\node.exe",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\node\node.exe")
                };

                foreach (var p in standardPaths)
                {
                    if (File.Exists(p))
                    {
                        _cachedNodePath = p;
                        return p;
                    }
                }

                var pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrWhiteSpace(pathEnv))
                {
                    var dirs = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var dir in dirs)
                    {
                        var candidate = Path.Combine(dir.Trim(), "node.exe");
                        if (File.Exists(candidate))
                        {
                            _cachedNodePath = candidate;
                            return candidate;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static bool IsStreamingUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (url.StartsWith("tg://", StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var host = uri.Host.ToLowerInvariant();
                    if (host.Contains("youtube.com") || host.Contains("youtu.be") ||
                        host.Contains("tiktok.com") || host.Contains("instagram.com") ||
                        host.Contains("facebook.com") || host.Contains("fb.watch") ||
                        host.Contains("twitter.com") || host.Contains("x.com") ||
                        host.Contains("vimeo.com") || host.Contains("soundcloud.com") ||
                        host.Contains("bilibili.com") || host.Contains("dailymotion.com") ||
                        host.Contains("twitch.tv") || host.Contains("reddit.com") ||
                        uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                        uri.AbsolutePath.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private void AttachCommonArguments(ProcessStartInfo startInfo)
        {
            if (!string.IsNullOrWhiteSpace(FfmpegDirectoryPath))
            {
                startInfo.ArgumentList.Add("--ffmpeg-location");
                startInfo.ArgumentList.Add(FfmpegDirectoryPath);
            }

            var nodeExe = FindNodeJsExecutable();
            if (!string.IsNullOrWhiteSpace(nodeExe))
            {
                startInfo.ArgumentList.Add("--js-runtimes");
                startInfo.ArgumentList.Add($"node:{nodeExe}");
            }
            else
            {
                startInfo.ArgumentList.Add("--js-runtimes");
                startInfo.ArgumentList.Add("node");
            }

            // Multi-client extraction (Android, Web, TV, iOS) to bypass bot blocks and SABR issues
            startInfo.ArgumentList.Add("--extractor-args");
            startInfo.ArgumentList.Add("youtube:player_client=android,web,tv,ios;youtubetab:approximate_date");

            // Multi-Connection Turbo Speed Acceleration
            var connections = Math.Clamp(_configService?.CurrentConfig.TurboConnectionCount ?? 32, 8, 64);
            startInfo.ArgumentList.Add("--concurrent-fragments");
            startInfo.ArgumentList.Add(connections.ToString());

            // High-throughput streaming buffer
            startInfo.ArgumentList.Add("--buffer-size");
            startInfo.ArgumentList.Add("8M");
            startInfo.ArgumentList.Add("--http-chunk-size");
            startInfo.ArgumentList.Add("10M");

            startInfo.ArgumentList.Add("--socket-timeout");
            startInfo.ArgumentList.Add("10"); // Resilient 10s socket timeout
            startInfo.ArgumentList.Add("--retries");
            startInfo.ArgumentList.Add("10");
            startInfo.ArgumentList.Add("--fragment-retries");
            startInfo.ArgumentList.Add("10");
            startInfo.ArgumentList.Add("--no-warnings");
            startInfo.ArgumentList.Add("--no-color");
            startInfo.ArgumentList.Add("--no-check-certificates");

            // Anti-stall progress reporting when stdout is redirected
            startInfo.ArgumentList.Add("--progress");
            startInfo.ArgumentList.Add("--progress-delta");
            startInfo.ArgumentList.Add("0.1");
            startInfo.ArgumentList.Add("--continue");
            startInfo.ArgumentList.Add("--force-overwrites");
            startInfo.ArgumentList.Add("--postprocessor-args");
            startInfo.ArgumentList.Add("ffmpeg:-y -nostdin");

            // Attach Proxy if configured
            if (_configService?.CurrentConfig.UseProxy == true && !string.IsNullOrWhiteSpace(_configService.CurrentConfig.ProxyHost))
            {
                var scheme = _configService.CurrentConfig.ProxyType switch
                {
                    ProxyType.Socks5 => "socks5",
                    ProxyType.Socks4 => "socks4",
                    ProxyType.Https => "https",
                    _ => "http"
                };
                string proxyUrl;
                if (_configService.CurrentConfig.UseProxyAuth && !string.IsNullOrWhiteSpace(_configService.CurrentConfig.ProxyUsername))
                {
                    var user = Uri.EscapeDataString(_configService.CurrentConfig.ProxyUsername);
                    var pass = Uri.EscapeDataString(_configService.CurrentConfig.ProxyPassword);
                    proxyUrl = $"{scheme}://{user}:{pass}@{_configService.CurrentConfig.ProxyHost}:{_configService.CurrentConfig.ProxyPort}";
                }
                else
                {
                    proxyUrl = $"{scheme}://{_configService.CurrentConfig.ProxyHost}:{_configService.CurrentConfig.ProxyPort}";
                }
                startInfo.ArgumentList.Add("--proxy");
                startInfo.ArgumentList.Add(proxyUrl);
            }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "Unknown";
            if (bytes >= 1024L * 1024L * 1024L) return $"~ {(bytes / (1024.0 * 1024.0 * 1024.0)):F1} GB";
            if (bytes >= 1024L * 1024L) return $"~ {(bytes / (1024.0 * 1024.0)):F1} MB";
            if (bytes >= 1024L) return $"~ {(bytes / 1024.0):F1} KB";
            return $"{bytes} B";
        }

        public async Task<(MediaProbeResult? Result, string? ErrorMessage)> ProbeMediaAsync(
            string url, 
            CancellationToken cancellationToken = default, 
            string? cookies = null, 
            string? userAgent = null)
        {
            if (!SecurityGuard.ValidateUrl(url, out var safeUrl, out var error))
            {
                return (null, error);
            }

            // Fast zero-latency Telegram resolver (direct CDN extraction)
            if (_telegramResolver.IsTelegramUrl(safeUrl))
            {
                try
                {
                    var tgMedia = await _telegramResolver.ResolveTelegramMediaAsync(safeUrl, cancellationToken);
                    if (tgMedia != null && tgMedia.IsDirectDownloadable)
                    {
                        var ext = System.IO.Path.GetExtension(tgMedia.FileName).TrimStart('.').ToLowerInvariant();
                        if (string.IsNullOrWhiteSpace(ext)) ext = tgMedia.MediaType == "video" ? "mp4" : "bin";

                        var probe = new MediaProbeResult
                        {
                            Id = safeUrl,
                            Title = tgMedia.Title,
                            ThumbnailUrl = tgMedia.ThumbnailUrl,
                            PublisherName = !string.IsNullOrWhiteSpace(tgMedia.ChannelName) ? $"Telegram @{tgMedia.ChannelName}" : "Telegram",
                            Formats = new List<MediaFormat>
                            {
                                new MediaFormat
                                {
                                    FormatId = "telegram_cdn_stream",
                                    Resolution = !string.IsNullOrWhiteSpace(tgMedia.FormattedSize) ? $"{tgMedia.FormattedSize} (Direct CDN)" : "Direct Telegram Stream",
                                    Extension = ext,
                                    Note = "High-Speed Parallel Fiber Stream",
                                    HasVideo = tgMedia.MediaType == "video",
                                    HasAudio = true,
                                    DirectDownloadUrl = tgMedia.DirectStreamUrl
                                }
                            }
                        };
                        return (probe, null);
                    }
                }
                catch
                {
                    // Fall back to yt-dlp native Telegram extractor
                }
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromMilliseconds(25000));

            string json = "";
            string err = "";
            int exitCode = -1;

            try
            {
                var extract = await ExecuteYtDlpJsonExtractionAsync(safeUrl, cookies, userAgent, linkedCts.Token, bypassCookies: false);
                json = extract.Json;
                err = extract.Error;
                exitCode = extract.ExitCode;
            }
            catch (OperationCanceledException)
            {
                return (null, "Probe timed out after 25s");
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }

            // Automatic fallback without cookies if cookie/bot check caused failure
            if ((string.IsNullOrWhiteSpace(json) || exitCode != 0) && (!string.IsNullOrWhiteSpace(cookies) || IsCookiesConfigured) && !linkedCts.IsCancellationRequested)
            {
                try
                {
                    var fallback = await ExecuteYtDlpJsonExtractionAsync(safeUrl, null, userAgent, linkedCts.Token, bypassCookies: true);
                    if (!string.IsNullOrWhiteSpace(fallback.Json) && fallback.ExitCode == 0)
                    {
                        json = fallback.Json;
                        err = fallback.Error;
                        exitCode = fallback.ExitCode;

                        // Clean up bad cookie file
                        try
                        {
                            var cached = GetBestAvailableCookiesFile();
                            if (!string.IsNullOrWhiteSpace(cached) && File.Exists(cached))
                            {
                                File.Delete(cached);
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            if (string.IsNullOrWhiteSpace(json) || exitCode != 0)
            {
                var cleanErr = !string.IsNullOrWhiteSpace(err) ? err.Trim() : "Could not retrieve media details.";
                if (cleanErr.Contains("Sign in to confirm you're not a bot", StringComparison.OrdinalIgnoreCase) || 
                    cleanErr.Contains("LOGIN_REQUIRED", StringComparison.OrdinalIgnoreCase))
                {
                    cleanErr = "YouTube Bot Protection: Video requires fresh verification cookies.";
                }
                else if (cleanErr.Contains("ERROR: ", StringComparison.OrdinalIgnoreCase))
                {
                    cleanErr = cleanErr.Substring(cleanErr.IndexOf("ERROR: ", StringComparison.OrdinalIgnoreCase) + 7);
                }
                return (null, cleanErr);
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                var title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "Unknown Media" : "Unknown Media";
                
                // Publisher / Channel details
                var uploader = root.TryGetProperty("uploader", out var upProp) ? upProp.GetString() : null;
                var channel = root.TryGetProperty("channel", out var chProp) ? chProp.GetString() : null;
                var publisherName = !string.IsNullOrWhiteSpace(channel) ? channel : (!string.IsNullOrWhiteSpace(uploader) ? uploader : "Online Creator");

                // Published Date
                var rawDate = root.TryGetProperty("upload_date", out var dateProp) ? dateProp.GetString() ?? "" : "";
                var formattedDate = "Unknown Date";
                if (rawDate.Length == 8 && DateTime.TryParseExact(rawDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    formattedDate = dt.ToString("MMM dd, yyyy");
                }

                // View Count
                var viewCount = root.TryGetProperty("view_count", out var viewsProp) ? viewsProp.GetInt64() : 0;
                var viewFormatted = FormatViewCount(viewCount);

                // Duration
                var durSeconds = root.TryGetProperty("duration", out var durProp) ? durProp.GetDouble() : 0;
                var durFormatted = FormatDuration(durSeconds);

                // Filesize or approximate filesize at root
                long rootFileSize = 0;
                if (root.TryGetProperty("filesize", out var fsProp) && fsProp.ValueKind == JsonValueKind.Number)
                {
                    rootFileSize = fsProp.GetInt64();
                }
                else if (root.TryGetProperty("filesize_approx", out var fsaProp) && fsaProp.ValueKind == JsonValueKind.Number)
                {
                    rootFileSize = fsaProp.GetInt64();
                }

                // Bitrate (tbr / abr / vbr in kbps)
                double rootTbr = 0;
                if (root.TryGetProperty("tbr", out var tbrProp) && tbrProp.ValueKind == JsonValueKind.Number)
                {
                    rootTbr = tbrProp.GetDouble();
                }

                if (rootFileSize <= 0 && durSeconds > 0 && rootTbr > 0)
                {
                    rootFileSize = (long)(durSeconds * (rootTbr * 1000.0 / 8.0));
                }

                // Description
                var description = root.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" : "";
                if (description.Length > 280)
                {
                    description = description.Substring(0, 277) + "...";
                }

                // Music / Artist Metadata
                var artist = root.TryGetProperty("artist", out var artProp) ? artProp.GetString() : null;
                var creator = root.TryGetProperty("creator", out var crProp) ? crProp.GetString() : null;
                var track = root.TryGetProperty("track", out var trProp) ? trProp.GetString() : null;
                var album = root.TryGetProperty("album", out var albProp) ? albProp.GetString() : null;
                var genre = root.TryGetProperty("genre", out var genProp) ? genProp.GetString() : null;

                var isMusic = !string.IsNullOrWhiteSpace(artist) || !string.IsNullOrWhiteSpace(track) || 
                              title.Contains("remix", StringComparison.OrdinalIgnoreCase) || 
                              title.Contains("official music video", StringComparison.OrdinalIgnoreCase) || 
                              title.Contains("song", StringComparison.OrdinalIgnoreCase) || 
                              title.Contains("audio", StringComparison.OrdinalIgnoreCase);

                var musicianName = !string.IsNullOrWhiteSpace(artist) ? artist : (!string.IsNullOrWhiteSpace(creator) ? creator : publisherName);

                var result = new MediaProbeResult
                {
                    Id = id,
                    Title = title,
                    PublisherName = publisherName,
                    PublishedDate = formattedDate,
                    ViewCountFormatted = viewFormatted,
                    DurationSeconds = durSeconds,
                    DurationFormatted = durFormatted,
                    Description = description,
                    ThumbnailUrl = root.TryGetProperty("thumbnail", out var thumbProp) ? thumbProp.GetString() ?? "" : "",
                    IsMusic = isMusic,
                    Musician = musicianName,
                    Album = album ?? "Single Release",
                    Track = track ?? title,
                    Genre = genre ?? (isMusic ? "Music / Entertainment" : "Video")
                };

                // Compute real format sizes directly from yt-dlp formats metadata
                var (bestAudioSize, bestAudioBitrate) = ExtractBestAudioMetrics(root, durSeconds);
                var realFormats = ExtractFormatsWithRealSizes(root, durSeconds, isMusic, bestAudioSize, bestAudioBitrate, rootFileSize);

                if (realFormats.Count > 0)
                {
                    foreach (var rf in realFormats)
                    {
                        result.Formats.Add(rf);
                    }
                }
                else
                {
                    // Fallback to reasonable bitrates if formats array empty
                    var baseDur = durSeconds > 0 ? durSeconds : (isMusic ? 210.0 : 240.0);
                    long size1080 = rootFileSize > 0 ? rootFileSize : (long)(baseDur * 2.8 * 1024 * 1024 / 8.0);
                    long size720 = (long)(baseDur * 1.5 * 1024 * 1024 / 8.0);
                    long size480 = (long)(baseDur * 0.8 * 1024 * 1024 / 8.0);
                    long size360 = (long)(baseDur * 0.45 * 1024 * 1024 / 8.0);

                    result.Formats.Add(new MediaFormat { FormatId = "bestvideo[height<=1080]+bestaudio/best[height<=1080]/best", Resolution = "1080p Full HD", Extension = "mp4", FileSizeBytes = size1080, EstimatedSizeFormatted = FormatBytes(size1080), HasVideo = true, HasAudio = true });
                    result.Formats.Add(new MediaFormat { FormatId = "bestvideo[height<=720]+bestaudio/best[height<=720]/best", Resolution = "720p HD", Extension = "mp4", FileSizeBytes = size720, EstimatedSizeFormatted = FormatBytes(size720), HasVideo = true, HasAudio = true });
                    result.Formats.Add(new MediaFormat { FormatId = "bestvideo[height<=480]+bestaudio/best[height<=480]/best", Resolution = "480p", Extension = "mp4", FileSizeBytes = size480, EstimatedSizeFormatted = FormatBytes(size480), HasVideo = true, HasAudio = true });
                    result.Formats.Add(new MediaFormat { FormatId = "bestvideo[height<=360]+bestaudio/best[height<=360]/best", Resolution = "360p", Extension = "mp4", FileSizeBytes = size360, EstimatedSizeFormatted = FormatBytes(size360), HasVideo = true, HasAudio = true });
                }

                MemoryOptimizer.TrimMemory();
                return (result, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        private async Task<(string Json, string Error, int ExitCode)> ExecuteYtDlpJsonExtractionAsync(
            string safeUrl,
            string? cookiesToUse,
            string? userAgentToUse,
            CancellationToken token,
            bool bypassCookies = false)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = EngineExecutablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("--dump-json");
            startInfo.ArgumentList.Add("--no-playlist");
            startInfo.ArgumentList.Add("--skip-download");
            startInfo.ArgumentList.Add("--no-warnings");
            startInfo.ArgumentList.Add("--no-check-certificates");
            startInfo.ArgumentList.Add("--socket-timeout");
            startInfo.ArgumentList.Add("10");
            startInfo.ArgumentList.Add("--retries");
            startInfo.ArgumentList.Add("3");

            if (!string.IsNullOrWhiteSpace(FfmpegDirectoryPath))
            {
                startInfo.ArgumentList.Add("--ffmpeg-location");
                startInfo.ArgumentList.Add(FfmpegDirectoryPath);
            }

            var nodeExe = FindNodeJsExecutable();
            if (!string.IsNullOrWhiteSpace(nodeExe))
            {
                startInfo.ArgumentList.Add("--js-runtimes");
                startInfo.ArgumentList.Add($"node:{nodeExe}");
            }
            else
            {
                startInfo.ArgumentList.Add("--js-runtimes");
                startInfo.ArgumentList.Add("node");
            }

            // Multi-client extraction (Android, Web, TV, iOS) to bypass bot blocks and SABR issues
            startInfo.ArgumentList.Add("--extractor-args");
            startInfo.ArgumentList.Add("youtube:player_client=android,web,tv,ios;youtubetab:approximate_date");

            string? tempCookiePath = null;
            AttachCookiesAndUserAgent(startInfo, cookiesToUse, userAgentToUse, out tempCookiePath, safeUrl, bypassCookies);

            startInfo.ArgumentList.Add(safeUrl);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(token);
            var errorTask = process.StandardError.ReadToEndAsync(token);

            try
            {
                await process.WaitForExitAsync(token);
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                throw;
            }
            finally
            {
                try { if (!string.IsNullOrWhiteSpace(tempCookiePath) && File.Exists(tempCookiePath)) File.Delete(tempCookiePath); } catch { }
            }

            return (await outputTask, await errorTask, process.ExitCode);
        }

        private static (long AudioSize, double AudioBitrate) ExtractBestAudioMetrics(JsonElement root, double durSeconds)
        {
            long maxAudioSize = 0;
            double maxAudioBitrate = 0;

            if (root.TryGetProperty("formats", out var formatsProp) && formatsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var fmt in formatsProp.EnumerateArray())
                {
                    var vcodec = fmt.TryGetProperty("vcodec", out var vc) ? vc.GetString() : null;
                    var acodec = fmt.TryGetProperty("acodec", out var ac) ? ac.GetString() : null;

                    bool isAudioOnly = (string.Equals(vcodec, "none", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(vcodec)) &&
                                       !string.Equals(acodec, "none", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(acodec);

                    if (isAudioOnly)
                    {
                        long size = 0;
                        if (fmt.TryGetProperty("filesize", out var fs) && fs.ValueKind == JsonValueKind.Number && fs.GetInt64() > 0)
                        {
                            size = fs.GetInt64();
                        }
                        else if (fmt.TryGetProperty("filesize_approx", out var fsa) && fsa.ValueKind == JsonValueKind.Number && fsa.GetInt64() > 0)
                        {
                            size = fsa.GetInt64();
                        }

                        double abr = 0;
                        if (fmt.TryGetProperty("abr", out var abProp) && abProp.ValueKind == JsonValueKind.Number)
                        {
                            abr = abProp.GetDouble();
                        }
                        else if (fmt.TryGetProperty("tbr", out var tbProp) && tbProp.ValueKind == JsonValueKind.Number)
                        {
                            abr = tbProp.GetDouble();
                        }

                        if (size <= 0 && abr > 0 && durSeconds > 0)
                        {
                            size = (long)(durSeconds * abr * 1000.0 / 8.0);
                        }

                        if (size > maxAudioSize) maxAudioSize = size;
                        if (abr > maxAudioBitrate) maxAudioBitrate = abr;
                    }
                }
            }

            if (maxAudioSize <= 0 && durSeconds > 0)
            {
                maxAudioBitrate = 128.0;
                maxAudioSize = (long)(durSeconds * 128.0 * 1000.0 / 8.0);
            }

            return (maxAudioSize, maxAudioBitrate);
        }

        private static List<MediaFormat> ExtractFormatsWithRealSizes(
            JsonElement root,
            double durSeconds,
            bool isMusic,
            long bestAudioSize,
            double bestAudioBitrate,
            long rootFileSize)
        {
            var results = new List<MediaFormat>();
            var targetResolutions = new[] { 2160, 1440, 1080, 720, 480, 360, 240, 144 };

            if (isMusic || bestAudioSize > 0)
            {
                var audioBytes = bestAudioSize > 0 ? bestAudioSize : (durSeconds > 0 ? (long)(durSeconds * 320 * 1000 / 8.0) : 10_000_000L);
                results.Add(new MediaFormat
                {
                    FormatId = "ba/b",
                    Resolution = "High Quality Audio (320 kbps)",
                    Extension = "mp3",
                    FileSizeBytes = audioBytes,
                    EstimatedSizeFormatted = FormatBytes(audioBytes),
                    HasVideo = false,
                    HasAudio = true
                });
            }

            if (root.TryGetProperty("formats", out var formatsProp) && formatsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var targetH in targetResolutions)
                {
                    long bestStreamSize = 0;
                    bool matched = false;

                    foreach (var fmt in formatsProp.EnumerateArray())
                    {
                        int height = 0;
                        if (fmt.TryGetProperty("height", out var hProp) && hProp.ValueKind == JsonValueKind.Number)
                        {
                            height = hProp.GetInt32();
                        }

                        if (height <= 0) continue;

                        if (Math.Abs(height - targetH) <= 8 || (targetH == 720 && height is >= 700 and <= 720) || (targetH == 1080 && height is >= 1000 and <= 1080))
                        {
                            matched = true;
                            var vcodec = fmt.TryGetProperty("vcodec", out var vc) ? vc.GetString() : null;
                            var acodec = fmt.TryGetProperty("acodec", out var ac) ? ac.GetString() : null;

                            bool isVideoOnly = !string.Equals(vcodec, "none", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(vcodec) &&
                                               (string.Equals(acodec, "none", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(acodec));

                            long fmtSize = 0;
                            if (fmt.TryGetProperty("filesize", out var fs) && fs.ValueKind == JsonValueKind.Number && fs.GetInt64() > 0)
                            {
                                fmtSize = fs.GetInt64();
                            }
                            else if (fmt.TryGetProperty("filesize_approx", out var fsa) && fsa.ValueKind == JsonValueKind.Number && fsa.GetInt64() > 0)
                            {
                                fmtSize = fsa.GetInt64();
                            }

                            double vbr = 0;
                            if (fmt.TryGetProperty("vbr", out var vbProp) && vbProp.ValueKind == JsonValueKind.Number)
                            {
                                vbr = vbProp.GetDouble();
                            }
                            else if (fmt.TryGetProperty("tbr", out var tbProp) && tbProp.ValueKind == JsonValueKind.Number)
                            {
                                vbr = tbProp.GetDouble();
                            }

                            if (fmtSize <= 0 && vbr > 0 && durSeconds > 0)
                            {
                                fmtSize = (long)(durSeconds * vbr * 1000.0 / 8.0);
                            }

                            if (isVideoOnly && fmtSize > 0)
                            {
                                fmtSize += bestAudioSize;
                            }

                            if (fmtSize > bestStreamSize)
                            {
                                bestStreamSize = fmtSize;
                            }
                        }
                    }

                    if (matched || targetH is 1080 or 720 or 480 or 360)
                    {
                        if (bestStreamSize <= 0)
                        {
                            var baseDur = durSeconds > 0 ? durSeconds : 240.0;
                            double bitrateKbps = targetH switch
                            {
                                2160 => 9000,
                                1440 => 5500,
                                1080 => 2800,
                                720 => 1600,
                                480 => 850,
                                360 => 450,
                                240 => 250,
                                _ => 120
                            };
                            bestStreamSize = (long)(baseDur * bitrateKbps * 1000.0 / 8.0);
                        }

                        var label = targetH switch
                        {
                            2160 => "4K UHD (2160p)",
                            1440 => "2K QHD (1440p)",
                            1080 => "1080p Full HD",
                            720 => "720p HD",
                            _ => $"{targetH}p"
                        };

                        results.Add(new MediaFormat
                        {
                            FormatId = $"bestvideo[height<={targetH}]+bestaudio/best[height<={targetH}]/best",
                            Resolution = label,
                            Extension = "mp4",
                            FileSizeBytes = bestStreamSize,
                            EstimatedSizeFormatted = FormatBytes(bestStreamSize),
                            HasVideo = true,
                            HasAudio = true
                        });
                    }
                }
            }

            return results;
        }

        private static string FormatViewCount(long views)
        {
            if (views >= 1_000_000_000) return $"{(views / 1_000_000_000.0):F1}B views";
            if (views >= 1_000_000) return $"{(views / 1_000_000.0):F1}M views";
            if (views >= 1_000) return $"{(views / 1_000.0):F1}K views";
            if (views > 0) return $"{views:N0} views";
            return "Trending";
        }

        private static string FormatDuration(double seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1 
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}" 
                : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        public async Task<bool> DownloadVideoAsync(
            string url,
            string formatId,
            string outputDirectory,
            IProgress<DownloadProgressReport>? progress = null,
            CancellationToken cancellationToken = default,
            string? targetContainer = null,
            string? destinationFilePath = null,
            string? cookies = null,
            string? userAgent = null)
        {
            if (!SecurityGuard.ValidateUrl(url, out var safeUrl, out var error))
            {
                progress?.Report(new DownloadProgressReport { StatusMessage = error, IsError = true });
                return false;
            }

            Directory.CreateDirectory(outputDirectory);
            var outputTemplate = !string.IsNullOrWhiteSpace(destinationFilePath)
                ? Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(destinationFilePath) + ".%(ext)s")
                : Path.Combine(outputDirectory, "%(title)s.%(ext)s");

            var startInfo = new ProcessStartInfo
            {
                FileName = EngineExecutablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-f");
            var effectiveFormat = formatId;
            if (string.IsNullOrWhiteSpace(effectiveFormat) || effectiveFormat == "telegram_cdn_stream")
            {
                effectiveFormat = "best";
            }
            startInfo.ArgumentList.Add(effectiveFormat);
            startInfo.ArgumentList.Add("--newline");
            startInfo.ArgumentList.Add("--no-playlist");

            if (!string.IsNullOrWhiteSpace(targetContainer))
            {
                var cleanExt = targetContainer.Trim().TrimStart('.').ToLowerInvariant();
                if (cleanExt != "auto")
                {
                    startInfo.ArgumentList.Add("--remux-video");
                    startInfo.ArgumentList.Add(cleanExt);
                }
            }
            else
            {
                startInfo.ArgumentList.Add("--merge-output-format");
                startInfo.ArgumentList.Add("mp4");
            }

            // Attach anti-stall parameters
            AttachCommonArguments(startInfo);

            string? tempCookiePath = null;
            AttachCookiesAndUserAgent(startInfo, cookies, userAgent, out tempCookiePath, safeUrl);

            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(outputTemplate);
            startInfo.ArgumentList.Add(safeUrl);

            _lastNonZeroSpeed = "Calculating...";
            LastErrorMessage = null;

            try
            {
                var streamState = new StreamProgressState();
                using var process = new Process { StartInfo = startInfo };
                process.OutputDataReceived += (_, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;
                    ParseAndReportProgress(e.Data, progress, streamState);
                };

                process.ErrorDataReceived += (_, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;
                    LastErrorMessage = e.Data.Trim();
                    if (e.Data.Contains("ERROR:", StringComparison.OrdinalIgnoreCase) ||
                        e.Data.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase))
                    {
                        var msg = e.Data.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase)
                            ? "YouTube Authentication: Please ensure you are logged into YouTube in Chrome or load cookies.txt."
                            : e.Data.Trim();
                        progress?.Report(new DownloadProgressReport { StatusMessage = msg, IsError = true });
                    }
                    else
                    {
                        ParseAndReportProgress(e.Data, progress, streamState);
                    }
                };

                process.Start();
                try { process.StandardInput.Close(); } catch { }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch
                {
                    try { if (!process.HasExited) process.Kill(true); } catch { }
                    throw;
                }

                if (process.ExitCode == 0)
                {
                    progress?.Report(new DownloadProgressReport
                    {
                        Percentage = 100.0,
                        Speed = "0 KB/s",
                        Eta = "00:00",
                        StatusMessage = "Complete - Downloaded successfully"
                    });
                }

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(destinationFilePath) && !File.Exists(destinationFilePath))
                {
                    try
                    {
                        var baseName = Path.GetFileNameWithoutExtension(destinationFilePath);
                        var dir = Path.GetDirectoryName(destinationFilePath) ?? outputDirectory;
                        if (Directory.Exists(dir))
                        {
                            var match = Directory.GetFiles(dir, $"{baseName}.*")
                                .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                                            !f.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) &&
                                            !f.EndsWith(".aria2", StringComparison.OrdinalIgnoreCase) &&
                                            !f.EndsWith(".temp", StringComparison.OrdinalIgnoreCase) &&
                                            !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                                .FirstOrDefault();
                            if (match != null && !string.Equals(match, destinationFilePath, StringComparison.OrdinalIgnoreCase))
                            {
                                File.Move(match, destinationFilePath, true);
                            }
                        }
                    }
                    catch { }
                }

                if (process.ExitCode != 0 && !cancellationToken.IsCancellationRequested && (!string.IsNullOrWhiteSpace(cookies) || IsCookiesConfigured))
                {
                    try
                    {
                        var cached = GetBestAvailableCookiesFile();
                        if (!string.IsNullOrWhiteSpace(cached) && File.Exists(cached))
                        {
                            File.Delete(cached);
                        }
                    }
                    catch { }

                    progress?.Report(new DownloadProgressReport
                    {
                        Percentage = 5.0,
                        StatusMessage = "Retrying download with multi-client bypass (no cookies)..."
                    });

                    return await DownloadVideoAsync(
                        url,
                        formatId,
                        outputDirectory,
                        progress,
                        cancellationToken,
                        targetContainer,
                        destinationFilePath,
                        cookies: "NONE",
                        userAgent: userAgent);
                }

                MemoryOptimizer.TrimMemory();
                return process.ExitCode == 0;
            }
            finally
            {
                try { if (!string.IsNullOrWhiteSpace(tempCookiePath) && File.Exists(tempCookiePath)) File.Delete(tempCookiePath); } catch { }
            }
        }

        public async Task<bool> ConvertToMp3Async(
            string sourceUrlOrPath,
            string bitrate,
            string outputDirectory,
            IProgress<DownloadProgressReport>? progress = null,
            CancellationToken cancellationToken = default,
            string? destinationFilePath = null,
            string? cookies = null,
            string? userAgent = null)
        {
            return await ConvertAudioAsync(sourceUrlOrPath, "mp3", bitrate, outputDirectory, false, progress, cancellationToken, destinationFilePath, cookies, userAgent);
        }

        public async Task<bool> ConvertAudioAsync(
            string sourceUrlOrPath,
            string targetFormat,
            string qualityOrBitrate,
            string outputDirectory,
            bool trimRingtone = false,
            IProgress<DownloadProgressReport>? progress = null,
            CancellationToken cancellationToken = default,
            string? destinationFilePath = null,
            string? cookies = null,
            string? userAgent = null)
        {
            Directory.CreateDirectory(outputDirectory);
            var normalizedFormat = targetFormat.ToLowerInvariant().TrimStart('.');
            var isLocalFile = File.Exists(sourceUrlOrPath);

            var ffmpegExe = !string.IsNullOrWhiteSpace(FfmpegDirectoryPath) 
                ? Path.Combine(FfmpegDirectoryPath, "ffmpeg.exe") 
                : "ffmpeg.exe";

            // If it's a local file and ffmpeg exists, use direct ffmpeg for instant conversion
            if (isLocalFile && File.Exists(ffmpegExe))
            {
                var localSuccess = await ConvertLocalFileWithFfmpegAsync(
                    ffmpegExe,
                    sourceUrlOrPath,
                    normalizedFormat,
                    qualityOrBitrate,
                    outputDirectory,
                    trimRingtone,
                    progress,
                    cancellationToken);

                if (localSuccess && !string.IsNullOrWhiteSpace(destinationFilePath) && !File.Exists(destinationFilePath))
                {
                    try
                    {
                        var baseName = Path.GetFileNameWithoutExtension(destinationFilePath);
                        var dir = Path.GetDirectoryName(destinationFilePath) ?? outputDirectory;
                        if (Directory.Exists(dir))
                        {
                            var match = Directory.GetFiles(dir, $"{baseName}.*")
                                .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                                            !f.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) &&
                                            !f.EndsWith(".aria2", StringComparison.OrdinalIgnoreCase) &&
                                            !f.EndsWith(".temp", StringComparison.OrdinalIgnoreCase) &&
                                            !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                                .FirstOrDefault();
                            if (match != null && !string.Equals(match, destinationFilePath, StringComparison.OrdinalIgnoreCase))
                            {
                                File.Move(match, destinationFilePath, true);
                            }
                        }
                    }
                    catch { }
                }

                MemoryOptimizer.TrimMemory();
                return localSuccess;
            }

            // Otherwise, use yt-dlp to extract online stream or convert
            var outputExt = normalizedFormat == "m4r" ? "m4a" : normalizedFormat;
            var outputTemplate = !string.IsNullOrWhiteSpace(destinationFilePath)
                ? Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(destinationFilePath) + ".%(ext)s")
                : Path.Combine(outputDirectory, "%(title)s.%(ext)s");

            var startInfo = new ProcessStartInfo
            {
                FileName = EngineExecutablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-x");
            startInfo.ArgumentList.Add("--audio-format");
            startInfo.ArgumentList.Add(outputExt);

            if (!string.IsNullOrWhiteSpace(qualityOrBitrate) && qualityOrBitrate.EndsWith("k", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.ArgumentList.Add("--audio-quality");
                startInfo.ArgumentList.Add(qualityOrBitrate);
            }

            startInfo.ArgumentList.Add("--embed-metadata");
            startInfo.ArgumentList.Add("--newline");

            AttachCommonArguments(startInfo);

            string? tempCookiePath = null;
            AttachCookiesAndUserAgent(startInfo, cookies, userAgent, out tempCookiePath, isLocalFile ? null : sourceUrlOrPath);

            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(outputTemplate);

            if (isLocalFile)
            {
                startInfo.ArgumentList.Add(sourceUrlOrPath);
            }
            else
            {
                if (!SecurityGuard.ValidateUrl(sourceUrlOrPath, out var safeUrl, out var error))
                {
                    progress?.Report(new DownloadProgressReport { StatusMessage = error, IsError = true });
                    return false;
                }
                startInfo.ArgumentList.Add(safeUrl);
            }

            _lastNonZeroSpeed = "Calculating...";
            LastErrorMessage = null;

            try
            {
                var streamState = new StreamProgressState();
                using var process = new Process { StartInfo = startInfo };
                process.OutputDataReceived += (_, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;
                    ParseAndReportProgress(e.Data, progress, streamState);
                };

                process.ErrorDataReceived += (_, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;
                    LastErrorMessage = e.Data.Trim();
                    if (e.Data.Contains("ERROR:", StringComparison.OrdinalIgnoreCase) ||
                        e.Data.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase))
                    {
                        var msg = e.Data.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase)
                            ? "YouTube Authentication: Please ensure you are logged into YouTube in Chrome or load cookies.txt."
                            : e.Data.Trim();
                        progress?.Report(new DownloadProgressReport { StatusMessage = msg, IsError = true });
                    }
                    else
                    {
                        ParseAndReportProgress(e.Data, progress, streamState);
                    }
                };

                process.Start();
                try { process.StandardInput.Close(); } catch { }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch
                {
                    try { if (!process.HasExited) process.Kill(true); } catch { }
                    throw;
                }

                if (process.ExitCode == 0)
                {
                    progress?.Report(new DownloadProgressReport
                    {
                        Percentage = 100.0,
                        Speed = "0 KB/s",
                        Eta = "00:00",
                        StatusMessage = "Complete - Audio converted successfully"
                    });
                }

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(destinationFilePath))
                {
                    try
                    {
                        var expectedTarget = Path.ChangeExtension(destinationFilePath, normalizedFormat);
                        if (!File.Exists(expectedTarget) && !File.Exists(destinationFilePath))
                        {
                            var baseName = Path.GetFileNameWithoutExtension(destinationFilePath);
                            var dir = Path.GetDirectoryName(destinationFilePath) ?? outputDirectory;
                            if (Directory.Exists(dir))
                            {
                                var match = Directory.GetFiles(dir, $"{baseName}.*")
                                    .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                                                !f.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) &&
                                                !f.EndsWith(".aria2", StringComparison.OrdinalIgnoreCase) &&
                                                !f.EndsWith(".temp", StringComparison.OrdinalIgnoreCase) &&
                                                !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                                    .FirstOrDefault();
                                if (match != null)
                                {
                                    var targetToUse = string.Equals(Path.GetExtension(match), "." + normalizedFormat, StringComparison.OrdinalIgnoreCase)
                                        ? expectedTarget
                                        : destinationFilePath;
                                    if (!string.Equals(match, targetToUse, StringComparison.OrdinalIgnoreCase))
                                    {
                                        File.Move(match, targetToUse, true);
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                // If user requested m4r (iPhone Ringtone), rename/remux the downloaded m4a to m4r
                if (process.ExitCode == 0 && normalizedFormat == "m4r")
                {
                    try
                    {
                        var m4aFiles = Directory.GetFiles(outputDirectory, "*.m4a")
                            .Select(f => new FileInfo(f))
                            .OrderByDescending(f => f.LastWriteTimeUtc)
                            .ToList();

                        if (m4aFiles.Count > 0)
                        {
                            var latestM4a = m4aFiles[0].FullName;
                            var m4rPath = Path.ChangeExtension(latestM4a, ".m4r");
                            if (File.Exists(m4rPath)) File.Delete(m4rPath);
                            File.Move(latestM4a, m4rPath);
                        }
                    }
                    catch
                    {
                        // Fall through
                    }
                }

                if (process.ExitCode != 0 && !cancellationToken.IsCancellationRequested && !isLocalFile && (!string.IsNullOrWhiteSpace(cookies) || IsCookiesConfigured))
                {
                    try
                    {
                        var cached = GetBestAvailableCookiesFile();
                        if (!string.IsNullOrWhiteSpace(cached) && File.Exists(cached))
                        {
                            File.Delete(cached);
                        }
                    }
                    catch { }

                    progress?.Report(new DownloadProgressReport
                    {
                        Percentage = 5.0,
                        StatusMessage = "Retrying audio extraction with multi-client bypass (no cookies)..."
                    });

                    return await ConvertAudioAsync(
                        sourceUrlOrPath,
                        targetFormat,
                        qualityOrBitrate,
                        outputDirectory,
                        trimRingtone,
                        progress,
                        cancellationToken,
                        destinationFilePath,
                        cookies: "NONE",
                        userAgent: userAgent);
                }

                MemoryOptimizer.TrimMemory();
                return process.ExitCode == 0;
            }
            finally
            {
                try { if (!string.IsNullOrWhiteSpace(tempCookiePath) && File.Exists(tempCookiePath)) File.Delete(tempCookiePath); } catch { }
            }
        }

        private async Task<bool> ConvertLocalFileWithFfmpegAsync(
            string ffmpegPath,
            string localFilePath,
            string targetFormat,
            string qualityOrBitrate,
            string outputDirectory,
            bool trimRingtone,
            IProgress<DownloadProgressReport>? progress,
            CancellationToken cancellationToken)
        {
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(localFilePath);
            var destFile = Path.Combine(outputDirectory, $"{fileNameWithoutExt}.{targetFormat}");

            // Ensure unique output filename if file already exists
            int counter = 1;
            while (File.Exists(destFile))
            {
                destFile = Path.Combine(outputDirectory, $"{fileNameWithoutExt}_{counter}.{targetFormat}");
                counter++;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-y"); // Overwrite if needed

            // Optional 30-second cut for iPhone Ringtone
            if (trimRingtone && targetFormat == "m4r")
            {
                startInfo.ArgumentList.Add("-t");
                startInfo.ArgumentList.Add("30");
            }

            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(localFilePath);
            startInfo.ArgumentList.Add("-vn"); // Strip video streams

            // Format-specific codec and parameters
            switch (targetFormat)
            {
                case "mp3":
                    startInfo.ArgumentList.Add("-c:a");
                    startInfo.ArgumentList.Add("libmp3lame");
                    startInfo.ArgumentList.Add("-b:a");
                    startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(qualityOrBitrate) ? "320k" : qualityOrBitrate);
                    break;

                case "wav":
                    startInfo.ArgumentList.Add("-c:a");
                    startInfo.ArgumentList.Add("pcm_s16le");
                    break;

                case "m4r":
                    startInfo.ArgumentList.Add("-c:a");
                    startInfo.ArgumentList.Add("aac");
                    startInfo.ArgumentList.Add("-b:a");
                    startInfo.ArgumentList.Add("256k");
                    startInfo.ArgumentList.Add("-f");
                    startInfo.ArgumentList.Add("ipod");
                    break;

                case "m4a":
                case "aac":
                    startInfo.ArgumentList.Add("-c:a");
                    startInfo.ArgumentList.Add("aac");
                    startInfo.ArgumentList.Add("-b:a");
                    startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(qualityOrBitrate) ? "256k" : qualityOrBitrate);
                    break;

                case "flac":
                    startInfo.ArgumentList.Add("-c:a");
                    startInfo.ArgumentList.Add("flac");
                    break;

                case "ogg":
                    startInfo.ArgumentList.Add("-c:a");
                    startInfo.ArgumentList.Add("libvorbis");
                    startInfo.ArgumentList.Add("-q:a");
                    startInfo.ArgumentList.Add("7");
                    break;

                case "mp2":
                    startInfo.ArgumentList.Add("-c:a");
                    startInfo.ArgumentList.Add("mp2");
                    startInfo.ArgumentList.Add("-b:a");
                    startInfo.ArgumentList.Add("192k");
                    break;

                case "amr":
                    startInfo.ArgumentList.Add("-ar");
                    startInfo.ArgumentList.Add("8000");
                    startInfo.ArgumentList.Add("-ac");
                    startInfo.ArgumentList.Add("1");
                    startInfo.ArgumentList.Add("-c:a");
                    startInfo.ArgumentList.Add("amr_nb");
                    break;

                case "opus":
                    startInfo.ArgumentList.Add("-c:a");
                    startInfo.ArgumentList.Add("libopus");
                    startInfo.ArgumentList.Add("-b:a");
                    startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(qualityOrBitrate) ? "192k" : qualityOrBitrate);
                    break;

                default:
                    startInfo.ArgumentList.Add("-c:a");
                    startInfo.ArgumentList.Add("copy");
                    break;
            }

            startInfo.ArgumentList.Add(destFile);

            progress?.Report(new DownloadProgressReport
            {
                Percentage = 50.0,
                Speed = "Turbo Encoder",
                StatusMessage = $"Encoding high-performance {targetFormat.ToUpperInvariant()} audio..."
            });

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                throw;
            }

            if (process.ExitCode == 0)
            {
                progress?.Report(new DownloadProgressReport
                {
                    Percentage = 100.0,
                    StatusMessage = $"Saved: {Path.GetFileName(destFile)}"
                });
                return true;
            }

            return false;
        }

        public void ParseAndReportProgress(string rawData, IProgress<DownloadProgressReport>? progress, StreamProgressState? state = null)
        {
            if (progress == null || string.IsNullOrWhiteSpace(rawData)) return;

            var cleanData = AnsiRegex.Replace(rawData, "");
            var lines = cleanData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                // Destination line: gives early signal that download has commenced
                var destMatch = YtdlDestRegex.Match(trimmed);
                if (destMatch.Success)
                {
                    var destName = Path.GetFileName(destMatch.Groups[1].Value.Trim());
                    if (state != null)
                    {
                        state.PassCount++;
                        double destPct = state.PassCount >= 2 ? 85.0 : 5.0;
                        state.HighestMappedPercent = Math.Max(state.HighestMappedPercent, destPct);
                    }
                    var pct = state?.HighestMappedPercent ?? 5.0;
                    progress.Report(new DownloadProgressReport
                    {
                        Percentage = pct,
                        Speed = "Connecting...",
                        Eta = "--:--",
                        StatusMessage = state?.PassCount >= 2 ? $"Streaming audio stream: {destName}" : $"Streaming video target: {destName}"
                    });
                    continue;
                }

                // 1. Check Standard YTDL Progress
                var pctMatch = YtdlPercentRegex.Match(trimmed);
                if (pctMatch.Success && double.TryParse(pctMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var percent))
                {
                    string totalSize = "";
                    var sizeMatch = YtdlSizeRegex.Match(trimmed);
                    if (sizeMatch.Success)
                    {
                        totalSize = sizeMatch.Groups[1].Value.Trim();
                    }

                    string speed = _lastNonZeroSpeed;
                    var speedMatch = YtdlSpeedRegex.Match(trimmed);
                    if (speedMatch.Success)
                    {
                        var rawSpeed = speedMatch.Groups[1].Value.Trim();
                        if (!rawSpeed.StartsWith("0", StringComparison.OrdinalIgnoreCase) && 
                            !rawSpeed.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
                        {
                            _lastNonZeroSpeed = rawSpeed;
                            speed = rawSpeed;
                        }
                    }

                    string eta = "--:--";
                    var etaMatch = YtdlEtaRegex.Match(trimmed);
                    if (etaMatch.Success)
                    {
                        var rawEta = etaMatch.Groups[1].Value.Trim();
                        if (!string.Equals(rawEta, "Unknown", StringComparison.OrdinalIgnoreCase))
                        {
                            eta = rawEta;
                        }
                    }
                    else
                    {
                        var inMatch = YtdlInTimeRegex.Match(trimmed);
                        if (inMatch.Success)
                        {
                            eta = inMatch.Groups[1].Value.Trim();
                        }
                    }

                    double effectivePercent = percent;
                    if (state != null)
                    {
                        if (percent < state.LastRawPercent - 15.0 && state.LastRawPercent > 70.0)
                        {
                            state.PassCount = Math.Max(2, state.PassCount + 1);
                        }
                        state.LastRawPercent = percent;

                        if (state.PassCount >= 2)
                        {
                            // Audio pass: 0%..100% -> 85%..98%
                            effectivePercent = 85.0 + (percent / 100.0 * 13.0);
                        }
                        else
                        {
                            // Video pass: 0%..100% -> 5%..85%
                            effectivePercent = 5.0 + (percent / 100.0 * 80.0);
                        }

                        state.HighestMappedPercent = Math.Max(state.HighestMappedPercent, effectivePercent);
                        effectivePercent = state.HighestMappedPercent;
                    }

                    string statusMsg;
                    var fragMatch = YtdlFragRegex.Match(trimmed);
                    if (fragMatch.Success)
                    {
                        statusMsg = $"Downloading fragment {fragMatch.Groups[1].Value}/{fragMatch.Groups[2].Value} ({effectivePercent:F1}% @ {speed})";
                    }
                    else
                    {
                        statusMsg = $"Downloading: {effectivePercent:F1}% @ {speed}";
                    }

                    progress.Report(new DownloadProgressReport
                    {
                        Percentage = effectivePercent,
                        TotalSize = totalSize,
                        Speed = speed,
                        Eta = eta,
                        StatusMessage = statusMsg
                    });
                    continue;
                }

                // 2. Check Aria2c Progress
                var ariaMatch = Aria2ProgressRegex.Match(trimmed);
                if (ariaMatch.Success && double.TryParse(ariaMatch.Groups[3].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var ariaPercent))
                {
                    var speed = ariaMatch.Groups[4].Value;
                    if (!string.IsNullOrWhiteSpace(speed)) _lastNonZeroSpeed = speed;

                    progress.Report(new DownloadProgressReport
                    {
                        Percentage = ariaPercent,
                        TotalSize = ariaMatch.Groups[2].Value,
                        Speed = speed,
                        Eta = ariaMatch.Groups[5].Success ? ariaMatch.Groups[5].Value : "--:--",
                        StatusMessage = $"Turbo Segmenting: {ariaPercent:F1}% @ {speed}"
                    });
                    continue;
                }

                // 3. Status handling during multiplexing / format merging
                if (trimmed.Contains("[Merger]", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("Merging formats", StringComparison.OrdinalIgnoreCase))
                {
                    if (state != null) state.HighestMappedPercent = Math.Max(state.HighestMappedPercent, 98.0);
                    progress.Report(new DownloadProgressReport
                    {
                        Percentage = state?.HighestMappedPercent ?? 98.0,
                        Speed = _lastNonZeroSpeed,
                        Eta = "00:01",
                        StatusMessage = "Turbo Multiplexing video and audio streams with FFmpeg (Finalizing)..."
                    });
                    continue;
                }
                else if (trimmed.Contains("[ExtractAudio]", StringComparison.OrdinalIgnoreCase))
                {
                    if (state != null) state.HighestMappedPercent = Math.Max(state.HighestMappedPercent, 96.0);
                    progress.Report(new DownloadProgressReport
                    {
                        Percentage = state?.HighestMappedPercent ?? 96.0,
                        Speed = _lastNonZeroSpeed,
                        Eta = "00:01",
                        StatusMessage = "Transcoding pristine high-fidelity audio stream..."
                    });
                    continue;
                }
                else if (trimmed.Contains("[Fixup", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("Fixing", StringComparison.OrdinalIgnoreCase))
                {
                    if (state != null) state.HighestMappedPercent = Math.Max(state.HighestMappedPercent, 97.0);
                    progress.Report(new DownloadProgressReport
                    {
                        Percentage = state?.HighestMappedPercent ?? 97.0,
                        Speed = _lastNonZeroSpeed,
                        Eta = "00:01",
                        StatusMessage = "Fixing media container and packet timestamps..."
                    });
                    continue;
                }
                else if (trimmed.Contains("[Metadata]", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("Adding metadata", StringComparison.OrdinalIgnoreCase))
                {
                    if (state != null) state.HighestMappedPercent = Math.Max(state.HighestMappedPercent, 98.0);
                    progress.Report(new DownloadProgressReport
                    {
                        Percentage = state?.HighestMappedPercent ?? 98.0,
                        Speed = _lastNonZeroSpeed,
                        Eta = "00:01",
                        StatusMessage = "Embedding pristine audio metadata & tags..."
                    });
                    continue;
                }
                else if (trimmed.Contains("Deleting original file", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("[DeleteFile]", StringComparison.OrdinalIgnoreCase))
                {
                    if (state != null) state.HighestMappedPercent = Math.Max(state.HighestMappedPercent, 99.0);
                    progress.Report(new DownloadProgressReport
                    {
                        Percentage = state?.HighestMappedPercent ?? 99.0,
                        Speed = _lastNonZeroSpeed,
                        Eta = "00:01",
                        StatusMessage = "Finalizing pristine audio file..."
                    });
                    continue;
                }

                // 4. Initial handshake & format resolution signals (immediate feedback <100ms)
                if (trimmed.StartsWith("[youtube]", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("[info]", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("[hlsnative]", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("[generic]", StringComparison.OrdinalIgnoreCase))
                {
                    if (state != null && state.HighestMappedPercent < 5.0)
                    {
                        state.HighestMappedPercent = 3.0;
                    }
                    var pct = state?.HighestMappedPercent ?? 3.0;
                    var cleanMsg = trimmed.Length > 75 ? trimmed.Substring(0, 72) + "..." : trimmed;
                    progress.Report(new DownloadProgressReport
                    {
                        Percentage = pct,
                        Speed = "Connecting...",
                        Eta = "--:--",
                        StatusMessage = cleanMsg
                    });
                }
            }
        }

        public async Task<(bool Success, string Message)> UpdateEngineAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(EngineExecutablePath))
            {
                return (false, "Media extraction engine binary (yt-dlp) not found.");
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = EngineExecutablePath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                startInfo.ArgumentList.Add("--update");

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                var outTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errTask = process.StandardError.ReadToEndAsync(cancellationToken);

                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch
                {
                    try { if (!process.HasExited) process.Kill(true); } catch { }
                    throw;
                }

                var output = (await outTask).Trim();
                var error = (await errTask).Trim();

                if (!string.IsNullOrWhiteSpace(output))
                {
                    return (process.ExitCode == 0, output);
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    return (false, error);
                }

                return (process.ExitCode == 0, "Extraction engine checked and up-to-date.");
            }
            catch (Exception ex)
            {
                return (false, $"Engine update failed: {ex.Message}");
            }
        }
    }
}
