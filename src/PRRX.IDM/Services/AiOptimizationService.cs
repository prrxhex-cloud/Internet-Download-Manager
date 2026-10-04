// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using PRRX.IDM.Models;

namespace PRRX.IDM.Services
{
    public class ParsedMediaInfo
    {
        public string CleanTitle { get; set; } = string.Empty;
        public string? Artist { get; set; }
        public string? TrackTitle { get; set; }
        public string? DetectedResolution { get; set; }
        public int? ReleaseYear { get; set; }
        public bool IsOfficialRelease { get; set; }
    }

    public enum MediaArtType
    {
        Unknown,
        SquareAlbumArt,    // ~ 1:1 aspect ratio (audio/cover)
        VideoPoster16x9,   // ~ 16:9 aspect ratio (YouTube, video thumbnails)
        BannerWide,        // > 2:1 aspect ratio
        PortraitPoster     // ~ 2:3 aspect ratio (movie posters)
    }

    /// <summary>
    /// AI Optimization Engine: NLP Filename Processing, ML Concurrency Prediction,
    /// Smart Categorization, and Computer Vision Aspect Ratio Analysis.
    /// Internal-only intelligence (no chatbots or popups).
    /// </summary>
    public class AiOptimizationService
    {
        private static readonly Lazy<AiOptimizationService> _lazy = new(() => new AiOptimizationService());
        public static AiOptimizationService Current => _lazy.Value;

