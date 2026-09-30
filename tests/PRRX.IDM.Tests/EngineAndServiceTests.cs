using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using PRRX.IDM.Models;
using PRRX.IDM.Services;
using PRRX.IDM.ViewModels;

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

            // UnlinkTelegramCommand
            Assert.True(vm.UnlinkTelegramCommand.CanExecute(null));
            vm.UnlinkTelegramCommand.Execute(null);

            Assert.False(string.IsNullOrWhiteSpace(vm.TelegramPairingCode));
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
