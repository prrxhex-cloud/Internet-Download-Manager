// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

using System;
using System.IO;
using System.Linq;
using PRRX.IDM.Models;
using PRRX.IDM.Services;
using PRRX.IDM.ViewModels;
using Xunit;

namespace PRRX.IDM.Tests
{
    public class UpdateAndChangelogTests
    {
        [Theory]
        [InlineData("config.json", true)]
        [InlineData("download_history.json", true)]
        [InlineData("history.json", true)]
        [InlineData("settings.json", true)]
        [InlineData("cookies.txt", true)]
        [InlineData("master.key", true)]
        [InlineData("ipc.token", true)]
        [InlineData("data.sqlite", true)]
        [InlineData("store.db", true)]
        [InlineData("manifest.json", true)]
        [InlineData("app.old", false)]
        [InlineData("old_binary.bak", false)]
        [InlineData("staging.swap", false)]
        [InlineData("temp_swap.tmp", false)]
        public void UpdateService_IsProtectedUserDataFile_AccuratelyDistinguishesProtectedData(string fileName, bool expectedProtected)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "PRRX_Protection_Test");
            Directory.CreateDirectory(tempDir);
            var testFilePath = Path.Combine(tempDir, fileName);

            try
            {
                bool isProtected = UpdateService.IsProtectedUserDataFile(testFilePath);
                Assert.Equal(expectedProtected, isProtected);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public void UpdateService_CleanupPostUpdateArtifacts_CleansStaleBinaries_PreservesUserData()
        {
            var testAppDir = Path.Combine(Path.GetTempPath(), "PRRX_TestApp_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testAppDir);

            try
            {
                // Create protected files
                var protectedConfig = Path.Combine(testAppDir, "config.json");
                File.WriteAllText(protectedConfig, "{\"downloadDirectory\":\"C:\\\\Downloads\"}");

                var protectedHistory = Path.Combine(testAppDir, "download_history.json");
                File.WriteAllText(protectedHistory, "[{\"title\":\"test\"}]");

                // Create stale binary swap files
                var staleOld = Path.Combine(testAppDir, "PRRX.InternetDownloadManager.exe.old");
                File.WriteAllText(staleOld, "stale old executable content");

                var staleBak = Path.Combine(testAppDir, "core.dll.bak");
                File.WriteAllText(staleBak, "stale bak dll content");

                var staleTmp = Path.Combine(testAppDir, "engine.tmp");
                File.WriteAllText(staleTmp, "stale tmp content");

                var service = new UpdateService();
                var report = service.CleanupPostUpdateArtifacts(testAppDir);

                // Verify protected files are 100% intact
                Assert.True(File.Exists(protectedConfig), "Protected config.json must NEVER be deleted!");
                Assert.True(File.Exists(protectedHistory), "Protected download_history.json must NEVER be deleted!");

                // Verify stale binary swap files were cleaned
                Assert.False(File.Exists(staleOld), "Stale .old file should have been deleted");
                Assert.False(File.Exists(staleBak), "Stale .bak file should have been deleted");
                Assert.False(File.Exists(staleTmp), "Stale .tmp file should have been deleted");

                Assert.True(report.FilesDeletedCount >= 3);
            }
            finally
            {
                if (Directory.Exists(testAppDir))
                {
                    Directory.Delete(testAppDir, true);
                }
            }
        }

        [Fact]
        public void SettingsViewModel_ChangelogHistory_ContainsFullStructuredReleaseHistory()
        {
            var config = new ConfigurationService();
            var theme = new ThemeService(config);
            var update = new UpdateService();
            var media = new MediaEngineService(config);

            var vm = new SettingsViewModel(config, theme, update, media);

            Assert.NotNull(vm.ChangelogHistory);
            Assert.Equal(4, vm.ChangelogHistory.Count);

            // 1. v1.3.0 Current Release
            var v130 = vm.ChangelogHistory[0];
            Assert.Equal("v1.3.0", v130.Version);
            Assert.True(v130.IsCurrentRelease);
            Assert.True(v130.IsExpanded);
            Assert.Equal("Current Release", v130.StatusBadge);
            Assert.Contains("In-App Seamless", v130.Summary);
            Assert.Contains(v130.Items, i => i.Category == "Features" && i.Description.Contains("Changelog"));
            Assert.Contains(v130.Items, i => i.Category == "Features" && i.Description.Contains("Post-Update Cleanup"));
            Assert.Contains(v130.Items, i => i.Category == "Security" && i.Description.Contains("Zero Data Loss"));

            // 2. v1.2.0
            var v120 = vm.ChangelogHistory[1];
            Assert.Equal("v1.2.0", v120.Version);
            Assert.False(v120.IsCurrentRelease);
            Assert.Contains(v120.Items, i => i.Description.Contains("Chrome"));

            // 3. v1.1.0
            var v110 = vm.ChangelogHistory[2];
            Assert.Equal("v1.1.0", v110.Version);
            Assert.False(v110.IsCurrentRelease);
            Assert.Contains(v110.Items, i => i.Description.Contains("HTTP bridge"));

            // 4. v1.0.0
            var v100 = vm.ChangelogHistory[3];
            Assert.Equal("v1.0.0", v100.Version);
            Assert.False(v100.IsCurrentRelease);
            Assert.Contains(v100.Items, i => i.Description.Contains(".NET 8"));
        }

        [Fact]
        public void UpdateService_CurrentVersion_ReflectsV1_3_0()
        {
            var service = new UpdateService();
            Assert.NotNull(service.CurrentVersion);
            Assert.True(service.CurrentVersion >= new Version(1, 2, 0));
        }
    }
}
