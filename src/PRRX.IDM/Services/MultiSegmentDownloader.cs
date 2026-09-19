// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

namespace PRRX.IDM.Services
{
    /// <summary>
    /// Adaptive High-Speed Multi-Socket Segmented Downloader.
    /// Manages parallel download streams (up to 64 sockets), 512KB chunk buffers,
    /// and Win32 direct cluster pre-allocation for high-bandwidth fiber connections.
    /// </summary>
    public class MultiSegmentDownloader : SegmentedDownloadEngine
    {
        public const int MaxSupportedConcurrency = 64;
        public const int DefaultBufferSize = 1048576; // 1 MB Turbo High-Speed Buffer

        /// <summary>
        /// Calculates adaptive multi-socket concurrency based on byte size, ensuring
        /// concurrency never exceeds total bytes and scales up to 64 streams for high-speed fiber.
        /// </summary>
        public static int CalculateOptimalConcurrency(long totalBytes, int requestedConcurrency = 0)
        {
            if (requestedConcurrency > 0)
            {
                int clamped = Math.Clamp(requestedConcurrency, 1, MaxSupportedConcurrency);
                if (totalBytes > 0 && clamped > totalBytes)
                {
                    return (int)Math.Max(1, totalBytes);
                }
                return clamped;
            }

            if (totalBytes > 100 * 1024 * 1024) return 64;      // 100 MB+ : 64 parallel sockets for high-bandwidth fiber
            if (totalBytes > 25 * 1024 * 1024) return 32;       // 25 MB+  : 32 parallel sockets
            if (totalBytes > 5 * 1024 * 1024) return 16;        // 5 MB+   : 16 parallel sockets
            if (totalBytes > 1024 * 1024) return 8;             // 1 MB+   : 8 parallel sockets
            if (totalBytes > 0) return (int)Math.Min(4, Math.Max(1, totalBytes));
            return 1;
        }

        /// <summary>
        /// Initiates high-speed download with adaptive multi-socket concurrency, browser headers, and 512KB chunk buffers.
        /// </summary>
        public System.Threading.Tasks.Task<bool> DownloadAsync(
            string url,
            string destinationFilePath,
            int concurrency = 0,
            System.Threading.CancellationToken cancellationToken = default,
            string? referer = null,
            string? userAgent = null,
            string? cookies = null,
            Dictionary<string, string>? customHeaders = null)
        {
            return StartDownloadAsync(url, destinationFilePath, concurrency, cancellationToken, referer, userAgent, cookies, customHeaders);
        }
    }
}
