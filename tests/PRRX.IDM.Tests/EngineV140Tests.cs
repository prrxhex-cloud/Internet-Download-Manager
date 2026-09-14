// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine v1.4.0
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using PRRX.IDM.Models;
using PRRX.IDM.Native;
using PRRX.IDM.Services;
using Xunit;

namespace PRRX.IDM.Tests
{
    public class EngineV140Tests
    {
        [Fact]
        public void NativeFileHelper_FastPreallocate_AllocatesFileSizeWithoutException()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"prrx_test_prealloc_{Guid.NewGuid():N}.dat");
            try
            {
                using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    long targetSize = 2 * 1024 * 1024; // 2 MB
                    bool success = NativeFileHelper.FastPreallocate(fs, targetSize);
                    Assert.True(success);
                    Assert.True(fs.Length >= targetSize);
                }

                var fi = new FileInfo(tempFile);
                Assert.True(fi.Length >= 2 * 1024 * 1024);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public void NativeFileHelper_FastPreallocate_HandlesZeroAndNegativeGracefully()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"prrx_test_zero_{Guid.NewGuid():N}.dat");
            try
            {
                using var fs = new FileStream(tempFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                bool resZero = NativeFileHelper.FastPreallocate(fs, 0);
                Assert.False(resZero);

                bool resNeg = NativeFileHelper.FastPreallocate(fs, -100);
                Assert.False(resNeg);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void MultiSegmentDownloader_InheritsFromSegmentedDownloadEngine_AndConfiguresDefaults()
        {
            var downloader = new MultiSegmentDownloader();
            Assert.IsAssignableFrom<SegmentedDownloadEngine>(downloader);
            Assert.Equal(64, MultiSegmentDownloader.MaxSupportedConcurrency);
            Assert.Equal(524288, MultiSegmentDownloader.DefaultBufferSize);
            Assert.False(downloader.IsRunning);
        }

        [Fact]
        public void MediaDownloaderService_InheritsFromMediaEngineService()
        {
            var config = new ConfigurationService();
            var service = new MediaDownloaderService(config);
            Assert.IsAssignableFrom<MediaEngineService>(service);
            Assert.NotNull(service.EngineExecutablePath);
        }

        [Fact]
        public void UpdateService_Version140_IsLatestReleaseAndProperlyNormalized()
        {
            var update = new UpdateService();
            Assert.Equal("1.4.0", update.CurrentVersionClean);

            // Remote v1.4.0 is not newer than local v1.4.0
            Assert.False(UpdateService.IsVersionNewer("1.4.0", update.CurrentVersionClean));
            Assert.False(UpdateService.IsVersionNewer("v1.4.0", update.CurrentVersionClean));

            // Remote v1.4.1 or v1.5.0 IS newer
            Assert.True(UpdateService.IsVersionNewer("1.4.1", update.CurrentVersionClean));
            Assert.True(UpdateService.IsVersionNewer("v1.5.0", update.CurrentVersionClean));

            // Remote v1.3.0 is NOT newer
            Assert.False(UpdateService.IsVersionNewer("1.3.0", update.CurrentVersionClean));
        }

        [Fact]
        public void BrowserIntegrationService_FixedExtensionId_RemainsStable()
        {
            Assert.Equal("mjcomdjfgmiphnekplhmgdepbhafbjal", BrowserIntegrationService.FixedExtensionId);
        }

        [Fact]
        public void ConfigurationService_V140Defaults_SupportHighConcurrency()
        {
            var config = new ConfigurationService();
            Assert.NotNull(config.CurrentConfig);
            Assert.True(config.CurrentConfig.TurboConnectionCount >= 8);
            Assert.True(config.CurrentConfig.EnableTurboAcceleration);
        }
    }
}
