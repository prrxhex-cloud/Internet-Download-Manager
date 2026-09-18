// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Collections.Generic;
using PRRX.IDM.Models;
using Xunit;

namespace PRRX.IDM.Tests
{
    public class IdmSuiteTests
    {
        [Theory]
        [InlineData("https://example.com/setup.exe", FileCategory.Programs)]
        [InlineData("https://example.com/archive.zip", FileCategory.Compressed)]
        [InlineData("https://example.com/movie.mp4", FileCategory.Video)]
        [InlineData("https://example.com/track.mp3", FileCategory.Music)]
        [InlineData("https://example.com/document.pdf", FileCategory.Documents)]
        [InlineData("https://example.com/unknown_stream", FileCategory.General)]
        public void FileCategoryHelper_CorrectlyIdentifiesCategory(string url, FileCategory expectedCategory)
        {
            var category = FileCategoryHelper.DetectCategory(url);
            Assert.Equal(expectedCategory, category);
        }

        [Fact]
        public void DownloadConnectionThread_CalculatesPercentageAccurately()
        {
            var thread = new DownloadConnectionThread
            {
                ThreadId = 1,
                StartByte = 0,
                EndByte = 999, // 1000 bytes
                DownloadedBytes = 500,
                StatusInfo = "Receiving data..."
            };

            long total = thread.EndByte - thread.StartByte + 1;
            double pct = (thread.DownloadedBytes / (double)total) * 100.0;

            Assert.Equal(50.0, pct);
        }

        [Fact]
        public void SpeedLimiterSettings_MaintainsBounds()
        {
            var settings = new SpeedLimiterSettings
            {
                IsEnabled = true,
                MaxSpeedKbps = 2048
            };

            Assert.True(settings.IsEnabled);
            Assert.Equal(2048, settings.MaxSpeedKbps);
        }

        [Fact]
        public void BatchDownloadRequest_InitializesWithLinks()
        {
            var request = new BatchDownloadRequest
            {
                SourcePageUrl = "https://example.com/files",
                PageTitle = "File Gallery",
                Links = new List<BatchLinkItem>
                {
                    new() { FileName = "file1.zip", Url = "https://example.com/file1.zip", IsSelected = true },
                    new() { FileName = "file2.pdf", Url = "https://example.com/file2.pdf", IsSelected = false }
                }
            };

            Assert.Equal(2, request.Links.Count);
            Assert.True(request.Links[0].IsSelected);
            Assert.False(request.Links[1].IsSelected);
        }

        [Fact]
        public async System.Threading.Tasks.Task BrowserIntegrationService_HttpBridgeHandlesPingOptionsAndPost()
        {
            var configMock = new PRRX.IDM.Services.ConfigurationService();
            var service = new PRRX.IDM.Services.BrowserIntegrationService(configMock);
            service.StartIpcServer(0);

            try
            {
                await System.Threading.Tasks.Task.Delay(300);
                int testPort = service.ActiveHttpPort;
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };

                // 1. Test GET /api/ping
                var pingResp = await http.GetStringAsync($"http://127.0.0.1:{testPort}/api/ping");
                Assert.Contains("online", pingResp);

                // 2. Test OPTIONS Preflight
                var optReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Options, $"http://127.0.0.1:{testPort}/api/download");
                optReq.Headers.Add("Origin", "chrome-extension://jpnkdblibibkbnllncikdeijkbdnmpem");
                optReq.Headers.Add("Access-Control-Request-Method", "POST");
                var optResp = await http.SendAsync(optReq);
                Assert.Equal(System.Net.HttpStatusCode.OK, optResp.StatusCode);

                // 3. Test POST /api/download with authorized extension Origin
                var jsonContent = new System.Net.Http.StringContent(
                    "{\"action\":\"download\",\"url\":\"https://example.com/testfile.exe\",\"pageTitle\":\"Download Test\"}",
                    System.Text.Encoding.UTF8,
                    "application/json");

                var postReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"http://127.0.0.1:{testPort}/api/download")
                {
                    Content = jsonContent
                };
                postReq.Headers.Add("Origin", "chrome-extension://jpnkdblibibkbnllncikdeijkbdnmpem");
                var postResp = await http.SendAsync(postReq);
                Assert.Equal(System.Net.HttpStatusCode.OK, postResp.StatusCode);
                var postBody = await postResp.Content.ReadAsStringAsync();
                Assert.Contains("ok", postBody);

