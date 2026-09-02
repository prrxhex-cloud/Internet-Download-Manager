using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PRRX.IDM.Models
{
    public enum AppThemeMode
    {
        System = 0,
        Light = 1,
        Dark = 2
    }

    public class AppConfig
    {
        public bool IsOnboardingCompleted { get; set; } = false;
        public bool HasCompletedQuickTour { get; set; } = false;
        public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;
        
        /// <summary>
        /// Liquid Glass / Acrylic transparency factor for Windows 11 (0.1 to 1.0)
        /// </summary>
        public double TransparencyFactor { get; set; } = 0.85;

        public bool EnableTransparency { get; set; } = true;
        public string DownloadDirectory { get; set; } = string.Empty;
        
        /// <summary>
        /// Explicit cookie authentication toggle
        /// </summary>
        public bool IsCookiesEnabled { get; set; } = true;

        /// <summary>
        /// Path to custom exported cookies.txt file for YouTube authentication
        /// </summary>
        public string CookiesFilePath { get; set; } = string.Empty;

        /// <summary>
        /// Turbo Multi-Connection Chunk Acceleration (8 to 32 simultaneous streams)
        /// </summary>
        public bool EnableTurboAcceleration { get; set; } = true;
        public int TurboConnectionCount { get; set; } = 32;

        public bool EnableClipboardSniffer { get; set; } = true;
        public bool AutoCheckUpdates { get; set; } = true;
        public int MaxConcurrentDownloads { get; set; } = 3;
        public string AudioBitrateDefault { get; set; } = "320k";
    }

    public enum DownloadStatus
    {
        Queued,
        Probing,
        Downloading,
        Converting,
        Completed,
        Failed,
        Paused,
        Cancelled
    }

    public enum MediaType
    {
        Video,
        Audio,
        Thumbnail
    }

    public class DownloadItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = "Initializing...";
        public string PublisherName { get; set; } = "Online Creator";
        public string TargetFilePath { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public MediaType Type { get; set; } = MediaType.Video;
        public string SelectedQuality { get; set; } = "1080p Full HD";
        public string FormatId { get; set; } = string.Empty;
        public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
        
        public double ProgressPercentage { get; set; } = 0.0;
        public string FileSizeFormatted { get; set; } = "Completed";
        public string SpeedFormatted { get; set; } = "0 KB/s";
        public string EtaFormatted { get; set; } = "--:--";
        public string FormattedTime { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }

    public class MediaFormat
    {
        public string FormatId { get; set; } = string.Empty;
        public string Resolution { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public long? FileSizeBytes { get; set; }
        public string EstimatedSizeFormatted { get; set; } = string.Empty;
        public bool HasVideo { get; set; }
        public bool HasAudio { get; set; }
        public int Fps { get; set; }

        public string DisplayLabel
        {
            get
            {
                var label = !string.IsNullOrWhiteSpace(Resolution) ? Resolution : Note;
                if (!string.IsNullOrWhiteSpace(Extension))
                {
                    label += $" ({Extension.ToUpperInvariant()})";
                }
                if (!string.IsNullOrWhiteSpace(EstimatedSizeFormatted))
                {
                    label += $" • {EstimatedSizeFormatted}";
                }
                return label;
            }
        }
    }

    public class MediaProbeResult
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string PublisherName { get; set; } = string.Empty;
        public string PublishedDate { get; set; } = string.Empty;
        public string ViewCountFormatted { get; set; } = string.Empty;
        public string DurationFormatted { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public string ThumbnailUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        
        // Music details
        public bool IsMusic { get; set; } = false;
        public string Musician { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public string Track { get; set; } = string.Empty;

        public List<MediaFormat> Formats { get; set; } = new();
    }

    public class ThumbnailItem
    {
        public string VideoId { get; set; } = string.Empty;
        public string ResolutionTitle { get; set; } = string.Empty;
        public string Dimensions { get; set; } = string.Empty;
        public string EstimatedSizeFormatted { get; set; } = "~ 250 KB";
        public string QualityBadge { get; set; } = "1080p HD";
        public string Url { get; set; } = string.Empty;
        public bool IsAvailable { get; set; } = true;
    }

    public class UpdateManifest
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonPropertyName("releaseDate")]
        public string ReleaseDate { get; set; } = string.Empty;

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("sha256Hash")]
        public string Sha256Hash { get; set; } = string.Empty;

        [JsonPropertyName("releaseNotes")]
        public string ReleaseNotes { get; set; } = string.Empty;

        [JsonPropertyName("isMandatory")]
        public bool IsMandatory { get; set; } = false;
    }
}
