// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine v1.5.0
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using PRRX.IDM.Models;
using PRRX.IDM.Services;
using PRRX.IDM.ViewModels;
using Xunit;

namespace PRRX.IDM.Tests
{
    public class EngineV150Tests
    {
        [Fact]
        public void AppConfig_DefaultAccelerationMode_IsCloudAccelerated()
        {
            var config = new AppConfig();
            Assert.Equal(DownloadAccelerationMode.CloudAccelerated, config.AccelerationMode);
            Assert.True(config.AutoFailoverToDefaultOnTraffic);
        }

        [Fact]
        public void AppConfig_DefaultProcessPriority_IsHigh()
        {
            var config = new AppConfig();
            Assert.Equal(ProcessPrioritySetting.High, config.ProcessPriority);
        }

        [Fact]
        public void App_ApplyProcessPriority_ExecutesSafelyWithoutThrowing()
        {
            // Verifies that priority setting application succeeds or degrades safely without throwing exceptions
            var exception = Record.Exception(() =>
            {
                App.ApplyProcessPriority(ProcessPrioritySetting.High);
                App.ApplyProcessPriority(ProcessPrioritySetting.AboveNormal);
                App.ApplyProcessPriority(ProcessPrioritySetting.Normal);
            });

            Assert.Null(exception);
        }

        [Fact]
        public void SegmentedDownloadEngine_HighSpeedBufferSize_IsOneMegabyte()
        {
            Assert.Equal(1048576, SegmentedDownloadEngine.HighSpeedBufferSize);
            Assert.Equal(1048576, MultiSegmentDownloader.DefaultBufferSize);
        }

        [Fact]
        public void ServerTrafficReport_Properties_EvaluateStatusCorrectly()
        {
            var normalReport = new ServerTrafficReport
            {
                Status = ServerTrafficStatus.Normal,
                LatencyMs = 45,
                EdgeNode = "sin-01"
            };

            Assert.True(normalReport.IsAcceleratedAvailable);
            Assert.False(normalReport.ShouldFailoverToDefault);
            Assert.Contains("Normal", normalReport.BadgeText);

            var congestedReport = new ServerTrafficReport
            {
                Status = ServerTrafficStatus.Congested,
                LatencyMs = 2800,
                EdgeNode = "sin-01"
            };

            Assert.False(congestedReport.IsAcceleratedAvailable);
            Assert.True(congestedReport.ShouldFailoverToDefault);
            Assert.Contains("High Traffic", congestedReport.BadgeText);
        }

        [Fact]
        public async Task CloudIntelligenceService_EvaluateServerTraffic_ReturnsReport()
        {
            var service = new CloudIntelligenceService();
            var report = await service.EvaluateServerTrafficAsync();

            Assert.NotNull(report);
            Assert.NotNull(service.CurrentTrafficReport);
            Assert.False(string.IsNullOrWhiteSpace(report.BadgeText));
            Assert.False(string.IsNullOrWhiteSpace(report.BadgeColor));
        }

        [Fact]
        public void SettingsViewModel_AccelerationModeToggle_PersistsAndNotifies()
        {
            var config = new ConfigurationService();
            var theme = new ThemeService(config);
            var update = new UpdateService();
            var media = new MediaEngineService(config);

            var vm = new SettingsViewModel(config, theme, update, media);

            // Toggle to Default Direct
            vm.IsDefaultDirect = true;
            Assert.True(vm.IsDefaultDirect);
            Assert.False(vm.IsCloudAccelerated);
            Assert.Equal(DownloadAccelerationMode.DefaultDirect, config.CurrentConfig.AccelerationMode);

            // Toggle to Cloud Accelerated
            vm.IsCloudAccelerated = true;
            Assert.True(vm.IsCloudAccelerated);
            Assert.False(vm.IsDefaultDirect);
            Assert.Equal(DownloadAccelerationMode.CloudAccelerated, config.CurrentConfig.AccelerationMode);
        }

        [Fact]
        public void SettingsViewModel_PrioritySelection_PersistsAndApplies()
        {
            var config = new ConfigurationService();
            var theme = new ThemeService(config);
            var update = new UpdateService();
            var media = new MediaEngineService(config);

            var vm = new SettingsViewModel(config, theme, update, media);

            Assert.NotNull(vm.AvailablePriorities);
            Assert.Equal(3, vm.AvailablePriorities.Count);

            var normalItem = vm.AvailablePriorities.First(p => p.Setting == ProcessPrioritySetting.Normal);
            vm.SelectedPriority = normalItem;
            Assert.Equal(ProcessPrioritySetting.Normal, config.CurrentConfig.ProcessPriority);

            var highItem = vm.AvailablePriorities.First(p => p.Setting == ProcessPrioritySetting.High);
            vm.SelectedPriority = highItem;
            Assert.Equal(ProcessPrioritySetting.High, config.CurrentConfig.ProcessPriority);
        }

        [Fact]
        public void InstallerScripts_ContainLicenseAndV150Version()
        {
            var rootDir = @"D:\Internet Download Manager";
            var offlineScript = Path.Combine(rootDir, "installer.iss");
            var webScript = Path.Combine(rootDir, "web_installer.iss");
            var licenseFile = Path.Combine(rootDir, "LICENSE.txt");

            Assert.True(File.Exists(offlineScript));
            Assert.True(File.Exists(webScript));
            Assert.True(File.Exists(licenseFile));

            var offlineContent = File.ReadAllText(offlineScript);
            var webContent = File.ReadAllText(webScript);

            Assert.Contains("1.5.0", offlineContent);
            Assert.Contains("LicenseFile=", offlineContent);

            Assert.Contains("1.5.0", webContent);
            Assert.Contains("LicenseFile=", webContent);
        }
    }
}
