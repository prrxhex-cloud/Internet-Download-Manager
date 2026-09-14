// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

using System;
using System.IO;
using System.Runtime.CompilerServices;
using PRRX.IDM.Security;
using PRRX.IDM.Services;

namespace PRRX.IDM.Tests
{
    public static class TestAssemblyInitializer
    {
        private static string? s_testIsolationDir;

        [ModuleInitializer]
        public static void InitializeTests()
        {
            s_testIsolationDir = Path.Combine(Path.GetTempPath(), "PRRX_TestIsolation_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(s_testIsolationDir);
            ConfigurationService.OverrideAppDataFolder = s_testIsolationDir;

            // Purge any pre-existing test artifacts from live history file so user's UI is clean
            try
            {
                var liveHistoryPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PRRX Cooperation",
                    "InternetDownloadManager",
                    "download_history.json");

                if (File.Exists(liveHistoryPath))
                {
                    var liveSec = new SecurityService();
                    HistoryService.PurgeTestArtifacts(liveHistoryPath, liveSec);
                }
            }
            catch
            {
                // Best-effort live cleanup
            }
        }
    }
}
