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
            Assert.False(config.CurrentConfig.IsCookiesEnabled); // Tokenless YouTube emulation by default
        }

        [Theory]
        [InlineData(0, 0, 1)]
        [InlineData(-1, 0, 1)]
        [InlineData(5, 0, 4)]
        [InlineData(5, 32, 5)] // Concurrency must never exceed file size
        [InlineData(2, 64, 2)] // Concurrency clamped to total bytes
        [InlineData(100, 32, 32)]
        [InlineData(2 * 1024 * 1024, 0, 8)]      // 2 MB -> 8 sockets
        [InlineData(10 * 1024 * 1024, 0, 16)]   // 10 MB -> 16 sockets
        [InlineData(30 * 1024 * 1024, 0, 32)]   // 30 MB -> 32 sockets
        [InlineData(150 * 1024 * 1024, 0, 64)]  // 150 MB -> 64 sockets
        [InlineData(200 * 1024 * 1024, 128, 64)]// Clamped to MaxSupportedConcurrency (64)
        public void MultiSegmentDownloader_CalculateOptimalConcurrency_CalculatesAccurately(long totalBytes, int requested, int expected)
        {
            int actual = MultiSegmentDownloader.CalculateOptimalConcurrency(totalBytes, requested);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void MediaEngineService_ResolveCookiesPath_StrictlyOptional_NoScraping()
        {
            var config = new ConfigurationService();
            var service = new MediaEngineService(config);

            // Default: cookies disabled -> null
            Assert.False(config.CurrentConfig.IsCookiesEnabled);
            Assert.Null(service.ResolveCookiesPath());

            // Cookies enabled, but empty path -> null
            config.CurrentConfig.IsCookiesEnabled = true;
            config.CurrentConfig.CookiesFilePath = string.Empty;
            Assert.Null(service.ResolveCookiesPath());

            // Cookies enabled, but nonexistent file -> null
            config.CurrentConfig.CookiesFilePath = @"C:\nonexistent_path_cookie.txt";
            Assert.Null(service.ResolveCookiesPath());

            // Cookies enabled with real temporary file -> returns path
            var tempCookie = Path.GetTempFileName();
            try
            {
                config.CurrentConfig.CookiesFilePath = tempCookie;
                Assert.Equal(tempCookie, service.ResolveCookiesPath());
            }
            finally
            {
                if (File.Exists(tempCookie)) File.Delete(tempCookie);
            }
        }

        [Fact]
        public void ActiveDownloadViewModel_InstantiatesMultiSegmentDownloaderByDefault()
        {
            var vm = new ViewModels.ActiveDownloadViewModel("https://example.com/file.dat", @"C:\temp\file.dat");
            var field = typeof(ViewModels.ActiveDownloadViewModel).GetField("_downloadEngine", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            var engine = field.GetValue(vm);
            Assert.NotNull(engine);
            Assert.IsType<MultiSegmentDownloader>(engine);
        }

        [Fact]
        public void ApplyStandardHeaders_InjectsSmartCinevibesReferer_ForCvCloud()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://cv-cloud.top/download/69a03bc5ff033");
            SegmentedDownloadEngine.ApplyStandardHeaders(request, "https://cv-cloud.top/download/69a03bc5ff033", referer: null, userAgent: null, cookies: null);

            Assert.True(request.Headers.Contains("User-Agent"));
            Assert.True(request.Headers.Contains("Referer"));
            Assert.Equal("https://cinevibes.lk/", request.Headers.Referrer?.ToString());
            Assert.True(request.Headers.Contains("Sec-Fetch-Site"));
            Assert.True(request.Headers.Contains("Sec-Fetch-Mode"));
        }

        [Fact]
        public void ApplyStandardHeaders_PreservesCustomRefererAndCookies()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/file.iso");
            SegmentedDownloadEngine.ApplyStandardHeaders(
                request, 
                "https://example.com/file.iso", 
                referer: "https://custom-portal.org/item/123", 
                userAgent: "CustomIDM/1.4", 
                cookies: "session=abc123xyz; secure=true");

            Assert.Equal("https://custom-portal.org/item/123", request.Headers.Referrer?.ToString());
            Assert.Equal("CustomIDM/1.4", request.Headers.UserAgent.ToString());
            Assert.True(request.Headers.Contains("Cookie"));
        }

        [Fact]
        public void AppConfig_LaunchOnStartup_HasCorrectDefault()
        {
            var config = new AppConfig();
            Assert.False(config.LaunchOnStartup);
            config.LaunchOnStartup = true;
            Assert.True(config.LaunchOnStartup);
        }

        [Fact]
        public void BrowserDownloadPayload_IncludesHeadersAndReferer()
        {
            var json = "{\"action\":\"download\",\"url\":\"https://cv-cloud.top/download/123\",\"referer\":\"https://cinevibes.lk/\",\"userAgent\":\"Mozilla/5.0\",\"cookies\":\"id=test\"}";
            var payload = System.Text.Json.JsonSerializer.Deserialize<BrowserDownloadPayload>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            Assert.NotNull(payload);
            Assert.Equal("https://cv-cloud.top/download/123", payload.Url);
            Assert.Equal("https://cinevibes.lk/", payload.Referer);
            Assert.Equal("Mozilla/5.0", payload.UserAgent);
            Assert.Equal("id=test", payload.Cookies);
        }
    }
}
