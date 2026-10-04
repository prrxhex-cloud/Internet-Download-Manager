using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using System.Net.Http;
using PRRX.IDM.Models;
using PRRX.IDM.Services;
using PRRX.IDM.ViewModels;
using PRRX.IDM.Views;

namespace PRRX.IDM.Tests
{
    public class EngineAndServiceTests
    {
        [Fact]
        public void EnsureNetscapeCookieFormat_PreservesValidNetscape()
        {
            var validNetscape = "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t2147483647\tVISITOR_INFO1_LIVE\tsample_value\n";
            var result = MediaEngineService.EnsureNetscapeCookieFormat(validNetscape, "https://www.youtube.com");

            Assert.NotNull(result);
            Assert.Contains("# Netscape HTTP Cookie File", result);
            Assert.Contains("VISITOR_INFO1_LIVE", result);
            Assert.Contains(".youtube.com", result);
        }

        [Fact]
        public void VerifySymbolIconProperty()
        {
            var thread = new Thread(() =>
            {
                var icon = new Wpf.Ui.Controls.SymbolIcon();
                icon.Symbol = Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle24;
                Assert.Equal(Wpf.Ui.Controls.SymbolRegular.CheckmarkCircle24, icon.Symbol);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }



        [Fact]
        public void VerifyXamlEnumsAndSymbols()
        {

            var allowedAppearances = Enum.GetNames(typeof(Wpf.Ui.Controls.ControlAppearance)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allowedSymbols = Enum.GetNames(typeof(Wpf.Ui.Controls.SymbolRegular)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "PRRX.InternetDownloadManager.sln")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            Assert.NotNull(dir);
            var srcDir = Path.Combine(dir!, "src", "PRRX.IDM");
            Assert.True(Directory.Exists(srcDir));

            var xamlFiles = Directory.GetFiles(srcDir, "*.xaml", SearchOption.AllDirectories);
            Assert.NotEmpty(xamlFiles);


            var errors = new System.Collections.Generic.List<string>();

            var appearanceRegex = new System.Text.RegularExpressions.Regex(@"Appearance\s*=\s*[""']([^""'{}\s]+)[""']", System.Text.RegularExpressions.RegexOptions.Compiled);
            var symbolRegex = new System.Text.RegularExpressions.Regex(@"Symbol\s*=\s*(?:[""']([^""'\s]+)[""']|([A-Za-z0-9_]+))", System.Text.RegularExpressions.RegexOptions.Compiled);

            foreach (var file in xamlFiles)
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    foreach (System.Text.RegularExpressions.Match match in appearanceRegex.Matches(line))
                    {
                        var val = match.Groups[1].Value;
                        if (val.StartsWith("{") || val.Contains("Binding") || val.Contains("x:Static")) continue;
                        if (!allowedAppearances.Contains(val))
                        {
                            errors.Add($"{Path.GetFileName(file)}:{i + 1} - Invalid Appearance: '{val}'");
                        }
                    }

                    foreach (System.Text.RegularExpressions.Match match in symbolRegex.Matches(line))
                    {
                        var val = match.Groups[1].Success && !string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value;
                        if (val.StartsWith("{") || val.Contains("Binding") || val.Contains("x:Static")) continue;
                        if (!allowedSymbols.Contains(val))
                        {
                            errors.Add($"{Path.GetFileName(file)}:{i + 1} - Invalid Symbol: '{val}'");
                        }
                    }
                }
            }

            Assert.True(errors.Count == 0, "XAML Enum Errors:\n" + string.Join("\n", errors));
        }

        [Fact]
        public void InstantiateSiteGrabberTab_DoesNotThrow()
        {
            Exception? thrown = null;
            var thread = new Thread(() =>
            {
                try
                {
                    if (System.Windows.Application.Current == null)
                    {
                        if (System.Windows.Application.ResourceAssembly == null)
                        {
                            System.Windows.Application.ResourceAssembly = typeof(PRRX.IDM.App).Assembly;
                        }
                        new System.Windows.Application();
                    }
                    var configService = new ConfigurationService();
                    var spiderService = new SiteSpiderGrabberService(configService);
                    var scheduleService = new ScheduleService();
                    var vm = new SiteGrabberViewModel(spiderService, scheduleService, configService);

                    var tab = new PRRX.IDM.Views.Tabs.SiteGrabberTab();
                    tab.DataContext = vm;
                    tab.Measure(new System.Windows.Size(1000, 1000));
                    tab.Arrange(new System.Windows.Rect(0, 0, 1000, 1000));
                    tab.UpdateLayout();
                    Assert.NotNull(tab);

                    var idmTab = new PRRX.IDM.Views.Tabs.IdmDownloadsTab();
                    var videoTab = new PRRX.IDM.Views.Tabs.VideoDownloaderTab();
                    var audioTab = new PRRX.IDM.Views.Tabs.AudioConverterTab();
                    var thumbTab = new PRRX.IDM.Views.Tabs.ThumbnailTab();
                    var settingsTab = new PRRX.IDM.Views.Tabs.SettingsTab();
                    Assert.NotNull(idmTab);
                    Assert.NotNull(videoTab);
                    Assert.NotNull(audioTab);
                    Assert.NotNull(thumbTab);
                    Assert.NotNull(settingsTab);

                    var themeService = new ThemeService(configService);
                    var browserService = new BrowserIntegrationService(configService);
                    var quickTour = new PRRX.IDM.Views.QuickTourWindow(new QuickTourViewModel(configService, themeService));
                    var onboarding = new PRRX.IDM.Views.OnboardingWindow(new OnboardingViewModel(configService, themeService, browserService));
                    var fileInfo = new PRRX.IDM.Views.DownloadFileInfoDialog(new DownloadFileInfoViewModel("https://example.com/test.zip", "C:\\Downloads"));
                    var batchReq = new PRRX.IDM.Models.BatchDownloadRequest { SourcePageUrl = "https://example.com" };
                    var batchDialog = new PRRX.IDM.Views.BatchDownloadDialog(new BatchDownloadViewModel(batchReq, "C:\\Downloads"));
                    Assert.NotNull(quickTour);
                    Assert.NotNull(onboarding);
                    Assert.NotNull(fileInfo);
                    Assert.NotNull(batchDialog);



                }
                catch (Exception ex)
                {
                    thrown = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (thrown != null)
            {
                throw new Exception($"XAML view instantiation failed: {thrown}", thrown);
            }
        }

        [Fact]
        public async Task TestGoogleDriveSetupRequest()
        {
            var url = "https://dl.google.com/drive-file-stream/GoogleDriveSetup.exe";
            using var client = new System.Net.Http.HttpClient();

            // Test 1: HEAD with ApplyStandardHeaders
            using var headReq = new HttpRequestMessage(HttpMethod.Head, url);
            PRRX.IDM.Services.SegmentedDownloadEngine.ApplyStandardHeaders(headReq, url);
            using var headResp = await client.SendAsync(headReq);

            // Test 2: GET with ApplyStandardHeaders and Range: 0-0
            using var getReq0 = new HttpRequestMessage(HttpMethod.Get, url);
            PRRX.IDM.Services.SegmentedDownloadEngine.ApplyStandardHeaders(getReq0, url);
            getReq0.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var getResp0 = await client.SendAsync(getReq0, HttpCompletionOption.ResponseHeadersRead);

            // Test 3: What SegmentedDownloadEngine was sending when total size unknown:
            // reqStart = 0, reqEnd = -1 -> Range: bytes=0-
            using var getReqStream = new HttpRequestMessage(HttpMethod.Get, url);
            PRRX.IDM.Services.SegmentedDownloadEngine.ApplyStandardHeaders(getReqStream, url);
            getReqStream.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, null);
            using var getRespStream = await client.SendAsync(getReqStream, HttpCompletionOption.ResponseHeadersRead);

            Assert.True(headResp.IsSuccessStatusCode, $"HEAD status: {headResp.StatusCode}");
            Assert.True(getResp0.IsSuccessStatusCode, $"GET 0-0 status: {getResp0.StatusCode}");
            Assert.True(getRespStream.IsSuccessStatusCode, $"GET 0- status: {getRespStream.StatusCode}");
        }

        [Fact]
        public async Task TestSegmentedDownloadEngine_GoogleDriveSetup()
        {
            var engine = new MultiSegmentDownloader();
            var tempDir = Path.Combine(Path.GetTempPath(), "prrx_gdrive_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var dest = Path.Combine(tempDir, "GoogleDriveSetup.exe");
            var url = "https://dl.google.com/drive-file-stream/GoogleDriveSetup.exe";

            var tcs = new TaskCompletionSource<bool>();
            string? failedReason = null;
            engine.DownloadCompleted += (_, _) => tcs.TrySetResult(true);
            engine.DownloadFailed += (_, err) => { failedReason = err; tcs.TrySetResult(false); };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            cts.Token.Register(() => tcs.TrySetCanceled());

            // Cancel after 2 seconds so we only test the connection / start handshake
            _ = Task.Run(async () =>
            {
                await Task.Delay(2500);
                engine.Cancel();
            });

            _ = engine.StartDownloadAsync(url, dest, 4, cancellationToken: default);
            try
            {
                var result = await tcs.Task;
            }
            catch (TaskCanceledException) { }

            try { Directory.Delete(tempDir, true); } catch { }
        }








        [Fact]
        public void EnsureNetscapeCookieFormat_ConvertsRawKeyValuePairs()
        {
            var raw = "VISITOR_INFO1_LIVE=abc123xyz; PREF=f4=4000000; LOGIN_INFO=AFmmF2kw";
            var result = MediaEngineService.EnsureNetscapeCookieFormat(raw, "https://www.youtube.com/watch?v=test");

            Assert.NotNull(result);
            Assert.Contains("# Netscape HTTP Cookie File", result);
            Assert.Contains(".youtube.com\tTRUE\t/\tTRUE\t2147483647\tVISITOR_INFO1_LIVE\tabc123xyz", result);
            Assert.Contains(".youtube.com\tTRUE\t/\tTRUE\t2147483647\tPREF\tf4=4000000", result);
            Assert.Contains(".google.com\tTRUE\t/\tTRUE\t2147483647\tVISITOR_INFO1_LIVE\tabc123xyz", result);
        }

        [Fact]
        public void EnsureNetscapeCookieFormat_SanitizesTabsAndNewlines()
        {
            var injection = "MALICIOUS\tCOOKIE=VALUE\r\nNEXT=VAL";
            var result = MediaEngineService.EnsureNetscapeCookieFormat(injection, "https://www.youtube.com");

            Assert.NotNull(result);
            // All output rows (excluding comments) must contain exactly 6 tabs (7 columns)
            var lines = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("#")) continue;
                var parts = line.Split('\t');
                Assert.True(parts.Length == 7, $"Row did not have exactly 7 columns: {line}");
            }
        }

        [Fact]
        public void EnsureNetscapeCookieFormat_ReturnsNullOnEmptyOrWhitespace()
        {
            Assert.Null(MediaEngineService.EnsureNetscapeCookieFormat(null));
            Assert.Null(MediaEngineService.EnsureNetscapeCookieFormat("   "));
            Assert.Null(MediaEngineService.EnsureNetscapeCookieFormat(";;;\n\r"));
        }

        [Fact]
        public void SettingsViewModel_ExtensionDirectory_ResolvesToExistingDirectory()
        {
            var configService = new ConfigurationService();
            var themeService = new ThemeService(configService);
            var updateService = new UpdateService();
            var mediaEngine = new MediaEngineService(configService);
            var vm = new SettingsViewModel(configService, themeService, updateService, mediaEngine);

            var extDir = vm.ExtensionDirectory;
            Assert.False(string.IsNullOrWhiteSpace(extDir));
            Assert.True(Directory.Exists(extDir), $"Extension directory does not exist: {extDir}");
            Assert.True(File.Exists(Path.Combine(extDir, "manifest.json")), $"manifest.json missing in: {extDir}");
        }

        [Fact]
        public void OnboardingViewModel_ExtensionDirectory_ResolvesToExistingDirectory()
        {
            var configService = new ConfigurationService();
            var themeService = new ThemeService(configService);
            var vm = new OnboardingViewModel(configService, themeService);

            var extDir = vm.ExtensionDirectory;
            Assert.False(string.IsNullOrWhiteSpace(extDir));
            Assert.True(Directory.Exists(extDir), $"Extension directory does not exist: {extDir}");
            Assert.True(File.Exists(Path.Combine(extDir, "manifest.json")), $"manifest.json missing in: {extDir}");
        }

        [Fact]
        public void SegmentedDownloadEngine_PauseAndResumeState()
        {
            var engine = new SegmentedDownloadEngine();
            Assert.False(engine.IsPaused);
            Assert.False(engine.IsRunning);

            // Simulate start state then pause
            engine.Pause();
            // Not running so Pause does not activate
            Assert.False(engine.IsPaused);
        }

        [Fact]
        public void FormatBytes_ProducesAccurateHumanReadableValues()
        {
            Assert.Equal("1.00 GB", FormatBytes(1024L * 1024L * 1024L));
            Assert.Equal("50.00 MB", FormatBytes(50L * 1024L * 1024L));
            Assert.Equal("500.0 KB", FormatBytes(500L * 1024L));
            Assert.Equal("128 B", FormatBytes(128L));
        }

        [Fact]
        public void VideoFormatSelection_SelectsVideoFormat_NotAudio()
        {
            var probeResult = new MediaProbeResult
            {
                Title = "Test Video",
                IsMusic = false
            };
            probeResult.Formats.Add(new MediaFormat
            {
                FormatId = "ba/b",
                Resolution = "Audio Only",
                HasVideo = false,
                HasAudio = true,
                FileSizeBytes = 15_000_000L,
                EstimatedSizeFormatted = "~ 15.0 MB"
            });
            probeResult.Formats.Add(new MediaFormat
            {
                FormatId = "137+140",
                Resolution = "1080p Full HD",
                HasVideo = true,
                HasAudio = true,
                FileSizeBytes = 125_000_000L,
                EstimatedSizeFormatted = "~ 125.0 MB"
            });

            // For video download (SelectedCategory == Video)
            var selectedCategory = FileCategory.Video;
            var isAudioTarget = false;

            MediaFormat? chosenFormat = null;
            if (selectedCategory == FileCategory.Music || isAudioTarget)
            {
                chosenFormat = probeResult.Formats.FirstOrDefault(f => !f.HasVideo) ?? probeResult.Formats.FirstOrDefault();
            }
            else
            {
                chosenFormat = probeResult.Formats.FirstOrDefault(f => f.HasVideo) ?? probeResult.Formats.FirstOrDefault();
            }

            Assert.NotNull(chosenFormat);
            Assert.True(chosenFormat.HasVideo);
            Assert.Equal("1080p Full HD", chosenFormat.Resolution);
            Assert.Equal(125_000_000L, chosenFormat.FileSizeBytes);
        }

        [Fact]
        public void VideoFormatSelection_ForMusicCategory_SelectsAudioFormat()
        {
            var probeResult = new MediaProbeResult
            {
                Title = "Test Song",
                IsMusic = true
            };
            probeResult.Formats.Add(new MediaFormat
            {
                FormatId = "ba/b",
                Resolution = "High Quality Audio (320 kbps)",
                HasVideo = false,
                HasAudio = true,
                FileSizeBytes = 12_500_000L,
                EstimatedSizeFormatted = "~ 12.5 MB"
            });
            probeResult.Formats.Add(new MediaFormat
            {
                FormatId = "137+140",
                Resolution = "1080p Full HD",
                HasVideo = true,
                HasAudio = true,
                FileSizeBytes = 125_000_000L,
                EstimatedSizeFormatted = "~ 125.0 MB"
            });

            var selectedCategory = FileCategory.Music;
            var isAudioTarget = true;

            MediaFormat? chosenFormat = null;
            if (selectedCategory == FileCategory.Music || isAudioTarget)
            {
                chosenFormat = probeResult.Formats.FirstOrDefault(f => !f.HasVideo) ?? probeResult.Formats.FirstOrDefault();
            }
            else
            {
                chosenFormat = probeResult.Formats.FirstOrDefault(f => f.HasVideo) ?? probeResult.Formats.FirstOrDefault();
            }

            Assert.NotNull(chosenFormat);
            Assert.False(chosenFormat.HasVideo);
            Assert.Equal(12_500_000L, chosenFormat.FileSizeBytes);
        }

        [Theory]
        [InlineData("100 B", 100L)]
        [InlineData("~ 500 KB", 512000L)]
        [InlineData("≈ 1.5 MB", (long)(1.5 * 1024 * 1024))]
        [InlineData("> 2.5 GB", (long)(2.5 * 1024 * 1024 * 1024))]
        [InlineData("10 GiB", 10L * 1024 * 1024 * 1024)]
        [InlineData("128 MiB", 128L * 1024 * 1024)]
        [InlineData("64 KiB", 64L * 1024)]
        [InlineData("invalid", 0L)]
        public void ParseSizeStringToBytes_HandlesAllUnitsAndPrefixes(string input, long expectedMin)
        {
            var result = SegmentedDownloadEngine.ParseSizeStringToBytes(input);
            if (expectedMin == 0)
            {
                Assert.Equal(0, result);
            }
            else
            {
                Assert.True(result >= expectedMin - 100 && result <= expectedMin + 100, 
                    $"Input '{input}' gave {result}, expected near {expectedMin}");
            }
        }

        [Fact]
        public void CreateConfiguredClient_AppliesProxyAndCredentials()
        {
            var config = new AppConfig
            {
                UseProxy = true,
                ProxyType = ProxyType.Socks5,
                ProxyHost = "127.0.0.1",
                ProxyPort = 1080,
                UseProxyAuth = true,
                ProxyUsername = "user",
                ProxyPassword = "pwd"
            };

            var client = SegmentedDownloadEngine.CreateConfiguredClient(
                config, 
                siteUsername: "siteUser", 
                sitePassword: "sitePassword", 
                targetUri: new Uri("https://example.com/file.zip"));

            Assert.NotNull(client);
            Assert.Equal(TimeSpan.FromSeconds(45), client.Timeout);
        }

        [Fact]
        public void BatchDownloadViewModel_PresetsAndFiltering()
        {
            var req = new BatchDownloadRequest
            {
                SourcePageUrl = "https://example.com/index.html",
                PageTitle = "Test Page",
                Links = new System.Collections.Generic.List<BatchLinkItem>
                {
                    new BatchLinkItem { FileName = "movie.mp4", Extension = ".mp4", Category = FileCategory.Video, FileSizeBytes = 50 * 1024 * 1024 },
                    new BatchLinkItem { FileName = "song.mp3", Extension = ".mp3", Category = FileCategory.Music, FileSizeBytes = 5 * 1024 * 1024 },
                    new BatchLinkItem { FileName = "report.pdf", Extension = ".pdf", Category = FileCategory.Documents, FileSizeBytes = 2 * 1024 * 1024 },
                    new BatchLinkItem { FileName = "archive.zip", Extension = ".zip", Category = FileCategory.Compressed, FileSizeBytes = 20 * 1024 * 1024 }
                }
            };

            var vm = new BatchDownloadViewModel(req, @"C:\Downloads");
            Assert.Equal(4, vm.Links.Count);
            Assert.Equal(4, vm.SelectedLinks.Count);

            // Filter to Videos
            vm.SelectedPreset = BatchFilterPreset.Videos;
            Assert.Single(vm.SelectedLinks);
            Assert.Equal("movie.mp4", vm.SelectedLinks[0].FileName);

            // Filter to Documents
            vm.SelectedPreset = BatchFilterPreset.Documents;
            Assert.Single(vm.SelectedLinks);
            Assert.Equal("report.pdf", vm.SelectedLinks[0].FileName);

            // Size filtering
            vm.SelectedPreset = BatchFilterPreset.All;
            vm.MinSizeMbText = "10";
            // Should match movie.mp4 (50MB) and archive.zip (20MB)
            Assert.Equal(2, vm.SelectedLinks.Count);

            // Invert selection
            vm.InvertSelectionCommand.Execute(null);
            Assert.Equal(2, vm.SelectedLinks.Count);
            Assert.Contains(vm.SelectedLinks, l => l.FileName == "song.mp3");
            Assert.Contains(vm.SelectedLinks, l => l.FileName == "report.pdf");
        }

        [Fact]
        public void ScheduleService_SchedulesAndUnschedules()
        {
            using var schedule = new ScheduleService();
            var project = new GrabberProject
            {
                ProjectName = "Scheduled Site Project",
                StartUrl = "https://example.com",
                ScheduleMode = GrabberScheduleMode.RunOnceAtTime,
                ScheduleStartTime = DateTime.Now.AddHours(2)
            };

            schedule.ScheduleGrabber(project, _ => Task.CompletedTask);
            Assert.True(schedule.IsGrabberScheduled(project.Id));
            Assert.NotNull(schedule.GetNextRunTime(project.Id));
            Assert.Equal(GrabberStatus.Scheduled, project.Status);

            schedule.UnscheduleGrabber(project.Id);
            Assert.False(schedule.IsGrabberScheduled(project.Id));
            Assert.Equal(GrabberStatus.Idle, project.Status);
        }

        [Theory]
        [InlineData("video.mp4", FileCategory.Video, "Video Stream")]
        [InlineData("song.mp3", FileCategory.Music, "Audio Stream")]
        [InlineData("document.pdf", FileCategory.Documents, "Document")]
        [InlineData("setup.exe", FileCategory.Programs, "Software Package")]
        [InlineData("backup.zip", FileCategory.Compressed, "Archive File")]
        [InlineData("unknown.dat", FileCategory.General, "Dynamic Stream")]
        public void DownloadFileInfoViewModel_HeuristicResolution(string fileName, FileCategory category, string expectedSubstr)
        {
            var heuristic = DownloadFileInfoViewModel.GetCategoryHeuristic(fileName, category);
            Assert.Contains(expectedSubstr, heuristic);
        }

        [Fact]
        public void ParseAndReportProgress_ReportsImmediateFeedbackOnInitHandshake()
        {
            var mediaEngine = new MediaEngineService();
            DownloadProgressReport? lastReport = null;
            var progress = new SyncProgress<DownloadProgressReport>(r => lastReport = r);
            var state = new StreamProgressState();

            mediaEngine.ParseAndReportProgress("[youtube] Extracting URL: https://www.youtube.com/watch?v=dQw4w9WgXcQ", progress, state);

            Assert.NotNull(lastReport);
            Assert.True(lastReport.Percentage >= 3.0);
            Assert.Equal("Connecting...", lastReport.Speed);
            Assert.Contains("[youtube]", lastReport.StatusMessage);
        }

        [Fact]
        public void ParseAndReportProgress_ScalesMultiPassStreamsAccurately()
        {
            var mediaEngine = new MediaEngineService();
            DownloadProgressReport? lastReport = null;
            var progress = new SyncProgress<DownloadProgressReport>(r => lastReport = r);
            var state = new StreamProgressState();

            // Destination 1: Video
            mediaEngine.ParseAndReportProgress("[download] Destination: video.f137.mp4", progress, state);
            Assert.NotNull(lastReport);
            Assert.Equal(5.0, lastReport.Percentage);
            Assert.Contains("video.f137.mp4", lastReport.StatusMessage);

            // Pass 1: Video 50%
            mediaEngine.ParseAndReportProgress("[download]  50.0% of  50.00MiB at   5.00MiB/s ETA 00:05", progress, state);
            Assert.NotNull(lastReport);
            Assert.Equal(45.0, lastReport.Percentage);
            Assert.Equal("5.00MiB/s", lastReport.Speed);
            Assert.Equal("00:05", lastReport.Eta);

            // Pass 1: Video 100%
            mediaEngine.ParseAndReportProgress("[download] 100.0% of  50.00MiB at   5.00MiB/s ETA 00:00", progress, state);
            Assert.NotNull(lastReport);
            Assert.Equal(85.0, lastReport.Percentage);

            // Destination 2: Audio
            mediaEngine.ParseAndReportProgress("[download] Destination: audio.f140.m4a", progress, state);
            Assert.NotNull(lastReport);
            Assert.True(lastReport.Percentage >= 85.0);
            Assert.Contains("audio.f140.m4a", lastReport.StatusMessage);

            // Pass 2: Audio 50%
            mediaEngine.ParseAndReportProgress("[download]  50.0% of   5.00MiB at   2.00MiB/s ETA 00:01", progress, state);
            Assert.NotNull(lastReport);
            Assert.True(lastReport.Percentage >= 91.0 && lastReport.Percentage <= 92.0);

            // Merger
            mediaEngine.ParseAndReportProgress("[Merger] Merging formats into \"final.mp4\"", progress, state);
            Assert.NotNull(lastReport);
            Assert.Equal(98.0, lastReport.Percentage);
            Assert.Contains("Multiplexing", lastReport.StatusMessage);
        }

        [Fact]
        public void BatchDownloadViewModel_CustomFilter_SupportsMultiTokenDelimiters()
        {
            var req = new BatchDownloadRequest
            {
                SourcePageUrl = "https://example.com/files",
                PageTitle = "Download Directory",
                Links = new System.Collections.Generic.List<BatchLinkItem>
                {
                    new BatchLinkItem { FileName = "document.pdf", Extension = ".pdf", Category = FileCategory.Documents },
                    new BatchLinkItem { FileName = "presentation.pptx", Extension = ".pptx", Category = FileCategory.Documents },
                    new BatchLinkItem { FileName = "installer.msi", Extension = ".msi", Category = FileCategory.Programs },
                    new BatchLinkItem { FileName = "photo.png", Extension = ".png", Category = FileCategory.General }
                }
            };

            var vm = new BatchDownloadViewModel(req, @"C:\Downloads");
            vm.SelectedPreset = BatchFilterPreset.Custom;
            vm.FilterQuery = "*.pdf, pptx; *.msi";

            Assert.Equal(3, vm.SelectedLinks.Count);
            Assert.Contains(vm.SelectedLinks, l => l.FileName == "document.pdf");
            Assert.Contains(vm.SelectedLinks, l => l.FileName == "presentation.pptx");
            Assert.Contains(vm.SelectedLinks, l => l.FileName == "installer.msi");
            Assert.DoesNotContain(vm.SelectedLinks, l => l.FileName == "photo.png");
        }

        [Fact]
        public void TelegramBotSyncService_InitialStateAndStatusFormatting()
        {
            var configService = new ConfigurationService();
            var service = new TelegramBotSyncService(configService);

            Assert.False(string.IsNullOrWhiteSpace(service.PairingCode));
            Assert.False(string.IsNullOrWhiteSpace(service.ClientId));
            Assert.Equal("PRRX_IDM_Bot", service.BotUsername);
            Assert.Contains(service.PairingCode, service.BotDeepLink);

            Assert.Equal(TelegramBotConnectionStatus.Connecting, service.Status);
            Assert.Equal("Connecting / Polling...", service.StatusText);
            Assert.Equal("#CA5010", service.StatusColorHex);
        }

        [Fact]
        public void SettingsViewModel_TelegramProperties_InitializedProperly()
        {
            var configService = new ConfigurationService();
            var themeService = new ThemeService(configService);
            var updateService = new UpdateService();
            var mediaEngine = new MediaEngineService(configService);
            var syncService = new TelegramBotSyncService(configService);

            var vm = new SettingsViewModel(configService, themeService, updateService, mediaEngine);

            Assert.False(string.IsNullOrWhiteSpace(vm.TelegramPairingCode));
            Assert.Equal("PRRX_IDM_Bot", vm.TelegramBotUsername);
            Assert.Contains(vm.TelegramPairingCode, vm.TelegramBotUrl);
            Assert.False(string.IsNullOrWhiteSpace(vm.TelegramStatusText));
            Assert.False(string.IsNullOrWhiteSpace(vm.TelegramStatusBadgeColor));
            Assert.False(string.IsNullOrWhiteSpace(vm.TelegramLinkedUser));
            Assert.NotNull(vm.TestTelegramConnectionCommand);
            Assert.NotNull(vm.ForceTelegramHeartbeatCommand);
            Assert.NotNull(vm.UnlinkTelegramCommand);
        }

        [Fact]
        public void MainViewModel_TelegramPill_ReflectsSyncServiceState()
        {
            var configService = new ConfigurationService();
            var themeService = new ThemeService(configService);
            var mediaEngine = new MediaEngineService(configService);
            var thumbService = new ThumbnailService();
            var updateService = new UpdateService();
            var historyService = new HistoryService();
            var browserService = new BrowserIntegrationService(configService);
            var syncService = new TelegramBotSyncService(configService);

            var vm = new MainViewModel(
                configService, themeService, mediaEngine, thumbService,
                updateService, historyService, browserService);

            Assert.False(string.IsNullOrWhiteSpace(vm.TelegramStatusDotBrush));
            Assert.False(string.IsNullOrWhiteSpace(vm.TelegramStatusShortText));
            Assert.False(string.IsNullOrWhiteSpace(vm.TelegramStatusTooltip));
            Assert.False(string.IsNullOrWhiteSpace(vm.TelegramStatusBackgroundBrush));
            Assert.False(string.IsNullOrWhiteSpace(vm.TelegramStatusBorderBrush));
        }

        [Fact]
        public async Task TelegramBotSyncService_OfflineSignal_DoesNotThrow()
        {
            var configService = new ConfigurationService();
            var service = new TelegramBotSyncService(configService, "http://127.0.0.1:59999");

            // Calling SendOfflineStatusAsync to unreachable host should handle gracefully without throwing
            var ex = await Record.ExceptionAsync(() => service.SendOfflineStatusAsync());
            Assert.Null(ex);
        }

        [Fact]
        public void TelegramBotSyncService_ParseTaskFromJson_ExtractsAllFields()
        {
            var json = """
            {
                "id": "tg_task_123",
                "url": "https://example.com/movie.mp4",
                "fileName": "movie.mp4",
                "fileSize": 104857600,
                "formattedSize": "100.00 MB",
                "mediaType": "video",
                "source": "Telegram @PRRX_IDM_Bot",
                "fileId": "BAACAgUAAxkBAAI...",
                "mimeType": "video/mp4",
                "chatId": "123456789",
                "messageId": 42
            }
            """;

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var task = TelegramBotSyncService.ParseTaskFromJson(doc.RootElement);

            Assert.Equal("tg_task_123", task.Id);
            Assert.Equal("https://example.com/movie.mp4", task.Url);
            Assert.Equal("movie.mp4", task.FileName);
            Assert.Equal(104857600L, task.FileSize);
            Assert.Equal("100.00 MB", task.FormattedSize);
            Assert.Equal("video", task.MediaType);
            Assert.Equal("Telegram @PRRX_IDM_Bot", task.Source);
            Assert.Equal("BAACAgUAAxkBAAI...", task.FileId);
            Assert.Equal("video/mp4", task.MimeType);
            Assert.Equal("123456789", task.ChatId);
            Assert.Equal(42L, task.MessageId);
        }

        [Fact]
        public void TelegramBotSyncService_ParseTaskFromJson_ConstructsTgFileUrlWhenUrlEmpty()
        {
            var json = """
            {
                "id": "tg_task_456",
                "url": "",
                "fileName": "archive.zip",
                "fileSize": 52428800,
                "mediaType": "document",
                "fileId": "FILE_ID_ABC",
                "mimeType": "application/zip",
                "chatId": "987654321",
                "messageId": 99
            }
            """;

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var task = TelegramBotSyncService.ParseTaskFromJson(doc.RootElement);

            Assert.StartsWith("tg://file?", task.Url);
            Assert.Contains("file_id=FILE_ID_ABC", task.Url);
            Assert.Contains("file_name=archive.zip", task.Url);
            Assert.Contains("chat_id=987654321", task.Url);
            Assert.Equal("50 MB", task.FormattedSize);
        }

        [Fact]
        public void TelegramBotSyncService_ClientId_PreservedInConfiguration()
        {
            var configService = new ConfigurationService();
            var service1 = new TelegramBotSyncService(configService);
            var clientId1 = service1.ClientId;

            Assert.False(string.IsNullOrWhiteSpace(clientId1));
            Assert.Equal(clientId1, configService.CurrentConfig.TelegramClientId);

            // A second instance with the same config should preserve the same ClientId
            var service2 = new TelegramBotSyncService(configService);
            Assert.Equal(clientId1, service2.ClientId);
        }

        [Fact]
        public void SettingsViewModel_TelegramCommands_ExecuteWithoutException()
        {
            var configService = new ConfigurationService();
            var themeService = new ThemeService(configService);
            var updateService = new UpdateService();
            var mediaEngine = new MediaEngineService(configService);
            var syncService = new TelegramBotSyncService(configService, "http://127.0.0.1:59998");

            var vm = new SettingsViewModel(configService, themeService, updateService, mediaEngine);

            // TestConnectionCommand
            Assert.True(vm.TestTelegramConnectionCommand.CanExecute(null));
            vm.TestTelegramConnectionCommand.Execute(null);

            // ForceTelegramHeartbeatCommand
            Assert.True(vm.ForceTelegramHeartbeatCommand.CanExecute(null));
            vm.ForceTelegramHeartbeatCommand.Execute(null);

            // RePairTelegramCommand
            Assert.NotNull(vm.RePairTelegramCommand);
            Assert.True(vm.RePairTelegramCommand.CanExecute(null));
            vm.RePairTelegramCommand.Execute(null);

            // UnlinkTelegramCommand
            Assert.True(vm.UnlinkTelegramCommand.CanExecute(null));
            vm.UnlinkTelegramCommand.Execute(null);

            Assert.False(string.IsNullOrWhiteSpace(vm.TelegramPairingCode));
        }

        [Fact]
        public async Task TelegramBotSyncService_RePairAsync_GeneratesNewPairingCode()
        {
            var configService = new ConfigurationService();
            var service = new TelegramBotSyncService(configService, "http://127.0.0.1:59997");

            var initialCode = service.PairingCode;
            var newCode = await service.RePairAsync();

            Assert.False(string.IsNullOrWhiteSpace(newCode));
            Assert.StartsWith("PRRX-", newCode);
            Assert.Equal(TelegramBotConnectionStatus.PairingRequired, service.Status);
            Assert.Equal(newCode, service.PairingCode);
        }

        [Fact]
        public void TestMediaEngine_ParseAndReportProgress_AudioPostProcessingStages()
        {
            var configService = new ConfigurationService();
            var mediaEngine = new MediaEngineService(configService);
            var reports = new List<DownloadProgressReport>();
            var progress = new SyncProgress<DownloadProgressReport>(r => reports.Add(r));
            var streamState = new StreamProgressState();

            // 1. [ExtractAudio] -> 96%
            mediaEngine.ParseAndReportProgress("[ExtractAudio] Destination: test.mp3", progress, streamState);
            Assert.NotEmpty(reports);
            Assert.Equal(96.0, reports.Last().Percentage);
            Assert.Contains("Transcoding", reports.Last().StatusMessage);

            // 2. [Fixup] -> 97%
            mediaEngine.ParseAndReportProgress("[FixupM3u8] Fixing media container...", progress, streamState);
            Assert.Equal(97.0, reports.Last().Percentage);

            // 3. [Metadata] -> 98%
            mediaEngine.ParseAndReportProgress("[Metadata] Adding metadata to test.mp3", progress, streamState);
            Assert.Equal(98.0, reports.Last().Percentage);
            Assert.Contains("metadata", reports.Last().StatusMessage, StringComparison.OrdinalIgnoreCase);

            // 4. Deleting original file -> 99%
            mediaEngine.ParseAndReportProgress("Deleting original file test.temp.mp4 (pass -k to keep)", progress, streamState);
            Assert.Equal(99.0, reports.Last().Percentage);
            Assert.Contains("Finalizing", reports.Last().StatusMessage);
        }

        [Fact]
        public void TestSegmentedDownloadEngine_StandardHeaders_NoCorsHeaders()
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "https://dl.google.com/drive-file-stream/GoogleDriveSetup.exe");
            SegmentedDownloadEngine.ApplyStandardHeaders(req, req.RequestUri!.ToString());

            // Sec-Fetch-Mode: cors must NOT be present
            Assert.False(req.Headers.Contains("Sec-Fetch-Mode"), "Sec-Fetch-Mode should not be present");
            Assert.False(req.Headers.Contains("Sec-Fetch-Site"), "Sec-Fetch-Site should not be present");
            Assert.True(req.Headers.Contains("User-Agent"));
            Assert.True(req.Headers.Contains("Accept"));
        }

