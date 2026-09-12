// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
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

    public enum FileCategory
    {
        General,
        Programs,
        Compressed,
        Video,
        Music,
        Documents
    }

    public static class FileCategoryHelper
    {
        public static FileCategory DetectCategory(string fileNameOrUrl)
        {
            if (string.IsNullOrWhiteSpace(fileNameOrUrl)) return FileCategory.General;

            var clean = fileNameOrUrl.Split('?')[0].Split('#')[0];
            var ext = System.IO.Path.GetExtension(clean).ToLowerInvariant();

            return ext switch
            {
                ".exe" or ".msi" or ".bat" or ".cmd" or ".apk" or ".appx" => FileCategory.Programs,
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".iso" or ".bz2" or ".xz" => FileCategory.Compressed,
                ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".flv" or ".wmv" or ".m4v" => FileCategory.Video,
                ".mp3" or ".wav" or ".flac" or ".m4a" or ".m4r" or ".ogg" or ".aac" or ".opus" or ".wma" or ".amr" => FileCategory.Music,
                ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".epub" => FileCategory.Documents,
                _ => FileCategory.General
            };
        }
    }

    public enum CompletionAction
    {
        None = 0,
        ShowDialog = 1,
        ExitApplication = 2,
        SleepComputer = 3,
        ShutdownComputer = 4,
        RestartComputer = 5
    }

    public class DownloadConnectionThread : System.ComponentModel.INotifyPropertyChanged
    {
        private int _threadId;
        private long _startByte;
        private long _endByte;
        private long _currentByte;
        private long _downloadedBytes;
        private string _formattedDownloaded = "0 KB";
        private string _statusInfo = "Connecting...";
        private double _progressPercentage = 0.0;
        private bool _isActive = true;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propName = null) =>
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propName));

        public int ThreadId { get => _threadId; set { if (_threadId != value) { _threadId = value; OnPropertyChanged(); } } }
        public long StartByte { get => _startByte; set { if (_startByte != value) { _startByte = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormattedStartPosition)); OnPropertyChanged(nameof(FormattedRange)); } } }
        public long EndByte { get => _endByte; set { if (_endByte != value) { _endByte = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormattedRange)); OnPropertyChanged(nameof(FormattedProgress)); } } }
        public long CurrentByte { get => _currentByte; set { if (_currentByte != value) { _currentByte = value; OnPropertyChanged(); } } }
        public long DownloadedBytes { get => _downloadedBytes; set { if (_downloadedBytes != value) { _downloadedBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormattedProgress)); } } }
        public string FormattedDownloaded { get => _formattedDownloaded; set { if (_formattedDownloaded != value) { _formattedDownloaded = value; OnPropertyChanged(); } } }
        public string StatusInfo { get => _statusInfo; set { if (_statusInfo != value) { _statusInfo = value; OnPropertyChanged(); } } }
        public double ProgressPercentage { get => _progressPercentage; set { if (Math.Abs(_progressPercentage - value) > 0.001) { _progressPercentage = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormattedProgress)); } } }
        public bool IsActive { get => _isActive; set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } } }

        public string FormattedStartPosition => StartByte >= 0 ? FormatBytes(StartByte) : "0 B";
        public string FormattedRange => (StartByte >= 0 && EndByte >= StartByte) ? $"{FormatBytes(StartByte)} - {FormatBytes(EndByte)}" : "--";
        public string FormattedProgress => (EndByte >= StartByte && EndByte > 0) ? $"{ProgressPercentage:F1} %" : FormatBytes(DownloadedBytes);

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            if (bytes >= 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
            if (bytes >= 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
            if (bytes >= 1024) return $"{(bytes / 1024.0):F1} KB";
            return $"{bytes} B";
        }
    }

    public class SpeedLimiterSettings
    {
        public bool IsEnabled { get; set; } = false;
        public int MaxSpeedKbps { get; set; } = 1024;
        public bool RememberSettings { get; set; } = true;
    }

    public class DownloadFileInfoRequest
    {
        public string Url { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public FileCategory Category { get; set; } = FileCategory.General;
        public string SaveDirectory { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long TotalSizeBytes { get; set; }
        public string FormattedSize { get; set; } = "Unknown Size";
        public bool StartImmediately { get; set; } = true;
    }

    public class BatchLinkItem
    {
        public bool IsSelected { get; set; } = true;
        public string Url { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string FileSizeFormatted { get; set; } = "Pending...";
        public string LinkText { get; set; } = string.Empty;
        public FileCategory Category { get; set; } = FileCategory.General;
    }

    public class BatchDownloadRequest
    {
        public string SourcePageUrl { get; set; } = string.Empty;
        public string PageTitle { get; set; } = "Web Page";
        public List<BatchLinkItem> Links { get; set; } = new();
    }
}
