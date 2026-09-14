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
        public const int DefaultBufferSize = 524288; // 512 KB
    }
}