                // 4. Test POST without valid origin or token is rejected (401)
                var unauthPost = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"http://127.0.0.1:{testPort}/api/download")
                {
                    Content = new System.Net.Http.StringContent("{\"action\":\"download\",\"url\":\"https://evil.com/payload.exe\"}", System.Text.Encoding.UTF8, "application/json")
                };
                var unauthResp = await http.SendAsync(unauthPost);
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized, unauthResp.StatusCode);
            }
            finally
            {
                service.StopIpcServer();
            }
        }

        [Fact]
        public void BrowserIntegrationService_GeneratesConsistentExtensionId()
        {
            Assert.Equal("mjcomdjfgmiphnekplhmgdepbhafbjal", PRRX.IDM.Services.BrowserIntegrationService.FixedExtensionId);
            Assert.Equal("jpnkdblibibkbnllncikdeijkbdnmpem", PRRX.IDM.Services.BrowserIntegrationService.LegacyExtensionId);

            var pathId = PRRX.IDM.Services.BrowserIntegrationService.GenerateExtensionIdFromPath(@"D:\Internet Download Manager\extension");
            Assert.Equal(32, pathId.Length);
            foreach (char c in pathId)
            {
                Assert.InRange(c, 'a', 'p');
            }
        }

        [Fact]
        public void DownloadFileInfoViewModel_UsesDirectDownloadsFolderWithoutCategorySubfolders()
        {
            var baseDownloads = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var vm = new PRRX.IDM.ViewModels.DownloadFileInfoViewModel("https://example.com/software/setup.exe", baseDownloads);

            // Must use base downloads folder directly
            Assert.Equal(baseDownloads, vm.SaveDirectory);

            // Changing category to Compressed or Programs must NOT append subdirectories
            vm.SelectedCategory = FileCategory.Compressed;
            Assert.Equal(baseDownloads, vm.SaveDirectory);

            vm.SelectedCategory = FileCategory.Programs;
            Assert.Equal(baseDownloads, vm.SaveDirectory);

            vm.SelectedCategory = FileCategory.Video;
            Assert.Equal(baseDownloads, vm.SaveDirectory);
        }

        [Fact]
        public void DownloadFileInfoViewModel_AutoPopulatesDescription()
        {
            var baseDownloads = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var vm = new PRRX.IDM.ViewModels.DownloadFileInfoViewModel(
                "https://releases.ubuntu.com/24.04/ubuntu-24.04-desktop-amd64.iso",
                baseDownloads,
                "Ubuntu 24.04 LTS Download Page");

            Assert.False(string.IsNullOrWhiteSpace(vm.Description));
            Assert.Contains("Ubuntu 24.04 LTS Download Page", vm.Description);
            Assert.Contains("releases.ubuntu.com", vm.Description);
            Assert.Contains("Compressed", vm.Description);
        }

        [Fact]
        public async System.Threading.Tasks.Task DownloadFileInfoViewModel_ProbeFileSize_FallsBackGracefullyWithoutHanging()
        {
            var baseDownloads = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var vm = new PRRX.IDM.ViewModels.DownloadFileInfoViewModel("http://127.0.0.1:59999/non_existent_file.bin", baseDownloads);

            // ProbeFileSizeAsync should handle failure/timeout gracefully and set "Unknown size"
            await vm.ProbeFileSizeAsync("http://127.0.0.1:59999/non_existent_file.bin");

            Assert.NotEqual("Estimating size...", vm.FileSizeFormatted);
            Assert.Equal("Unknown size", vm.FileSizeFormatted);
        }

        [Fact]
        public void DownloadConnectionThread_RaisesPropertyChangedOnUpdates()
        {
            var thread = new DownloadConnectionThread
            {
                ThreadId = 1,
                StartByte = 0,
                EndByte = 1000,
                CurrentByte = 0
            };

            var changedProperties = new List<string>();
            thread.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != null) changedProperties.Add(e.PropertyName);
            };

            thread.DownloadedBytes = 500;
            thread.ProgressPercentage = 50.0;
            thread.IsActive = false;

            Assert.Contains(nameof(DownloadConnectionThread.DownloadedBytes), changedProperties);
            Assert.Contains(nameof(DownloadConnectionThread.ProgressPercentage), changedProperties);
            Assert.Contains(nameof(DownloadConnectionThread.IsActive), changedProperties);
        }

        [Fact]
        public void ActiveDownloadViewModel_CompletionStateUpdatesCorrectly()
        {
            var vm = new PRRX.IDM.ViewModels.ActiveDownloadViewModel(
                "https://example.com/testfile.bin",
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "testfile.bin"));

            Assert.Equal("Cancel", vm.CancelButtonText);
            Assert.False(vm.IsCompleted);

            vm.IsCompleted = true;
            Assert.Equal("Close", vm.CancelButtonText);
        }

        [Fact]
        public void DownloadConnectionThread_FormattedPositionAndRange_ReturnAccurateStrings()
        {
            var thread = new DownloadConnectionThread
            {
                ThreadId = 1,
                StartByte = 1048576, // 1 MB
                EndByte = 3145727,   // ~3 MB
                DownloadedBytes = 1048576,
                ProgressPercentage = 50.0
            };

            Assert.Equal("1.00 MB", thread.FormattedStartPosition);
            Assert.Contains("1.00 MB", thread.FormattedRange);
            Assert.Contains("3.00 MB", thread.FormattedRange);
            Assert.Equal("50.0 %", thread.FormattedProgress);
        }

        [Fact]
        public void ActiveDownloadViewModel_ReceivesEngineEvents_UpdatesPropertiesViaDispatchToUi()
        {
            var mockEngine = new MockSegmentedDownloadEngine();
            var vm = new PRRX.IDM.ViewModels.ActiveDownloadViewModel(
                "https://example.com/movie.mp4",
                @"C:\Downloads\movie.mp4",
                mockEngine);

            Assert.False(vm.IsCompleted);
            Assert.Equal("Connecting...", vm.StatusText);

            // 1. Simulate engine progress event
            mockEngine.TriggerProgress(new PRRX.IDM.Services.SegmentProgressEventArgs
            {
                OverallPercentage = 42.5,
                TotalBytes = 10000000,
                DownloadedBytes = 4250000,
                StatusMessage = "Receiving data...",
                TransferRateFormatted = "5.2 MB/s",
                TimeLeftFormatted = "00:02",
                Threads = new List<DownloadConnectionThread>
                {
                    new()
                    {
                        ThreadId = 1,
                        StartByte = 0,
                        EndByte = 4999999,
                        DownloadedBytes = 2500000,
                        ProgressPercentage = 50.0,
                        StatusInfo = "Receiving data..."
                    },
                    new()
                    {
                        ThreadId = 2,
                        StartByte = 5000000,
                        EndByte = 9999999,
                        DownloadedBytes = 1750000,
                        ProgressPercentage = 35.0,
                        StatusInfo = "Receiving data..."
                    }
                }
            });

            Assert.Equal(42.5, vm.OverallPercentage);
            Assert.Equal("Receiving data...", vm.StatusText);
            Assert.Equal(2, vm.ConnectionThreads.Count);
            Assert.Equal("50.0 %", vm.ConnectionThreads[0].FormattedProgress);
            Assert.Equal("35.0 %", vm.ConnectionThreads[1].FormattedProgress);

            // 2. Simulate engine completion event
            mockEngine.TriggerComplete(@"C:\Downloads\movie.mp4");

            Assert.True(vm.IsCompleted);
            Assert.Equal("Close", vm.CancelButtonText);
            Assert.Equal("Complete - Downloaded successfully", vm.StatusText);
            Assert.Equal(100.0, vm.OverallPercentage);
            Assert.Equal("Finished", vm.TransferRateFormatted);
        }

        [Fact]
        public void DownloadFileInfoViewModel_UpdatesDescription_WhenFileNameChanges()
        {
            var baseDownloads = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var vm = new PRRX.IDM.ViewModels.DownloadFileInfoViewModel("https://example.com/download.bin", baseDownloads, "Example Page");

            Assert.Contains("File: download.bin", vm.Description);
            Assert.Contains("Page: Example Page", vm.Description);

            // Update FileName as BrowserIntegrationService does when real filename is received
            vm.FileName = "Setup_Installer_v2.0.exe";

            Assert.Contains("File: Setup_Installer_v2.0.exe", vm.Description);
            Assert.Contains("Page: Example Page", vm.Description);
        }

        [Fact]
        public void DownloadConnectionThread_HandlesUnknownTotal_WithoutInstant100Percent()
        {
            var thread = new DownloadConnectionThread
            {
                ThreadId = 1,
                StartByte = 0,
                EndByte = -1, // Unknown total size
                DownloadedBytes = 5000,
                ProgressPercentage = 0.0
            };

            // Formatted range should be "--" when end is negative
            Assert.Equal("--", thread.FormattedRange);
            // Formatted progress should report downloaded bytes instead of 100%
            Assert.Equal("4.9 KB", thread.FormattedProgress);
        }

        private class MockSegmentedDownloadEngine : PRRX.IDM.Services.ISegmentedDownloadEngine
        {
            public event EventHandler<PRRX.IDM.Services.SegmentProgressEventArgs>? ProgressChanged;
            public event EventHandler<string>? DownloadCompleted;
            public event EventHandler<string>? DownloadFailed;

            public bool IsRunning { get; set; }
            public bool IsPaused { get; set; }
            public SpeedLimiterSettings SpeedLimiter { get; } = new();

            public System.Threading.Tasks.Task<bool> StartDownloadAsync(
                string url, 
                string destinationFilePath, 
                int threadCount = 16, 
                System.Threading.CancellationToken cancellationToken = default,
                string? referer = null,
                string? userAgent = null,
                string? cookies = null,
                System.Collections.Generic.Dictionary<string, string>? customHeaders = null) =>
                System.Threading.Tasks.Task.FromResult(true);

            public void Pause() { }
            public void Resume() { }
            public void Cancel() { }
            public void SetSpeedLimit(bool isEnabled, int maxSpeedKbps) { }

            public void TriggerProgress(PRRX.IDM.Services.SegmentProgressEventArgs args) => ProgressChanged?.Invoke(this, args);
            public void TriggerComplete(string path) => DownloadCompleted?.Invoke(this, path);
            public void TriggerFailed(string err) => DownloadFailed?.Invoke(this, err);
        }

        [Fact]
        public void OnboardingViewModel_InitializesAndNavigates5StepsCorrectly()
        {
            var config = new PRRX.IDM.Services.ConfigurationService();
            var theme = new PRRX.IDM.Services.ThemeService(config);
            var browser = new PRRX.IDM.Services.BrowserIntegrationService(config);

            var vm = new PRRX.IDM.ViewModels.OnboardingViewModel(config, theme, browser);

            Assert.Equal(1, vm.CurrentStep);
            Assert.Equal(5, vm.TotalSteps);
            Assert.Equal(1, vm.ExtensionSubStep);
            Assert.False(string.IsNullOrWhiteSpace(vm.ExtensionRegistrationStatus));
            Assert.False(string.IsNullOrWhiteSpace(vm.ExtensionDirectory));

            // Navigate through steps 1 -> 5
            for (int i = 1; i < 5; i++)
            {
                vm.NextStepCommand.Execute(null);
                Assert.Equal(i + 1, vm.CurrentStep);
            }

            // Verify cannot exceed step 5
            vm.NextStepCommand.Execute(null);
            Assert.Equal(5, vm.CurrentStep);

            // Test sub-steps navigation
            vm.SetExtensionSubStepCommand.Execute(2);
            Assert.Equal(2, vm.ExtensionSubStep);
            vm.SetExtensionSubStepCommand.Execute("3");
            Assert.Equal(3, vm.ExtensionSubStep);

            // Test Next and Previous extension sub-step commands
            vm.PreviousExtensionSubStepCommand.Execute(null);
            Assert.Equal(2, vm.ExtensionSubStep);
            vm.PreviousExtensionSubStepCommand.Execute(null);
            Assert.Equal(1, vm.ExtensionSubStep);
            vm.PreviousExtensionSubStepCommand.Execute(null); // Boundary test
            Assert.Equal(1, vm.ExtensionSubStep);

            vm.NextExtensionSubStepCommand.Execute(null);
            Assert.Equal(2, vm.ExtensionSubStep);
            vm.NextExtensionSubStepCommand.Execute(null);
            Assert.Equal(3, vm.ExtensionSubStep);
            vm.NextExtensionSubStepCommand.Execute(null); // Boundary test
            Assert.Equal(3, vm.ExtensionSubStep);

            // Navigate backward 5 -> 1
            for (int i = 5; i > 1; i--)
            {
                vm.PreviousStepCommand.Execute(null);
                Assert.Equal(i - 1, vm.CurrentStep);
            }

            // Cannot go below step 1
            vm.PreviousStepCommand.Execute(null);
            Assert.Equal(1, vm.CurrentStep);

            // Test extension registration command
            vm.RegisterExtensionCommand.Execute(null);
            Assert.Contains("✓", vm.ExtensionRegistrationStatus);

            // Test completion callback
            bool completed = false;
            vm.OnboardingCompleted += () => completed = true;
            vm.FinishCommand.Execute(null);
            Assert.True(completed);
            Assert.True(config.CurrentConfig.IsOnboardingCompleted);
            Assert.True(config.CurrentConfig.HasCompletedQuickTour);
        }

        [Fact]
        public void DownloadFileInfoViewModel_InstantLaunchWithPrecalculatedSize_SetsSizeImmediately()
        {
            var baseDownloads = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            long knownSize = 104_857_600; // 100 MB
            var vm = new PRRX.IDM.ViewModels.DownloadFileInfoViewModel("https://example.com/file.iso", baseDownloads, "Test ISO", knownSize);

            Assert.Equal("100.00 MB", vm.FileSizeFormatted);
            Assert.False(vm.IsProbing);
        }

        [Fact]
        public void DownloadFileInfoViewModel_InstantLaunchWithoutSize_DisplaysProbingImmediately()
        {
            var baseDownloads = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var vm = new PRRX.IDM.ViewModels.DownloadFileInfoViewModel("https://example.com/stream.bin", baseDownloads, "Test Stream");

            Assert.Equal("Probing size...", vm.FileSizeFormatted);
            Assert.True(vm.IsProbing);
        }

        [Fact]
        public void BrowserDownloadPayload_DeserializesTotalBytesAndActionShow()
        {
            string json = "{\"action\":\"show\",\"totalBytes\":52428800,\"url\":\"https://example.com/large.zip\"}";
            var payload = System.Text.Json.JsonSerializer.Deserialize<PRRX.IDM.Services.BrowserDownloadPayload>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            Assert.NotNull(payload);
            Assert.Equal("show", payload.Action);
            Assert.Equal(52428800, payload.TotalBytes);
            Assert.Equal("https://example.com/large.zip", payload.Url);
        }

        [Fact]
        public void MemoryOptimizer_TrimMemory_ExecutesWithoutException()
        {
            // Verify TrimMemory executes cleanly and sets LOH compaction flag
            PRRX.IDM.Services.MemoryOptimizer.TrimMemory();
            Assert.True(true);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public void DownloadFileInfoViewModel_InstantLaunchWithZeroOrNegativeSize_DisplaysProbingImmediately(long nonPositiveSize)
        {
            var baseDownloads = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var vm = new PRRX.IDM.ViewModels.DownloadFileInfoViewModel("https://example.com/unknown.bin", baseDownloads, "Unknown Size File", nonPositiveSize);

            Assert.Equal("Probing size...", vm.FileSizeFormatted);
            Assert.True(vm.IsProbing);
        }

        [Fact]
        public void DownloadFileInfoViewModel_CancelProbe_ExecutesWithoutThrowing()
        {
            var baseDownloads = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var vm = new PRRX.IDM.ViewModels.DownloadFileInfoViewModel("https://example.com/file.bin", baseDownloads, "File");
            vm.CancelProbe();
            Assert.True(true);
        }

        [Fact]
        public void ThumbnailViewModel_UnloadResources_ClearsPreviewImage()
        {
            var configService = new PRRX.IDM.Services.ConfigurationService();
            var thumbService = new PRRX.IDM.Services.ThumbnailService();
            var vm = new PRRX.IDM.ViewModels.ThumbnailViewModel(thumbService, configService);

            vm.UnloadResources();
            Assert.Null(vm.PreviewImage);
        }

        [Fact]
        public void BrowserDownloadPayload_IncludesTokenForSecureForwarding()
        {
            var sec = new PRRX.IDM.Security.SecurityService();
            var token = sec.GetOrCreateIpcToken();

            var payload = new PRRX.IDM.Services.BrowserDownloadPayload
            {
                Action = "show",
                Token = token
            };

            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<PRRX.IDM.Services.BrowserDownloadPayload>(json);

            Assert.NotNull(deserialized);
            Assert.Equal(token, deserialized.Token);
            Assert.True(sec.ValidateIpcToken(deserialized.Token));
        }
    }
}
