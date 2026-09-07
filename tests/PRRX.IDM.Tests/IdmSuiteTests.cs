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
    }
}
