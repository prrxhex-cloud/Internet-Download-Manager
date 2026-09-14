// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

namespace PRRX.IDM.Services
{
    /// <summary>
    /// YouTube & Universal Media Downloader Service.
    /// Employs modern tokenless client emulation arguments (android, ios, web_creator)
    /// to extract and stream YouTube media without mandatory cookie files.
    /// </summary>
    public class MediaDownloaderService : MediaEngineService
    {
        public MediaDownloaderService(IConfigurationService? configService = null)
            : base(configService)
        {
        }
    }
}
