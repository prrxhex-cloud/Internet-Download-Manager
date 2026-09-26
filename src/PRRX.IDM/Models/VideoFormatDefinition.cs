// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace PRRX.IDM.Models
{
    public class VideoFormatDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public string FfmpegFormat { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsRecommended { get; set; }

        public string DisplayLabel => IsRecommended ? $"{Name} (Default)" : Name;

        public static readonly List<VideoFormatDefinition> SupportedFormats = new()
        {
            // Default & Top Modern Formats
            new VideoFormatDefinition
            {
                Id = "mp4",
                Name = "MP4 • MPEG-4 Part 14",
                Extension = ".mp4",
                Category = "Standard",
                FfmpegFormat = "mp4",
                Description = "Universal compatibility across all PCs, smartphones, TVs, and web browsers.",
                IsRecommended = true
            },
            new VideoFormatDefinition
            {
                Id = "mkv",
                Name = "MKV • Matroska Video",
                Extension = ".mkv",
                Category = "Standard",
                FfmpegFormat = "matroska",
                Description = "High quality open container supporting multiple audio tracks, subtitles, and chapter markers."
            },
            new VideoFormatDefinition
            {
                Id = "webm",
                Name = "WebM • Royalty-free HTML5 Video",
                Extension = ".webm",
                Category = "Web",
                FfmpegFormat = "webm",
                Description = "High-efficiency open web video format created for HTML5 (VP8, VP9, AV1)."
            },
            new VideoFormatDefinition
            {
                Id = "mov",
                Name = "MOV • Apple QuickTime",
                Extension = ".mov",
                Category = "Apple",
                FfmpegFormat = "mov",
                Description = "Apple QuickTime container with high bitrate mastering and ProRes support."
            },
            new VideoFormatDefinition
            {
                Id = "avi",
                Name = "AVI • Audio Video Interleave",
                Extension = ".avi",
                Category = "Standard",
                FfmpegFormat = "avi",
                Description = "Legacy Microsoft multimedia container widely supported by older media players."
            },
            new VideoFormatDefinition
            {
                Id = "ts",
                Name = "TS • MPEG Transport Stream",
                Extension = ".ts",
                Category = "Broadcast",
                FfmpegFormat = "mpegts",
                Description = "Standard broadcast and Blu-ray streaming transport format (MTS, M2TS, TS)."
            },
            new VideoFormatDefinition
            {
                Id = "wmv",
                Name = "WMV • Windows Media Video",
                Extension = ".wmv",
                Category = "Microsoft",
                FfmpegFormat = "asf",
                Description = "Microsoft proprietary video format designed for Windows Media Player and legacy streaming."
            },
            new VideoFormatDefinition
            {
                Id = "flv",
                Name = "FLV • Flash Video",
                Extension = ".flv",
                Category = "Web",
                FfmpegFormat = "flv",
                Description = "Adobe Flash video format used in legacy RTMP web-based video streaming."
            },
            new VideoFormatDefinition
            {
                Id = "f4v",
                Name = "F4V • Flash MP4 Video",
                Extension = ".f4v",
                Category = "Web",
                FfmpegFormat = "flv",
                Description = "H.264/AAC based modern replacement for legacy FLV video."
            },
            new VideoFormatDefinition
            {
                Id = "vob",
                Name = "VOB • DVD Video Object",
                Extension = ".vob",
                Category = "Disc",
                FfmpegFormat = "vob",
                Description = "DVD Video Object container storing MPEG-2 video, AC-3 / DTS surround audio."
            },
            new VideoFormatDefinition
            {
                Id = "ogv",
                Name = "OGV • Ogg Theora Video",
                Extension = ".ogv",
                Category = "Open",
                FfmpegFormat = "ogg",
                Description = "Open-source royalty-free video container developed by the Xiph.Org Foundation."
            },
            new VideoFormatDefinition
            {
                Id = "3gp",
                Name = "3GP • 3GPP Mobile Video",
                Extension = ".3gp",
                Category = "Mobile",
                FfmpegFormat = "3gp",
                Description = "Lightweight multimedia container designed for mobile phones and 3G networks."
            },
            new VideoFormatDefinition
            {
                Id = "3g2",
                Name = "3G2 • 3GPP2 CDMA Mobile",
                Extension = ".3g2",
                Category = "Mobile",
                FfmpegFormat = "3g2",
                Description = "Mobile multimedia container designed for 3G CDMA networks."
            },
            new VideoFormatDefinition
            {
                Id = "m4v",
                Name = "M4V • Apple iTunes Video",
                Extension = ".m4v",
                Category = "Apple",
                FfmpegFormat = "ipod",
                Description = "Video container format developed by Apple, closely related to MP4."
            },
            new VideoFormatDefinition
            {
                Id = "mpeg",
                Name = "MPEG-1 • VCD System Stream",
                Extension = ".mpg",
                Category = "Disc",
                FfmpegFormat = "mpeg",
                Description = "Standard video format with universal playback on legacy DVD/VCD equipment."
            },
            new VideoFormatDefinition
            {
                Id = "m2v",
                Name = "MPEG-2 • Video Stream",
                Extension = ".m2v",
                Category = "Disc",
                FfmpegFormat = "mpeg2video",
                Description = "MPEG-2 elementary video stream used for digital television and DVD authoring."
            },
            new VideoFormatDefinition
            {
                Id = "mxf",
                Name = "MXF • Material Exchange Format",
                Extension = ".mxf",
                Category = "Broadcast",
                FfmpegFormat = "mxf",
                Description = "SMPTE standard professional container for television production and digital cinema."
            },
            new VideoFormatDefinition
            {
                Id = "asf",
                Name = "ASF • Advanced Systems Format",
                Extension = ".asf",
                Category = "Microsoft",
                FfmpegFormat = "asf",
                Description = "Microsoft proprietary streaming format containing audio, video, and script commands."
            },
            new VideoFormatDefinition
            {
                Id = "rm",
                Name = "RM • RealMedia Video",
                Extension = ".rm",
                Category = "Legacy",
                FfmpegFormat = "rm",
                Description = "Proprietary streaming media container created by RealNetworks for RealPlayer."
            },
            new VideoFormatDefinition
            {
                Id = "rmvb",
                Name = "RMVB • RealMedia Variable Bitrate",
                Extension = ".rmvb",
                Category = "Legacy",
                FfmpegFormat = "rm",
                Description = "Variable bitrate extension of RealMedia format optimized for desktop movie distribution."
            },
            new VideoFormatDefinition
            {
                Id = "gifv",
                Name = "GIFV • Video Alternative to GIF",
                Extension = ".gifv",
                Category = "Web",
                FfmpegFormat = "mp4",
                Description = "Mute, looping high-efficiency video container acting as modern lightweight replacement for GIF."
            },
            new VideoFormatDefinition
            {
                Id = "amv",
                Name = "AMV • Action Media Video",
                Extension = ".amv",
                Category = "Portable",
                FfmpegFormat = "amv",
                Description = "Compressed video format developed for portable MP4 and S1 MP3 players."
            },
            new VideoFormatDefinition
            {
                Id = "drc",
                Name = "Dirac • BBC Ultra-HD Video",
                Extension = ".drc",
                Category = "Advanced",
                FfmpegFormat = "dirac",
                Description = "Advanced open-source wavelet-based video compression format created by BBC Research."
            },
            new VideoFormatDefinition
            {
                Id = "yuv",
                Name = "YUV • Raw Uncompressed Video",
                Extension = ".yuv",
                Category = "Raw",
                FfmpegFormat = "rawvideo",
                Description = "Raw planar uncompressed pixel video stream for scientific analysis and video research."
            },
            new VideoFormatDefinition
            {
                Id = "nsv",
                Name = "NSV • Nullsoft Streaming Video",
                Extension = ".nsv",
                Category = "Legacy",
                FfmpegFormat = "nsv",
                Description = "Lightweight streaming container format designed by Nullsoft for Winamp Shoutcast."
            },
            new VideoFormatDefinition
            {
                Id = "roq",
                Name = "ROQ • Id Software Game Video",
                Extension = ".roq",
                Category = "Gaming",
                FfmpegFormat = "roq",
                Description = "Video format developed by id Software for game cutscenes in Quake III Arena."
            },
            new VideoFormatDefinition
            {
                Id = "mng",
                Name = "MNG • Multiple-image Network Graphics",
                Extension = ".mng",
                Category = "Graphics",
                FfmpegFormat = "apng",
                Description = "Multi-frame animated graphics extension of PNG format."
            },
            new VideoFormatDefinition
            {
                Id = "viv",
                Name = "VIV • VivoActive Streaming",
                Extension = ".viv",
                Category = "Legacy",
                FfmpegFormat = "flv",
                Description = "Legacy web streaming format based on H.263 video and G.723 audio."
            },
            new VideoFormatDefinition
            {
                Id = "svi",
                Name = "SVI • Samsung Video Format",
                Extension = ".svi",
                Category = "Portable",
                FfmpegFormat = "mp4",
                Description = "MPEG-4 based proprietary video format produced for Samsung portable media players."
            }
        };

        public static VideoFormatDefinition DefaultFormat => SupportedFormats.First(f => f.Id == "mp4");

        public static VideoFormatDefinition FindById(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return DefaultFormat;
            return SupportedFormats.FirstOrDefault(f => string.Equals(f.Id, id.Trim(), StringComparison.OrdinalIgnoreCase)) ?? DefaultFormat;
        }

        public static VideoFormatDefinition FindByExtension(string? extension)
        {
            if (string.IsNullOrWhiteSpace(extension)) return DefaultFormat;
            var ext = extension.StartsWith('.') ? extension : "." + extension;
            return SupportedFormats.FirstOrDefault(f => string.Equals(f.Extension, ext, StringComparison.OrdinalIgnoreCase)) ?? DefaultFormat;
        }
    }
}
