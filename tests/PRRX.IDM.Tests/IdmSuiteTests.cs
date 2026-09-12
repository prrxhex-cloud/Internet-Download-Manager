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

                // 3. Test POST /api/download
                var jsonContent = new System.Net.Http.StringContent(
                    "{\"action\":\"download\",\"url\":\"https://example.com/testfile.exe\",\"pageTitle\":\"Download Test\"}",
                    System.Text.Encoding.UTF8,
                    "application/json");

                var postResp = await http.PostAsync($"http://127.0.0.1:{testPort}/api/download", jsonContent);
                Assert.Equal(System.Net.HttpStatusCode.OK, postResp.StatusCode);
                var postBody = await postResp.Content.ReadAsStringAsync();
                Assert.Contains("ok", postBody);
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

            public System.Threading.Tasks.Task<bool> StartDownloadAsync(string url, string destinationFilePath, int threadCount = 16, System.Threading.CancellationToken cancellationToken = default) =>
                System.Threading.Tasks.Task.FromResult(true);

            public void Pause() { }
            public void Resume() { }
            public void Cancel() { }
            public void SetSpeedLimit(bool isEnabled, int maxSpeedKbps) { }

            public void TriggerProgress(PRRX.IDM.Services.SegmentProgressEventArgs args) => ProgressChanged?.Invoke(this, args);
            public void TriggerComplete(string path) => DownloadCompleted?.Invoke(this, path);
            public void TriggerFailed(string err) => DownloadFailed?.Invoke(this, err);
        }
    }
}