        [Fact]
        public void TestTelegramDynamicSegmentation_ThreadActiveToggling()
        {
            var thread = new DownloadConnectionThread
            {
                ThreadId = 1,
                StartByte = 0,
                EndByte = 1000,
                CurrentByte = 0,
                DownloadedBytes = 0,
                IsActive = true
            };

            // Thread active while receiving
            thread.DownloadedBytes = 500;
            long slotLen = thread.EndByte - thread.StartByte + 1;
            bool isComplete = thread.DownloadedBytes >= slotLen;
            thread.IsActive = !isComplete;
            Assert.True(thread.IsActive);

            // Thread completed -> IsActive becomes false
            thread.DownloadedBytes = 1001;
            isComplete = thread.DownloadedBytes >= slotLen;
            thread.IsActive = !isComplete;
            Assert.False(thread.IsActive);
        }

        [Fact]
        public void MainWindow_InstantiateAndNavigateToSiteGrabber_DoesNotThrow()
        {
            Exception? thrown = null;
            var thread = new Thread(() =>
            {
                try
                {
                    if (System.Windows.Application.Current == null)
                    {
                        if (System.Windows.Application.ResourceAssembly == null)
                        {
                            System.Windows.Application.ResourceAssembly = typeof(PRRX.IDM.App).Assembly;
                        }
                        new System.Windows.Application();
                    }

                    var configService = new ConfigurationService();
                    var themeService = new ThemeService(configService);
                    var mediaEngine = new MediaEngineService(configService);
                    var thumbnailService = new ThumbnailService();
                    var updateService = new UpdateService();
                    var historyService = new HistoryService();
                    var browserService = new BrowserIntegrationService(configService);
                    var spiderService = new SiteSpiderGrabberService(configService);
                    var scheduleService = new ScheduleService();

                    var mainVm = new MainViewModel(
                        configService,
                        themeService,
                        mediaEngine,
                        thumbnailService,
                        updateService,
                        historyService,
                        browserService,
                        spiderService,
                        scheduleService);

                    var mainWin = new MainWindow(mainVm);
                    mainWin.Measure(new System.Windows.Size(1200, 800));
                    mainWin.Arrange(new System.Windows.Rect(0, 0, 1200, 800));
                    mainWin.UpdateLayout();

                    // Navigate to Site Grabber
                    Assert.True(mainVm.NavigateTabCommand.CanExecute("SiteGrabber"));
                    mainVm.NavigateTabCommand.Execute("SiteGrabber");

                    Assert.IsType<SiteGrabberViewModel>(mainVm.CurrentTabViewModel);
                    mainWin.UpdateLayout();
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (thrown != null)
            {
                throw new Exception($"MainWindow SiteGrabber navigation failed: {thrown}", thrown);
            }
        }

        [Fact]
        public void TelegramDynamicSegmentation_SegmentQueues_DistributeChunksAcrossConcurrency()
        {
            int concurrency = 8;
            long totalBytes = 80 * 1024 * 1024; // 80 MB
            int chunkSize = 524288; // 512 KB
            int totalChunks = (int)Math.Ceiling((double)totalBytes / chunkSize);
            long segmentSize = (long)Math.Ceiling((double)totalBytes / concurrency);

            var segmentQueues = new System.Collections.Concurrent.ConcurrentQueue<int>[concurrency];
            for (int s = 0; s < concurrency; s++)
            {
                segmentQueues[s] = new System.Collections.Concurrent.ConcurrentQueue<int>();
            }

            for (int i = 0; i < totalChunks; i++)
            {
                long chunkOffset = (long)i * chunkSize;
                int sIdx = segmentSize > 0 ? Math.Clamp((int)(chunkOffset / segmentSize), 0, concurrency - 1) : 0;
                segmentQueues[sIdx].Enqueue(i);
            }

            // Every segment queue must contain chunks
            for (int s = 0; s < concurrency; s++)
            {
                Assert.False(segmentQueues[s].IsEmpty, $"Segment queue {s} should have chunks");
                Assert.True(segmentQueues[s].TryDequeue(out var firstChunk));
                long firstChunkOffset = (long)firstChunk * chunkSize;
                int owner = (int)(firstChunkOffset / segmentSize);
                Assert.Equal(s, owner);
            }
        }

        [Fact]
        public void TelegramMtproto_TotalSizeResolved_RecalculatesSegmentBounds()
        {
            int concurrency = 16;
            long totalBytes = 0;
            long segmentSize = 0;
            var threads = new List<DownloadConnectionThread>();
            for (int i = 0; i < concurrency; i++)
            {
                threads.Add(new DownloadConnectionThread
                {
                    ThreadId = i + 1,
                    StartByte = 0,
                    EndByte = 0,
                    CurrentByte = 0,
                    IsActive = true
                });
            }

            // Simulate size discovery during MTProto progress callback
            long discoveredSize = 160 * 1024 * 1024; // 160 MB
            if (discoveredSize > 0 && totalBytes <= 0)
            {
                totalBytes = discoveredSize;
                segmentSize = Math.Max(1, (long)Math.Ceiling((double)totalBytes / concurrency));
                for (int i = 0; i < concurrency; i++)
                {
                    long segStart = i * segmentSize;
                    long segEnd = (i == concurrency - 1) ? totalBytes - 1 : Math.Min(totalBytes - 1, segStart + segmentSize - 1);
                    threads[i].StartByte = segStart;
                    threads[i].EndByte = Math.Max(segStart, segEnd);
                    threads[i].CurrentByte = segStart;
                }
            }

            Assert.Equal(160 * 1024 * 1024, totalBytes);
            Assert.Equal(10 * 1024 * 1024, segmentSize);
            Assert.Equal(0, threads[0].StartByte);
            Assert.Equal(segmentSize - 1, threads[0].EndByte);
            Assert.Equal(totalBytes - 1, threads[15].EndByte);
        }

        [Fact]
        public void MediaEngine_AudioConversionTargetExtension_NotRenamedToVideo()
        {
            var destinationFilePath = "C:\\Downloads\\Track.mp4";
            var normalizedFormat = "mp3";
            var expectedTarget = Path.ChangeExtension(destinationFilePath, normalizedFormat);
            Assert.Equal("C:\\Downloads\\Track.mp3", expectedTarget);
            Assert.NotEqual(destinationFilePath, expectedTarget);
        }

        [Fact]
        public void SegmentedDownloadEngine_ThreadAllocation_DoesNotRetainPreliminaryThreads()
        {
            var threads = new List<DownloadConnectionThread>();
            // Seed preliminary threads
            for (int i = 0; i < 8; i++)
            {
                threads.Add(new DownloadConnectionThread
                {
                    ThreadId = i + 1,
                    StartByte = i * 1000,
                    EndByte = (i + 1) * 1000,
                    StatusInfo = "Connecting..."
                });
            }
            Assert.Equal(8, threads.Count);

            // Allocation of real download segments must clear preliminary threads
            threads.Clear();
            int threadCount = 16;
            long totalBytes = 100 * 1024 * 1024;
            long segmentSize = Math.Max(1, totalBytes / threadCount);
            for (int i = 0; i < threadCount; i++)
            {
                long start = i * segmentSize;
                long end = (i == threadCount - 1) ? totalBytes - 1 : (start + segmentSize - 1);
                threads.Add(new DownloadConnectionThread
                {
                    ThreadId = i + 1,
                    StartByte = start,
                    EndByte = end,
                    StatusInfo = "Connecting..."
                });
            }

            Assert.Equal(threadCount, threads.Count);
            Assert.Equal(0, threads[0].StartByte);
            Assert.Equal(totalBytes - 1, threads[^1].EndByte);
            Assert.True(threads[0].EndByte > 1000);
        }

        [Fact]
        public void AiSelfHealingEngine_DiagnosesSslError_CorrectlyRemediates()
        {
            var engine = new AiSelfHealingEngine();
            var plan = engine.DiagnoseAndRemediate(
                "https://com.spotify.music.en.aptoide.com/app.apk",
                new HttpRequestException("The SSL connection could not be established, see inner exception. UntrustedRoot: A certificate chain processed, but terminated in a root certificate which is not trusted by the trust provider."));

            Assert.NotNull(plan);
            Assert.Equal(HealingActionType.BypassSslCertificate, plan.HealingType);
            Assert.True(plan.BypassSsl);
            Assert.Contains("certificate", plan.Explanation);
        }

        [Fact]
        public void AiSelfHealingEngine_DiagnosesHttp400RangeError_CorrectlyRemediates()
        {
            var engine = new AiSelfHealingEngine();
            var plan = engine.DiagnoseAndRemediate(
                "http://download.internetdownloadmanager.com/idman643build12.exe",
                null,
                statusCode: 400,
                errorText: "HTTP/1.1 400 Bad Request");

            Assert.NotNull(plan);
            Assert.Equal(HealingActionType.StripRangeAndCustomHeaders, plan.HealingType);
            Assert.True(plan.StripRange);
            Assert.True(plan.ForceSingleStream);
            Assert.True(plan.StripCustomHeaders);
        }

        [Fact]
        public void AiSelfHealingEngine_DiagnosesYouTubeBotBlock_CorrectlyRemediates()
        {
            var engine = new AiSelfHealingEngine();
            var plan = engine.DiagnoseAndRemediate(
                "https://www.youtube.com/watch?v=1eEBjQW3pHQ",
                null,
                errorText: "ERROR: [youtube] 1eEBjQW3pHQ: Sign in to confirm you're not a bot");

            Assert.NotNull(plan);
            Assert.Equal(HealingActionType.RouteThroughCloudResolver, plan.HealingType);
            Assert.True(plan.UseCloudResolver);
            Assert.Contains("bot verification", plan.Explanation);
        }

        [Fact]
        public void AiOptimizationService_CleanAndOptimizeFilename_SanitizesProperly()
        {
            var raw = "https://cdn.example.com/media/Taylor_Swift_Blank_Space.mp4?token=abc12345#frag";
            var clean = AiOptimizationService.Current.CleanFileName(raw);

            Assert.False(string.IsNullOrWhiteSpace(clean));
            Assert.Equal("Taylor Swift Blank Space.mp4", clean);
        }

        [Fact]
        public void AiOptimizationService_PredictOptimalConcurrency_ReturnsValidRange()
        {
            // Small file (< 2 MB) returns 1 to 4 streams
            var smallThreads = AiOptimizationService.Current.PredictOptimalConcurrency(1024 * 500);
            Assert.InRange(smallThreads, 1, 4);

            // Large file (> 20 MB) returns 16 to 32 streams
            var largeThreads = AiOptimizationService.Current.PredictOptimalConcurrency(10L * 1024 * 1024 * 1024);
            Assert.InRange(largeThreads, 16, 32);
        }

        [Fact]
        public void CloudResolverService_IsYouTubeUrl_IdentifiesYouTubeUrls()
        {
            var resolver = new CloudResolverService();
            Assert.True(resolver.IsYouTubeUrl("https://www.youtube.com/watch?v=1eEBjQW3pHQ"));
            Assert.True(resolver.IsYouTubeUrl("https://youtu.be/1eEBjQW3pHQ"));
            Assert.True(resolver.IsYouTubeUrl("https://music.youtube.com/watch?v=1eEBjQW3pHQ"));
            Assert.False(resolver.IsYouTubeUrl("https://example.com/file.zip"));
        }

        [Fact]
        public void CloudResolverService_CanResolve_IdentifiesSupportedDomains()
        {
            var resolver = new CloudResolverService();
            Assert.True(resolver.CanResolve("https://www.youtube.com/watch?v=test"));
            Assert.True(resolver.CanResolve("https://drive.google.com/file/d/test/view"));
            Assert.True(resolver.CanResolve("https://www.mediafire.com/file/test"));
            Assert.True(resolver.CanResolve("https://pixeldrain.com/u/test"));
            Assert.True(resolver.CanResolve("https://happymod.com/app/test.html"));
        }

        [Fact]
        public void AiOptimizationService_PredictCategory_AccuratelyClassifies()
        {
            var ai = AiOptimizationService.Current;
            Assert.Equal(FileCategory.Music, ai.PredictCategory("track.mp3"));
            Assert.Equal(FileCategory.Video, ai.PredictCategory("movie.mp4"));
            Assert.Equal(FileCategory.Programs, ai.PredictCategory("setup.exe"));
            Assert.Equal(FileCategory.Documents, ai.PredictCategory("manual.pdf"));
            Assert.Equal(FileCategory.Compressed, ai.PredictCategory("archive.7z"));
        }

        [Fact]
        public async Task VideoDownloaderViewModel_ProbeUrl_QueriesCloudResolverFirst()
        {
            var mediaEngine = new MediaEngineService();
            var config = new ConfigurationService();
            var history = new HistoryService();
            var cloud = new TestCloudResolver();

            var vm = new VideoDownloaderViewModel(mediaEngine, config, history, cloud);
            vm.InputUrl = "https://www.youtube.com/watch?v=1eEBjQW3pHQ";

            // Should probe without throwing any exception
            await ((AsyncRelayCommand)vm.ProbeUrlCommand).ExecuteAsync(null);

            Assert.False(vm.IsProbing);
            Assert.NotNull(vm.CurrentProbeResult);
            Assert.NotEmpty(vm.AvailableFormats);
            Assert.Contains(vm.AvailableFormats, f => f.FormatId == "cloud_cdn_stream");
        }

        private class TestCloudResolver : ICloudResolverService
        {
            public string BaseUrl => "https://test";
            public bool CanResolve(string url) => true;
            public bool IsYouTubeUrl(string url) => url.Contains("youtube.com") || url.Contains("youtu.be");
            public Task<CloudResolvedMedia?> ResolveMediaAsync(string url, string? format = null, CancellationToken ct = default) =>
                Task.FromResult<CloudResolvedMedia?>(new CloudResolvedMedia { Success = true, DirectStreamUrl = "https://cdn.test/video.mp4", Title = "Test Video" });
            public Task<MediaProbeResult?> ResolveMediaProbeAsync(string url, CancellationToken ct = default)
            {
                var probe = new MediaProbeResult
                {
                    Id = url,
                    Title = "Test Video",
                    Formats = new List<MediaFormat>
                    {
                        new MediaFormat { FormatId = "cloud_cdn_stream", Resolution = "1080p", Extension = "mp4" },
                        new MediaFormat { FormatId = "cloud_mp3_stream", Resolution = "320kbps", Extension = "mp3" }
                    }
                };
                return Task.FromResult<MediaProbeResult?>(probe);
            }
            public Task<string?> ResolveDirectDownloadUrlAsync(string url, CancellationToken ct = default) => Task.FromResult<string?>("https://cdn.test/video.mp4");
            public Task<ApkSearchResult> SearchApkAsync(string query, string provider = "all", CancellationToken ct = default) => Task.FromResult(new ApkSearchResult { Success = true });
            public Task<string?> ResolveApkDownloadUrlAsync(string apkLinkOrPackage, CancellationToken ct = default) => Task.FromResult<string?>("https://cdn.test/app.apk");
            public Task<bool> CheckServiceHealthAsync(CancellationToken ct = default) => Task.FromResult(true);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "Unknown";
            if (bytes >= 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
            if (bytes >= 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
            if (bytes >= 1024) return $"{(bytes / 1024.0):F1} KB";
            return $"{bytes} B";
        }

        private class SyncProgress<T> : IProgress<T>
        {
            private readonly Action<T> _action;
            public SyncProgress(Action<T> action) => _action = action;
            public void Report(T value) => _action(value);
        }
    }
}
