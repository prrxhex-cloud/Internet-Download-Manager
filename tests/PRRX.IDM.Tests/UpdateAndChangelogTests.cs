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
            Assert.Equal(8, vm.ChangelogHistory.Count);

            // 1. v1.7.0 Current Release
            var v170 = vm.ChangelogHistory[0];
            Assert.Equal("v1.7.0", v170.Version);
            Assert.True(v170.IsCurrentRelease);
            Assert.True(v170.IsExpanded);
            Assert.Equal("Current Release", v170.StatusBadge);
            Assert.Contains("Telegram", v170.Summary);
            Assert.Contains(v170.Items, i => i.Category == "Features" && i.Description.Contains("Telegram Acceleration"));

            // 2. v1.6.0
            var v160 = vm.ChangelogHistory[1];
            Assert.Equal("v1.6.0", v160.Version);
            Assert.False(v160.IsCurrentRelease);
            Assert.Equal("Previous Release", v160.StatusBadge);
            Assert.Contains("Telegram", v160.Summary);
            Assert.Contains(v160.Items, i => i.Category == "Features" && i.Description.Contains("Telegram Bot"));

            // 3. v1.5.0
            var v150 = vm.ChangelogHistory[2];
            Assert.Equal("v1.5.0", v150.Version);
            Assert.False(v150.IsCurrentRelease);
            Assert.Equal("Previous Release", v150.StatusBadge);
            Assert.Contains("Cloud Install vs. Default Direct", v150.Summary);
            Assert.Contains(v150.Items, i => i.Category == "Features" && i.Description.Contains("Cloud Install vs. Default Direct"));
            Assert.Contains(v150.Items, i => i.Category == "Features" && i.Description.Contains("Circuit Breaker"));
            Assert.Contains(v150.Items, i => i.Category == "Performance" && i.Description.Contains("Highest Process & Network Priority"));
            Assert.Contains(v150.Items, i => i.Category == "Performance" && i.Description.Contains("1 MB High-Speed Fiber"));

            // 4. v1.4.0
            var v140 = vm.ChangelogHistory[3];
            Assert.Equal("v1.4.0", v140.Version);
            Assert.False(v140.IsCurrentRelease);
            Assert.Equal("Previous Release", v140.StatusBadge);
            Assert.Contains("High-speed multi-socket download engine", v140.Summary);
            Assert.Contains(v140.Items, i => i.Category == "Performance" && i.Description.Contains("Adaptive Multi-Socket"));
            Assert.Contains(v140.Items, i => i.Category == "Features" && i.Description.Contains("Tokenless YouTube"));
            Assert.Contains(v140.Items, i => i.Category == "Features" && i.Description.Contains("Cloudflare D1"));
            Assert.Contains(v140.Items, i => i.Category == "Security" && i.Description.Contains("End-to-End Encryption"));

            // 5. v1.3.0
            var v130 = vm.ChangelogHistory[4];
            Assert.Equal("v1.3.0", v130.Version);
            Assert.False(v130.IsCurrentRelease);
            Assert.Contains("In-App Seamless", v130.Summary);

            // 6. v1.2.0
            var v120 = vm.ChangelogHistory[5];
            Assert.Equal("v1.2.0", v120.Version);
            Assert.False(v120.IsCurrentRelease);

            // 7. v1.1.0
            var v110 = vm.ChangelogHistory[6];
            Assert.Equal("v1.1.0", v110.Version);
            Assert.False(v110.IsCurrentRelease);

            // 8. v1.0.0
            var v100 = vm.ChangelogHistory[7];
            Assert.Equal("v1.0.0", v100.Version);
            Assert.False(v100.IsCurrentRelease);
        }

        [Fact]
        public void UpdateService_CurrentVersion_ReflectsV1_7_0()
        {
            var service = new UpdateService();
            Assert.NotNull(service.CurrentVersion);
            Assert.Equal("1.7.0", service.CurrentVersionClean);
        }

        [Theory]
        [InlineData("1.3.0", "1.3.0", false)]
        [InlineData("v1.3.0", "1.3.0", false)]
        [InlineData("1.3.0", "v1.3.0", false)]
        [InlineData("1.3.0.0", "1.3.0", false)]
        [InlineData("1.3.0", "1.3.0.0", false)]
        [InlineData("1.2.0", "1.3.0", false)]
        [InlineData("1.2.9.9", "1.3.0", false)]
        [InlineData("1.4.0", "1.3.0", true)]
        [InlineData("v1.4.0", "1.3.0", true)]
        [InlineData("1.3.1", "1.3.0", true)]
        [InlineData("1.3.0.1", "1.3.0", true)]
        [InlineData("2.0.0", "1.3.0", true)]
        [InlineData("v2", "1.3.0", true)]
        [InlineData("v1.2", "1.3.0", false)]
        [InlineData("1.3.0-rc1", "1.3.0", false)]
        [InlineData("1.3.0+build123", "1.3.0", false)]
        [InlineData("1.4.0-preview", "1.3.0", true)]
        [InlineData("nightly-alpha", "1.3.0", false)]
        [InlineData("", "1.3.0", false)]
        [InlineData(null, "1.3.0", false)]
        public void UpdateService_IsVersionNewer_StrictComparison(string? remoteVersion, string? localVersion, bool expectedIsNewer)
        {
            bool isNewer = UpdateService.IsVersionNewer(remoteVersion, localVersion);
            Assert.Equal(expectedIsNewer, isNewer);
        }

        [Fact]
        public async System.Threading.Tasks.Task SettingsViewModel_WhenRemoteVersionEqualsCurrent_IsUpdateAvailableIsFalseAndButtonHidden()
        {
            var config = new ConfigurationService();
            var theme = new ThemeService(config);
            var media = new MediaEngineService(config);

            var fakeUpdate = new FakeUpdateService
            {
                CurrentVersion = new Version(1, 3, 0),
                ReleasesResult = (UpdateService.IsVersionNewer("1.3.0", "1.3.0"), new UpdateManifest { Version = "1.3.0" }, null)
            };

            var vm = new SettingsViewModel(config, theme, fakeUpdate, media);
            await vm.CheckGitHubUpdatesAsync();

            Assert.False(vm.IsUpdateAvailable);
            Assert.False(vm.CanApplyUpdate);
            Assert.False(vm.UpdateNowInAppCommand.CanExecute(null));
            Assert.Equal("You're up to date! PRRX IDM v1.3.0 is the latest version.", vm.UpdateStatusMessage);
        }

        [Fact]
        public async System.Threading.Tasks.Task SettingsViewModel_WhenRemoteVersionIsOlder_IsUpdateAvailableIsFalseAndButtonHidden()
        {
            var config = new ConfigurationService();
            var theme = new ThemeService(config);
            var media = new MediaEngineService(config);

            var fakeUpdate = new FakeUpdateService
            {
                CurrentVersion = new Version(1, 3, 0),
                ReleasesResult = (UpdateService.IsVersionNewer("1.2.0", "1.3.0"), new UpdateManifest { Version = "1.2.0" }, null)
            };

            var vm = new SettingsViewModel(config, theme, fakeUpdate, media);
            await vm.CheckGitHubUpdatesAsync();

            Assert.False(vm.IsUpdateAvailable);
            Assert.False(vm.CanApplyUpdate);
            Assert.False(vm.UpdateNowInAppCommand.CanExecute(null));
            Assert.Equal("You're up to date! PRRX IDM v1.3.0 is the latest version.", vm.UpdateStatusMessage);
        }

        [Fact]
        public async System.Threading.Tasks.Task SettingsViewModel_WhenRemoteVersionIsNewer_IsUpdateAvailableIsTrueAndButtonVisible()
        {
            var config = new ConfigurationService();
            var theme = new ThemeService(config);
            var media = new MediaEngineService(config);

            var fakeUpdate = new FakeUpdateService
            {
                CurrentVersion = new Version(1, 3, 0),
                ReleasesResult = (UpdateService.IsVersionNewer("1.4.0", "1.3.0"), new UpdateManifest { Version = "1.4.0" }, null)
            };

            var vm = new SettingsViewModel(config, theme, fakeUpdate, media);
            await vm.CheckGitHubUpdatesAsync();

            Assert.True(vm.IsUpdateAvailable);
            Assert.True(vm.CanApplyUpdate);
            Assert.True(vm.UpdateNowInAppCommand.CanExecute(null));
            Assert.Contains("New Release Found: v1.4.0", vm.UpdateStatusMessage);
        }

        [Fact]
        public void SettingsViewModel_AppVersion_DynamicallyReflectsCurrentVersion()
        {
            var config = new ConfigurationService();
            var theme = new ThemeService(config);
            var media = new MediaEngineService(config);

            var fakeUpdate = new FakeUpdateService
            {
                CurrentVersion = new Version(2, 5, 0)
            };

            var vm = new SettingsViewModel(config, theme, fakeUpdate, media);
            Assert.Equal("v2.5.0 (Official Release)", vm.AppVersion);
        }

        [Fact]
        public void SettingsViewModel_ChangelogHistory_DynamicallyUpdatesCurrentRelease_WhenRunningNewerVersion()
        {
            var config = new ConfigurationService();
            var theme = new ThemeService(config);
            var media = new MediaEngineService(config);

            var fakeUpdate = new FakeUpdateService
            {
                CurrentVersion = new Version(1, 7, 0)
            };

            var vm = new SettingsViewModel(config, theme, fakeUpdate, media);
            Assert.NotNull(vm.ChangelogHistory);
            Assert.True(vm.ChangelogHistory.Count >= 6);
            Assert.Equal("v1.7.0", vm.ChangelogHistory[0].Version);
            Assert.True(vm.ChangelogHistory[0].IsCurrentRelease);
            Assert.Equal("Current Release", vm.ChangelogHistory[0].StatusBadge);

            // Previous release v1.6.0 is no longer current release
            Assert.False(vm.ChangelogHistory[1].IsCurrentRelease);
        }

        [Fact]
        public void UpdateService_ParseNormalizedVersion_CorrectlyHandlesVariations()
        {
            Assert.Equal(new Version(1, 3, 0, 0), UpdateService.ParseNormalizedVersion("v1.3.0"));
            Assert.Equal(new Version(1, 3, 0, 0), UpdateService.ParseNormalizedVersion("1.3.0"));
            Assert.Equal(new Version(1, 3, 0, 2), UpdateService.ParseNormalizedVersion("v1.3.0.2"));
            Assert.Equal(new Version(2, 0, 0, 0), UpdateService.ParseNormalizedVersion("2"));
            Assert.Equal(new Version(1, 4, 0, 0), UpdateService.ParseNormalizedVersion("v1.4.0-beta.1+sha.abc"));
            Assert.Equal(new Version(0, 0, 0, 0), UpdateService.ParseNormalizedVersion("invalid"));
            Assert.Equal(new Version(0, 0, 0, 0), UpdateService.ParseNormalizedVersion(null));
        }

        private class FakeUpdateService : IUpdateService
        {
            public Version CurrentVersion { get; set; } = new Version(1, 3, 0);
            public string CurrentVersionClean => CurrentVersion.Revision > 0 
                ? $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{Math.Max(0, CurrentVersion.Build)}.{CurrentVersion.Revision}" 
                : $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{Math.Max(0, CurrentVersion.Build)}";
            public (bool UpdateAvailable, UpdateManifest? Manifest, string? ErrorMessage) ReleasesResult { get; set; }

            public System.Threading.Tasks.Task<(bool UpdateAvailable, UpdateManifest? Manifest, string? ErrorMessage)> CheckGitHubReleasesAsync(string? repoOwnerAndName = null)
            {
                return System.Threading.Tasks.Task.FromResult(ReleasesResult);
            }

            public System.Threading.Tasks.Task<bool> DownloadAndVerifyUpdateAsync(UpdateManifest manifest, string destinationPath, IProgress<double>? progress = null) => System.Threading.Tasks.Task.FromResult(true);
            public System.Threading.Tasks.Task<(bool Success, string Message)> ApplyInAppUpdateAsync(UpdateManifest manifest, IProgress<double>? progress = null, Action? beforeShutdown = null, System.Threading.CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.FromResult((true, "OK"));
            public System.Threading.Tasks.Task<CleanupReport> CleanupPostUpdateArtifactsAsync(string? customAppDir = null) => System.Threading.Tasks.Task.FromResult(new CleanupReport());
            public CleanupReport CleanupPostUpdateArtifacts(string? customAppDir = null) => new CleanupReport();
        }
    }
}
