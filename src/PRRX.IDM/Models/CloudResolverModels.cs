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
    public class ApkItem
    {
        public string Title { get; set; } = string.Empty;
        public string? PackageName { get; set; }
        public string? Version { get; set; } = "Latest";
        public string? Size { get; set; } = "Unknown";
        public string? IconUrl { get; set; }
        public string? DownloadUrl { get; set; }
        public string? DetailUrl { get; set; }
        public string Provider { get; set; } = "Ultra Store";
        public string? Description { get; set; }
        public string? Rating { get; set; }

        public string FormattedBadge => $"{Provider} • {Version} • {Size}";
    }

    public class ApkSearchResult
    {
        public bool Success { get; set; }
        public string? Query { get; set; }
        public List<ApkItem> Items { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public bool FallbackRequired { get; set; }
    }

    public class CloudResolvedMedia
    {
        public bool Success { get; set; }
        public string? Url { get; set; }
        public string? Title { get; set; }
        public string? DirectStreamUrl { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? Format { get; set; }
        public string? Extension { get; set; } = "mp4";
        public long FileSizeBytes { get; set; }
        public string? FormattedSize { get; set; }
        public string? Provider { get; set; }
        public bool IsAudioOnly { get; set; }
        public bool FallbackRequired { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, string> AdditionalMetadata { get; set; } = new();
    }
}
