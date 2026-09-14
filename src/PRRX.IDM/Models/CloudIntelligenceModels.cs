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
    public enum ReputationBadgeStatus
    {
        Safe,
        Suspicious,
        Unrated,
        Neutral
    }

    public class CloudHealthResult
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = "offline";

        [JsonPropertyName("service")]
        public string Service { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("edge_node")]
        public string EdgeNode { get; set; } = string.Empty;

        [JsonPropertyName("client_ip")]
        public string ClientIp { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = string.Empty;

        public bool IsOnline => string.Equals(Status, "online", StringComparison.OrdinalIgnoreCase);
    }

    public class FileReputationResult
    {
        [JsonPropertyName("found")]
        public bool Found { get; set; } = false;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;

        [JsonPropertyName("file_name")]
        public string? FileName { get; set; }

        [JsonPropertyName("file_size")]
        public long? FileSizeBytes { get; set; }

        [JsonPropertyName("safe_votes")]
        public int SafeVotes { get; set; } = 0;

        [JsonPropertyName("malware_reports")]
        public int MalwareReports { get; set; } = 0;

        [JsonPropertyName("download_count")]
        public int DownloadCount { get; set; } = 0;

        [JsonPropertyName("safety_score")]
        public int SafetyScore { get; set; } = 100;

        [JsonPropertyName("verdict")]
        public string Verdict { get; set; } = "unrated";

        [JsonPropertyName("first_seen")]
        public string? FirstSeen { get; set; }

        [JsonPropertyName("last_seen")]
        public string? LastSeen { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        public bool IsSafe => string.Equals(Verdict, "safe", StringComparison.OrdinalIgnoreCase);

        public bool IsSuspicious => string.Equals(Verdict, "suspicious", StringComparison.OrdinalIgnoreCase) || MalwareReports > 0;

        public bool IsUnrated => !Found || string.Equals(Verdict, "unrated", StringComparison.OrdinalIgnoreCase);

        public ReputationBadgeStatus BadgeStatus
        {
            get
            {
                if (IsSuspicious) return ReputationBadgeStatus.Suspicious;
                if (IsSafe) return ReputationBadgeStatus.Safe;
                return ReputationBadgeStatus.Unrated;
            }
        }

        public string BadgeText => BadgeStatus switch
        {
            ReputationBadgeStatus.Suspicious => "⚠️ Suspicious / Malware Reported",
            ReputationBadgeStatus.Safe => $"🛡️ Community Verified: Safe ({SafetyScore}%)",
            _ => "🛡️ Unrated File (New)"
        };

        public string BadgeTooltip => BadgeStatus switch
        {
            ReputationBadgeStatus.Suspicious => $"Warning: {MalwareReports} malware report(s). Community safety score: {SafetyScore}%.",
            ReputationBadgeStatus.Safe => $"Community Verified Safe: {SafeVotes} safe vote(s), {DownloadCount} total download(s).",
            _ => !string.IsNullOrWhiteSpace(Message) ? Message : "New file - No community reports yet. PRRX IDM Cloud Intelligence active."
        };
    }

    public class ReputationReportRequest
    {
        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;

        [JsonPropertyName("file_name")]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("file_size")]
        public long FileSize { get; set; } = 0;

        [JsonPropertyName("vote")]
        public string Vote { get; set; } = "safe";
    }

    public class ReputationReportResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; } = false;

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    public class DomainHealthResult
    {
        [JsonPropertyName("domain")]
        public string Domain { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "unknown";

        [JsonPropertyName("avg_speed_mbps")]
        public double AvgSpeedMbps { get; set; } = 0.0;

        public bool IsOnline => string.Equals(Status, "online", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(Status, "healthy", StringComparison.OrdinalIgnoreCase);

        public string DisplayText
        {
            get
            {
                if (AvgSpeedMbps > 0)
                {
                    return $"⚡ {AvgSpeedMbps:F1} Mbps";
                }
                if (IsOnline)
                {
                    return "⚡ Online";
                }
                if (!string.IsNullOrWhiteSpace(Status) && Status != "unknown")
                {
                    return $"⚡ {Status.ToUpperInvariant()}";
                }
                return string.Empty;
            }
        }
    }

    public class LanPeerItem
    {
        [JsonPropertyName("peer_id")]
        public string PeerId { get; set; } = string.Empty;

        [JsonPropertyName("lan_ip")]
        public string LanIp { get; set; } = string.Empty;

        [JsonPropertyName("port")]
        public int Port { get; set; } = 6881;

        [JsonPropertyName("completed_chunks")]
        public int CompletedChunks { get; set; } = 0;

        [JsonPropertyName("total_chunks")]
        public int TotalChunks { get; set; } = 0;
    }

    public class LanPeersResult
    {
        [JsonPropertyName("matched_network")]
        public string MatchedNetwork { get; set; } = string.Empty;

        [JsonPropertyName("total_peers")]
        public int TotalPeers { get; set; } = 0;

        [JsonPropertyName("peers")]
        public List<LanPeerItem> Peers { get; set; } = new();
    }

    public class PeerAnnounceRequest
    {
        [JsonPropertyName("peer_id")]
        public string PeerId { get; set; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;

        [JsonPropertyName("lan_ip")]
        public string LanIp { get; set; } = string.Empty;

        [JsonPropertyName("port")]
        public int Port { get; set; } = 6881;

        [JsonPropertyName("completed_chunks")]
        public int CompletedChunks { get; set; } = 0;

        [JsonPropertyName("total_chunks")]
        public int TotalChunks { get; set; } = 0;
    }

    public class PeerAnnounceResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; } = false;

        [JsonPropertyName("registered")]
        public bool Registered { get; set; } = false;
    }

    public class MirrorsResult
    {
        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;

        [JsonPropertyName("mirrors")]
        public List<string> Mirrors { get; set; } = new();
    }
}
