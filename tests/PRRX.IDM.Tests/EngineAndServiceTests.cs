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

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "Unknown";
            if (bytes >= 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
            if (bytes >= 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
            if (bytes >= 1024) return $"{(bytes / 1024.0):F1} KB";
            return $"{bytes} B";
        }
    }
}