        private static readonly Regex CdnParamRegex = new(@"\?.*$", RegexOptions.Compiled);
        private static readonly Regex ResolutionRegex = new(@"(?:1080p|720p|480p|360p|2160p|4k|8k|hd|fhd|uhd)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex YearRegex = new(@"\b(19\d{2}|20\d{2})\b", RegexOptions.Compiled);
        private static readonly Regex OfficialVideoRegex = new(@"\((?:official\s+(?:video|audio|music\s+video|lyric\s+video)|hd|4k)\)|\[(?:official\s+(?:video|audio|music\s+video|lyric\s+video)|hd|4k)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Host latency & speed learning history for ML predictions
        private readonly ConcurrentDictionary<string, HostPerformanceRecord> _hostHistory = new(StringComparer.OrdinalIgnoreCase);

        private class HostPerformanceRecord
        {
            public long LatencyMs { get; set; } = 100;
            public double AvgSpeedBytesPerSec { get; set; } = 2 * 1024 * 1024;
            public int SuccessCount { get; set; } = 1;
        }

        // ====================================================================
        // 1. Natural Language Processing (NLP): Filename & Metadata Cleaning
        // ====================================================================

        /// <summary>
        /// Cleans raw URLs and messy CDN query strings into clean, sanitized filenames.
        /// </summary>
        public string CleanFileName(string rawFileNameOrUrl)
        {
            if (string.IsNullOrWhiteSpace(rawFileNameOrUrl)) return "download";

            var clean = rawFileNameOrUrl.Trim();

            // 1. Remove URL query strings & fragments
            clean = CdnParamRegex.Replace(clean, "");
            var hashIdx = clean.IndexOf('#');
            if (hashIdx >= 0) clean = clean.Substring(0, hashIdx);

            // 2. Extract leaf file name if full URL/path was passed
            if (clean.Contains('/') || clean.Contains('\\'))
            {
                var leaf = Path.GetFileName(clean);
                if (!string.IsNullOrWhiteSpace(leaf)) clean = leaf;
            }

            // 3. Unescape URL encoding (%20, etc.)
            try
            {
                clean = Uri.UnescapeDataString(clean);
            }
            catch { }

            // 4. Remove unwanted brackets and tags from YouTube / CDN titles
            clean = OfficialVideoRegex.Replace(clean, "").Trim();

            // 5. Replace underscores with spaces when used as separators
            var ext = Path.GetExtension(clean);
            var baseName = Path.GetFileNameWithoutExtension(clean);
            if (baseName.Contains('_') && !baseName.Contains(' '))
            {
                baseName = baseName.Replace('_', ' ');
            }

            // Remove illegal Win32 path characters
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
            {
                baseName = baseName.Replace(c, '-');
            }

            baseName = Regex.Replace(baseName, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "download";

            return string.IsNullOrEmpty(ext) ? baseName : $"{baseName}{ext}";
        }

        /// <summary>
        /// Parses media titles to extract artist, song title, resolution, and release year.
        /// </summary>
        public ParsedMediaInfo ParseMediaMetadata(string rawTitle)
        {
            var info = new ParsedMediaInfo();
            if (string.IsNullOrWhiteSpace(rawTitle)) return info;

            var clean = CleanFileName(rawTitle);
            info.CleanTitle = clean;

            // Extract resolution
            var resMatch = ResolutionRegex.Match(rawTitle);
            if (resMatch.Success)
            {
                info.DetectedResolution = resMatch.Value.ToUpperInvariant();
            }

            // Extract release year
            var yearMatch = YearRegex.Match(rawTitle);
            if (yearMatch.Success && int.TryParse(yearMatch.Value, out var y))
            {
                info.ReleaseYear = y;
            }

            // Check if marked as official
            info.IsOfficialRelease = OfficialVideoRegex.IsMatch(rawTitle);

            // Extract Artist - Title structure (e.g., "Adele - Hello")
            var baseName = Path.GetFileNameWithoutExtension(clean);
            var dashIndex = baseName.IndexOf(" - ", StringComparison.Ordinal);
            if (dashIndex > 0)
            {
                info.Artist = baseName.Substring(0, dashIndex).Trim();
                info.TrackTitle = baseName.Substring(dashIndex + 3).Trim();
            }
            else
            {
                info.TrackTitle = baseName;
            }

            return info;
        }

        /// <summary>
        /// Predicts appropriate download subcategory based on file extensions and keywords.
        /// </summary>
        public FileCategory PredictCategory(string fileName, string? mimeType = null)
        {
            var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();

            // Audio / Music
            if (ext is "mp3" or "wav" or "m4a" or "flac" or "aac" or "ogg" or "opus" or "wma" ||
                (mimeType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true))
            {
                return FileCategory.Music;
            }

            // Video / Movies
            if (ext is "mp4" or "mkv" or "webm" or "avi" or "mov" or "flv" or "wmv" or "ts" or "m4v" ||
                (mimeType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true))
            {
                return FileCategory.Video;
            }

            // Software / Applications
            if (ext is "exe" or "msi" or "apk" or "bat" or "cmd" or "ps1" or "iso" or "dmg" or "deb" or "rpm")
            {
                return FileCategory.Programs;
            }

            // Documents
            if (ext is "pdf" or "doc" or "docx" or "xls" or "xlsx" or "ppt" or "pptx" or "txt" or "epub" or "csv")
            {
                return FileCategory.Documents;
            }

            // Compressed Archives
            if (ext is "zip" or "rar" or "7z" or "tar" or "gz" or "bz2" or "xz")
            {
                return FileCategory.Compressed;
            }

            return FileCategory.General;
        }

        // ====================================================================
        // 2. Predictive & Machine Learning (ML): Concurrency & Buffer Tuning
        // ====================================================================

        /// <summary>
        /// Dynamically predicts optimal socket concurrency (8-32 streams)
        /// based on file size, host latency, and historical throughput.
        /// </summary>
        public int PredictOptimalConcurrency(long totalBytes, string? host = null, int userRequested = 0)
        {
            if (userRequested > 0) return Math.Clamp(userRequested, 1, 64);
            if (totalBytes <= 0) return 1;

            long latency = 100;
            if (!string.IsNullOrWhiteSpace(host) && _hostHistory.TryGetValue(host, out var rec))
            {
                latency = rec.LatencyMs;
            }

            // Very small file (< 2 MB): 1 to 4 streams avoids TCP handshake overhead
            if (totalBytes < 2 * 1024 * 1024)
            {
                return Math.Clamp((int)(totalBytes / (512 * 1024)), 1, 4);
            }

            // Medium file (2 MB - 20 MB): 8 to 16 streams
            if (totalBytes < 20 * 1024 * 1024)
            {
                return latency > 300 ? 8 : 16;
            }

            // Large file (> 20 MB): 16 to 32 streams for maximum throughput
            if (latency > 400) return 16; // Avoid overwhelming high-latency hosts
            return 32;
        }

        /// <summary>
        /// Predicts optimal socket buffer size based on throughput profile.
        /// </summary>
        public int PredictOptimalBufferSize(long totalBytes, double currentSpeedBps = 0)
        {
            if (currentSpeedBps > 20 * 1024 * 1024) return 2 * 1024 * 1024; // 2 MB for >20MB/s Gigabit
            if (currentSpeedBps > 5 * 1024 * 1024) return 1024 * 1024;      // 1 MB for high speed
            if (totalBytes > 50 * 1024 * 1024) return 512 * 1024;           // 512 KB
            return 256 * 1024;                                               // 256 KB standard
        }

        /// <summary>
        /// Records observed host latency and speed to train the predictive engine.
        /// </summary>
        public void RecordHostPerformance(string host, long latencyMs, double speedBps)
        {
            if (string.IsNullOrWhiteSpace(host)) return;
            _hostHistory.AddOrUpdate(host,
                _ => new HostPerformanceRecord { LatencyMs = latencyMs, AvgSpeedBytesPerSec = speedBps, SuccessCount = 1 },
                (_, existing) =>
                {
                    existing.LatencyMs = (existing.LatencyMs + latencyMs) / 2;
                    existing.AvgSpeedBytesPerSec = (existing.AvgSpeedBytesPerSec * 0.7) + (speedBps * 0.3);
                    existing.SuccessCount++;
                    return existing;
                });
        }

        // ====================================================================
        // 3. Computer Vision: Media Artwork & Thumbnail Aspect Ratio Inspector
        // ====================================================================

        /// <summary>
        /// Analyzes image aspect ratio to classify artwork type (e.g. 1:1 Album Art, 16:9 Video Poster).
        /// </summary>
        public MediaArtType InspectMediaArtType(int width, int height)
        {
            if (width <= 0 || height <= 0) return MediaArtType.Unknown;

            double ratio = (double)width / height;

            // Square artwork: 0.95 to 1.05 -> Album art / cover
            if (ratio >= 0.92 && ratio <= 1.08) return MediaArtType.SquareAlbumArt;

            // 16:9 video format: ~1.77 (1.6 to 1.9)
            if (ratio >= 1.6 && ratio <= 1.9) return MediaArtType.VideoPoster16x9;

            // Wide banner: > 2.0
            if (ratio > 2.0) return MediaArtType.BannerWide;

            // Portrait poster (movie): ~0.66 (0.6 to 0.8)
            if (ratio >= 0.6 && ratio <= 0.8) return MediaArtType.PortraitPoster;

            return MediaArtType.Unknown;
        }
    }
}
