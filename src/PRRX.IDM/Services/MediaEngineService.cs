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

    public interface IMediaEngineService
    {
        string EngineExecutablePath { get; }
        string? FfmpegDirectoryPath { get; }
        string? Aria2cExecutablePath { get; }
        string? ActiveCookiesPath { get; }
        bool IsCookiesConfigured { get; }
        bool IsEngineAvailable { get; }
        Task<(MediaProbeResult? Result, string? ErrorMessage)> ProbeMediaAsync(string url, CancellationToken cancellationToken = default);
        Task<bool> DownloadVideoAsync(
            string url,
            string formatId,
            string outputDirectory,
            IProgress<DownloadProgressReport>? progress = null,
            CancellationToken cancellationToken = default,
            string? targetContainer = null);
        Task<bool> ConvertToMp3Async(
            string sourceUrlOrPath,
            string bitrate,
            string outputDirectory,
            IProgress<DownloadProgressReport>? progress = null,
            CancellationToken cancellationToken = default);
        Task<bool> ConvertAudioAsync(
            string sourceUrlOrPath,
            string targetFormat,
            string qualityOrBitrate,
            string outputDirectory,
            bool trimRingtone = false,
            IProgress<DownloadProgressReport>? progress = null,
            CancellationToken cancellationToken = default);
        Task<(bool Success, string Message)> UpdateEngineAsync(CancellationToken cancellationToken = default);
    }

    public class MediaEngineService : IMediaEngineService
    {
        private static readonly Regex YtdlProgressRegex = new(
            @"\[download\]\s+(\d+(?:\.\d+)?)%\s+of\s+(?:~\s*)?(\S+)\s+at\s+(\S+)\s+ETA\s+(\S+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex Aria2ProgressRegex = new(
            @"\[#[a-f0-9]+\s+(\S+)\/(\S+)\((\d+)%\).*?DL:(\S+)(?:.*?ETA:(\S+))?\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IConfigurationService? _configService;
        private readonly ITelegramLinkResolver _telegramResolver = new TelegramLinkResolver();
        private string _lastNonZeroSpeed = "Calculating...";

        public string EngineExecutablePath { get; private set; }
        public string? FfmpegDirectoryPath { get; private set; }
        public string? Aria2cExecutablePath { get; private set; }

        public string? ActiveCookiesPath => null;
        public bool IsCookiesConfigured => false;
        public bool IsEngineAvailable => File.Exists(EngineExecutablePath);

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

        private void AttachCommonArguments(ProcessStartInfo startInfo)
        {
            if (!string.IsNullOrWhiteSpace(FfmpegDirectoryPath))
            {
                startInfo.ArgumentList.Add("--ffmpeg-location");
                startInfo.ArgumentList.Add(FfmpegDirectoryPath);
            }

            // Modern tokenless / client emulation extractor arguments to bypass YouTube bot detection without requiring cookies
            startInfo.ArgumentList.Add("--extractor-args");
            startInfo.ArgumentList.Add("youtube:player_client=android,ios,web_creator");
            startInfo.ArgumentList.Add("--extractor-args");
            startInfo.ArgumentList.Add("youtubetab:approximate_date");

            // Multi-Connection Turbo Speed Acceleration
            var connections = Math.Clamp(_configService?.CurrentConfig.TurboConnectionCount ?? 32, 8, 64);
            startInfo.ArgumentList.Add("--concurrent-fragments");
            startInfo.ArgumentList.Add(connections.ToString());

            // Anti-freeze & Anti-stall streaming optimizations
            startInfo.ArgumentList.Add("--throttled-rate");
            startInfo.ArgumentList.Add("100K"); // Auto-drops any CDN connection that stalls below 100 KB/s and restarts it instantly

            startInfo.ArgumentList.Add("--hls-use-mpegts"); // Seamless fragment demuxing without MP4 container lock delays
            startInfo.ArgumentList.Add("--no-part"); // Direct streaming into destination without locking .part files
            startInfo.ArgumentList.Add("--file-allocation");
            startInfo.ArgumentList.Add("none"); // Eliminates Windows file-allocation pauses

            startInfo.ArgumentList.Add("--buffer-size");
            startInfo.ArgumentList.Add("8M"); // 8MB high-throughput buffer
            startInfo.ArgumentList.Add("--http-chunk-size");
            startInfo.ArgumentList.Add("10M");

            startInfo.ArgumentList.Add("--socket-timeout");
            startInfo.ArgumentList.Add("5"); // Fast 5s timeout fails hung sockets immediately and recovers
            startInfo.ArgumentList.Add("--retries");
            startInfo.ArgumentList.Add("10");
            startInfo.ArgumentList.Add("--fragment-retries");
            startInfo.ArgumentList.Add("10");
        }

        public async Task<(MediaProbeResult? Result, string? ErrorMessage)> ProbeMediaAsync(string url, CancellationToken cancellationToken = default)
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

            var startInfo = new ProcessStartInfo
            {
                FileName = EngineExecutablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("--dump-single-json");
            startInfo.ArgumentList.Add("--no-warnings");
            startInfo.ArgumentList.Add("--no-playlist");
            
            AttachCommonArguments(startInfo);
            startInfo.ArgumentList.Add(safeUrl);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            var json = await outputTask;
            var err = await errorTask;

            if (string.IsNullOrWhiteSpace(json) || process.ExitCode != 0)
            {
                var cleanErr = !string.IsNullOrWhiteSpace(err) ? err.Trim() : "Could not retrieve media details.";
                if (cleanErr.Contains("Sign in to confirm you're not a bot", StringComparison.OrdinalIgnoreCase) || 
                    cleanErr.Contains("LOGIN_REQUIRED", StringComparison.OrdinalIgnoreCase))
                {
                    cleanErr = "YouTube Bot Protection: Click 'Export All Cookies' in extension and load in Settings.";
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

                // Standard available resolutions with estimated file sizes
                var baseDur = durSeconds > 0 ? durSeconds : 200.0;
                result.Formats.Add(new MediaFormat { FormatId = "bestvideo[height<=1080]+bestaudio/best[height<=1080]/best", Resolution = "1080p Full HD", Extension = "mp4", EstimatedSizeFormatted = $"~ {(baseDur * 2.8 / 8.0):F1} MB", HasVideo = true, HasAudio = true });
                result.Formats.Add(new MediaFormat { FormatId = "bestvideo[height<=720]+bestaudio/best[height<=720]/best", Resolution = "720p HD", Extension = "mp4", EstimatedSizeFormatted = $"~ {(baseDur * 1.5 / 8.0):F1} MB", HasVideo = true, HasAudio = true });
                result.Formats.Add(new MediaFormat { FormatId = "bestvideo[height<=480]+bestaudio/best[height<=480]/best", Resolution = "480p", Extension = "mp4", EstimatedSizeFormatted = $"~ {(baseDur * 0.8 / 8.0):F1} MB", HasVideo = true, HasAudio = true });
                result.Formats.Add(new MediaFormat { FormatId = "bestvideo[height<=360]+bestaudio/best[height<=360]/best", Resolution = "360p", Extension = "mp4", EstimatedSizeFormatted = $"~ {(baseDur * 0.45 / 8.0):F1} MB", HasVideo = true, HasAudio = true });
                result.Formats.Add(new MediaFormat { FormatId = "bestvideo[height<=240]+bestaudio/best[height<=240]/best", Resolution = "240p", Extension = "mp4", EstimatedSizeFormatted = $"~ {(baseDur * 0.25 / 8.0):F1} MB", HasVideo = true, HasAudio = true });
                result.Formats.Add(new MediaFormat { FormatId = "bestvideo[height<=144]+bestaudio/best[height<=144]/best", Resolution = "144p", Extension = "mp4", EstimatedSizeFormatted = $"~ {(baseDur * 0.12 / 8.0):F1} MB", HasVideo = true, HasAudio = true });

                MemoryOptimizer.TrimMemory();
                return (result, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
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
            string? targetContainer = null)
        {
            if (!SecurityGuard.ValidateUrl(url, out var safeUrl, out var error))
            {
                progress?.Report(new DownloadProgressReport { StatusMessage = error, IsError = true });
                return false;
            }

            Directory.CreateDirectory(outputDirectory);
            var outputTemplate = Path.Combine(outputDirectory, "%(title)s.%(ext)s");

            var startInfo = new ProcessStartInfo
            {
                FileName = EngineExecutablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
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

            // Attach anti-stall parameters
            AttachCommonArguments(startInfo);

            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(outputTemplate);
            startInfo.ArgumentList.Add(safeUrl);

            _lastNonZeroSpeed = "Calculating...";

            using var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                ParseAndReportProgress(e.Data, progress);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                if (e.Data.Contains("ERROR:", StringComparison.OrdinalIgnoreCase) ||
                    e.Data.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase))
                {
                    var msg = e.Data.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase)
                        ? "YouTube Authentication: Please load cookies.txt in Settings & Updates."
                        : e.Data.Trim();
                    progress?.Report(new DownloadProgressReport { StatusMessage = msg, IsError = true });
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);
            MemoryOptimizer.TrimMemory();
            return process.ExitCode == 0;
        }

        public async Task<bool> ConvertToMp3Async(
            string sourceUrlOrPath,
            string bitrate,
            string outputDirectory,
            IProgress<DownloadProgressReport>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return await ConvertAudioAsync(sourceUrlOrPath, "mp3", bitrate, outputDirectory, false, progress, cancellationToken);
        }

        public async Task<bool> ConvertAudioAsync(
            string sourceUrlOrPath,
            string targetFormat,
            string qualityOrBitrate,
            string outputDirectory,
            bool trimRingtone = false,
            IProgress<DownloadProgressReport>? progress = null,
            CancellationToken cancellationToken = default)
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

                MemoryOptimizer.TrimMemory();
                return localSuccess;
            }

            // Otherwise, use yt-dlp to extract online stream or convert
            var outputExt = normalizedFormat == "m4r" ? "m4a" : normalizedFormat;
            var outputTemplate = Path.Combine(outputDirectory, "%(title)s.%(ext)s");

            var startInfo = new ProcessStartInfo
            {
                FileName = EngineExecutablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
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

            using var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                ParseAndReportProgress(e.Data, progress);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                if (e.Data.Contains("ERROR:", StringComparison.OrdinalIgnoreCase) ||
                    e.Data.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase))
                {
                    var msg = e.Data.Contains("Sign in to confirm", StringComparison.OrdinalIgnoreCase)
                        ? "YouTube Authentication: Please load cookies.txt in Settings & Updates."
                        : e.Data.Trim();
                    progress?.Report(new DownloadProgressReport { StatusMessage = msg, IsError = true });
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

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

            MemoryOptimizer.TrimMemory();
            return process.ExitCode == 0;
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

            await process.WaitForExitAsync(cancellationToken);

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

        private void ParseAndReportProgress(string line, IProgress<DownloadProgressReport>? progress)
        {
            if (progress == null) return;

            // 1. Check Standard YTDL Progress
            var match = YtdlProgressRegex.Match(line);
            if (match.Success)
            {
                if (double.TryParse(match.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var percent))
                {
                    var rawSpeed = match.Groups[3].Value;
                    if (!rawSpeed.StartsWith("0", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(rawSpeed))
                    {
                        _lastNonZeroSpeed = rawSpeed;
                    }

                    var speedToDisplay = rawSpeed.StartsWith("0", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_lastNonZeroSpeed)
                        ? _lastNonZeroSpeed
                        : rawSpeed;

                    progress.Report(new DownloadProgressReport
                    {
                        Percentage = percent,
                        TotalSize = match.Groups[2].Value,
                        Speed = speedToDisplay,
                        Eta = match.Groups[4].Value,
                        StatusMessage = $"Downloading: {percent:F1}% @ {speedToDisplay}"
                    });
                    return;
                }
            }

            // 2. Check Aria2c Progress
            var ariaMatch = Aria2ProgressRegex.Match(line);
            if (ariaMatch.Success)
            {
                if (double.TryParse(ariaMatch.Groups[3].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var percent))
                {
                    var speed = ariaMatch.Groups[4].Value;
                    if (!string.IsNullOrWhiteSpace(speed)) _lastNonZeroSpeed = speed;

                    progress.Report(new DownloadProgressReport
                    {
                        Percentage = percent,
                        TotalSize = ariaMatch.Groups[2].Value,
                        Speed = speed,
                        Eta = ariaMatch.Groups[5].Success ? ariaMatch.Groups[5].Value : "--:--",
                        StatusMessage = $"Turbo Segmenting: {percent:F1}% @ {speed}"
                    });
                    return;
                }
            }

            // 3. Status handling during multiplexing / format merging
            if (line.Contains("[Merger]", StringComparison.OrdinalIgnoreCase) || line.Contains("Merging formats", StringComparison.OrdinalIgnoreCase))
            {
                progress.Report(new DownloadProgressReport
                {
                    Percentage = 98.0,
                    Speed = _lastNonZeroSpeed,
                    Eta = "00:01",
                    StatusMessage = "Turbo Multiplexing video and audio streams with FFmpeg (Finalizing)..."
                });
            }
            else if (line.Contains("[ExtractAudio]", StringComparison.OrdinalIgnoreCase) || line.Contains("[FixupM3u8]", StringComparison.OrdinalIgnoreCase))
            {
                progress.Report(new DownloadProgressReport
                {
                    Percentage = 96.0,
                    Speed = _lastNonZeroSpeed,
                    Eta = "00:01",
                    StatusMessage = "Transcoding pristine high-fidelity audio stream..."
                });
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

                await process.WaitForExitAsync(cancellationToken);

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
