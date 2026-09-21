// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-TEST-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System.IO;
using System.Threading.Tasks;
using PRRX.IDM.Models;
using PRRX.IDM.Services;
using PRRX.IDM.ViewModels;
using Xunit;

namespace PRRX.IDM.Tests
{
    public class TelegramResolverTests
    {
        private readonly TelegramLinkResolver _resolver = new();

        [Theory]
        [InlineData("https://t.me/durov/123", true)]
        [InlineData("http://telegram.me/tech_channel/45678", true)]
        [InlineData("https://www.t.me/breaking_news/999", true)]
        [InlineData("https://api.telegram.org/file/bot123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11/music/track.mp3", true)]
        [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ", false)]
        [InlineData("https://example.com/file.zip", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsTelegramUrl_IdentifiesCorrectly(string? url, bool expected)
        {
            var result = _resolver.IsTelegramUrl(url);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void IsTelegramPostUrl_ExtractsChannelAndMessageId()
        {
            var isPost = _resolver.IsTelegramPostUrl("https://t.me/telegram/88", out var channel, out var messageId);
            Assert.True(isPost);
            Assert.Equal("telegram", channel);
            Assert.Equal("88", messageId);
        }

        [Fact]
        public void IsTelegramBotFileUrl_ExtractsTokenAndPath()
        {
            var url = "https://api.telegram.org/file/bot987654321:AAHk1_xyz/documents/manual.pdf";
            var isBot = _resolver.IsTelegramBotFileUrl(url, out var token, out var path);
            Assert.True(isBot);
            Assert.Equal("987654321:AAHk1_xyz", token);
            Assert.Equal("documents/manual.pdf", path);
        }

        [Fact]
        public void ParseTelegramEmbedHtml_ParsesVideoCorrectly()
        {
            var html = @"
                <html>
                <body>
                    <div class=""tgme_widget_message"">
                        <div class=""tgme_widget_message_user"">
                            <span dir=""auto"">Durov Channel</span>
                        </div>
                        <video src=""https://cdn4.telesco.pe/file/video_sample.mp4"" preload=""metadata""></video>
                        <div class=""tgme_widget_message_text"">Check out this new Telegram video demo!</div>
                    </div>
                </body>
                </html>";

            var result = TelegramLinkResolver.ParseTelegramEmbedHtml(html, "https://t.me/durov/42", "durov", "42");
            Assert.NotNull(result);
            Assert.Equal("https://cdn4.telesco.pe/file/video_sample.mp4", result.DirectStreamUrl);
            Assert.Equal("video", result.MediaType);
            Assert.Equal("durov_42.mp4", result.FileName);
            Assert.Equal("Check out this new Telegram video demo!", result.Title);
            Assert.True(result.IsDirectDownloadable);
        }

        [Fact]
        public void ParseTelegramEmbedHtml_ParsesDocumentWithTitleAndSize()
        {
            var html = @"
                <html>
                <body>
                    <div class=""tgme_widget_message"">
                        <a class=""tgme_widget_message_document_wrap"" href=""https://cdn1.telesco.pe/file/document_report.pdf"">
                            <div class=""tgme_widget_message_document_title"">Financial_Report_2026.pdf</div>
                            <div class=""tgme_widget_message_document_extra"">14.8 MB</div>
                        </a>
                    </div>
                </body>
                </html>";

            var result = TelegramLinkResolver.ParseTelegramEmbedHtml(html, "https://t.me/finance/101", "finance", "101");
            Assert.NotNull(result);
            Assert.Equal("https://cdn1.telesco.pe/file/document_report.pdf", result.DirectStreamUrl);
            Assert.Equal("document", result.MediaType);
            Assert.Equal("Financial_Report_2026.pdf", result.FileName);
            Assert.Equal("14.8 MB", result.FormattedSize);
            Assert.True(result.IsDirectDownloadable);
        }

        [Fact]
        public async Task ResolveTelegramMediaAsync_HandlesBotUrl()
        {
            var botUrl = "https://api.telegram.org/file/bot12345:TOKEN/audio/voice.ogg";
            var result = await _resolver.ResolveTelegramMediaAsync(botUrl);

            Assert.NotNull(result);
            Assert.Equal(botUrl, result.DirectStreamUrl);
            Assert.Equal("voice.ogg", result.FileName);
            Assert.Equal("audio", result.MediaType);
            Assert.True(result.IsDirectDownloadable);
        }

        [Fact]
        public void MediaFormat_SupportsDirectDownloadUrl()
        {
            var format = new MediaFormat
            {
                FormatId = "telegram_cdn_stream",
                Resolution = "Original",
                DirectDownloadUrl = "https://cdn4.telesco.pe/file/sample.mp4"
            };

            Assert.Equal("https://cdn4.telesco.pe/file/sample.mp4", format.DirectDownloadUrl);
        }

        [Theory]
        [InlineData("tg://file?file_id=BAACAgIAAxkBAAIB&file_name=Movie_1080p.mkv&file_size=2147483648&mime_type=video%2Fx-matroska&chat_id=123456&message_id=99", true)]
        [InlineData("https://cdn4.telesco.pe/file/sample.mp4", true)]
        [InlineData("https://web.telegram.org/a/#12345", true)]
        [InlineData("https://api.telegram.org/file/bot12345:TOKEN/video/clip.mp4", true)]
        public void TelegramDownloadProvider_CanHandle_RecognizesSupportedTelegramUris(string url, bool expected)
        {
            var canHandle = TelegramDownloadProvider.Current.CanHandle(url);
            Assert.Equal(expected, canHandle);
        }

        [Fact]
        public void TelegramDownloadProvider_ParseTelegramUrlOrTask_ExtractsLargeFileMetadata()
        {
            // 2.5 GB file test without 20MB limit
            long largeSize = 2684354560; // 2.5 GB
            var tgUri = $"tg://file?file_id=BAACAgIAAxkBAAIB123&file_name=Avengers_Endgame_2026.mkv&file_size={largeSize}&mime_type=video%2Fx-matroska&chat_id=987654321&message_id=42";

            var req = TelegramDownloadProvider.Current.ParseTelegramUrlOrTask(tgUri);
            Assert.NotNull(req);
            Assert.Equal("BAACAgIAAxkBAAIB123", req.FileId);
            Assert.Equal("Avengers_Endgame_2026.mkv", req.FileName);
            Assert.Equal(largeSize, req.FileSize);
            Assert.Equal("video/x-matroska", req.MimeType);
            Assert.Equal("987654321", req.ChatId);
            Assert.Equal(42, req.MessageId);
            Assert.Equal("video", req.MediaType);
            Assert.Equal("2.5 GB", req.FormattedSize);
        }

        [Fact]
        public void TelegramDownloadProvider_FormatBytes_FormatsCorrectly()
        {
            Assert.Equal("500 KB", TelegramDownloadProvider.FormatBytes(500 * 1024));
            Assert.Equal("20 MB", TelegramDownloadProvider.FormatBytes(20 * 1024 * 1024));
            Assert.Equal("1.4 GB", TelegramDownloadProvider.FormatBytes(1503238553));
            Assert.Equal("3.73 GB", TelegramDownloadProvider.FormatBytes(4000000000));
        }

        [Fact]
        public void ParseTelegramEmbedHtml_ParsesModernOpenGraphTags()
        {
            var html = @"
                <html>
                <head>
                    <meta property=""og:video"" content=""https://cdn4.telesco.pe/file/video_og.mp4"">
                    <meta property=""og:title"" content=""Breaking News Video Broadcast"">
                    <meta property=""og:image"" content=""https://cdn4.telesco.pe/file/thumb.jpg"">
                </head>
                <body>
                    <div class=""tgme_widget_message_text"">Check out this live broadcast!</div>
                </body>
                </html>";

            var result = TelegramLinkResolver.ParseTelegramEmbedHtml(html, "https://t.me/news/777", "news", "777");
            Assert.NotNull(result);
            Assert.Equal("https://cdn4.telesco.pe/file/video_og.mp4", result.DirectStreamUrl);
            Assert.Equal("video", result.MediaType);
            Assert.Equal("Breaking News Video Broadcast", result.Title);
            Assert.Equal("https://cdn4.telesco.pe/file/thumb.jpg", result.ThumbnailUrl);
            Assert.True(result.IsDirectDownloadable);
        }

        [Fact]
        public async Task ResolveTelegramMediaAsync_HandlesTgFileUriOfAnySize()
        {
            var tgUrl = "tg://file?file_id=LARGE_FILE_999&file_name=Huge_Archive.zip&file_size=3221225472&mime_type=application%2Fzip&chat_id=777&message_id=12";
            var result = await _resolver.ResolveTelegramMediaAsync(tgUrl);

            Assert.NotNull(result);
            Assert.Equal(tgUrl, result.DirectStreamUrl);
            Assert.Equal("Huge_Archive.zip", result.FileName);
            Assert.Equal("Huge_Archive.zip", result.Title);
            Assert.Equal("document", result.MediaType);
            Assert.Equal("3 GB", result.FormattedSize);
            Assert.Equal(3221225472, result.EstimatedSizeBytes);
            Assert.True(result.IsDirectDownloadable);
        }

        [Fact]
        public void ParseTelegramEmbedHtml_ParsesSingleQuotedMediaAttributes()
        {
            var html = @"
                <html>
                <body>
                    <div class='tgme_widget_message'>
                        <video src='https://cdn4.telesco.pe/file/video_single_quote.mp4' preload='metadata'></video>
                        <a class='tgme_widget_message_document_wrap' href='https://cdn1.telesco.pe/file/single_quote_doc.pdf'>
                            <div class='tgme_widget_message_document_title'>Quarterly_Report.pdf</div>
                            <div class='tgme_widget_message_document_extra'>22.5 MB</div>
                        </a>
                    </div>
                </body>
                </html>";

            var result = TelegramLinkResolver.ParseTelegramEmbedHtml(html, "https://t.me/channel/55", "channel", "55");
            Assert.NotNull(result);
            Assert.Equal("https://cdn4.telesco.pe/file/video_single_quote.mp4", result.DirectStreamUrl);
            Assert.Equal("video", result.MediaType);
            Assert.True(result.IsDirectDownloadable);
        }

        [Theory]
        [InlineData("tg://file?file_id=ABC&file_name=Setup_v2.exe&file_size=50000000", "Setup_v2.exe")]
        [InlineData("https://example.com/dl?file_name=Database_Backup.sql.gz", "Database_Backup.sql.gz")]
        [InlineData("https://example.com/fetch?file-name=AudioTrack.flac", "AudioTrack.flac")]
        [InlineData("https://example.com/files/Presentation_2026.pptx", "Presentation_2026.pptx")]
        public void DownloadFileInfoViewModel_ExtractFileNameFromUrl_ExtractsCorrectly(string url, string expectedFileName)
        {
            var fileName = DownloadFileInfoViewModel.ExtractFileNameFromUrl(url);
            Assert.Equal(expectedFileName, fileName);
        }

        [Fact]
        public void TelegramDownloadProvider_ParseTelegramUrlOrTask_ExtractsChannelAndPublicUrl()
        {
            var tgUri = "tg://file?file_id=XYZ_999&file_name=Movie_4K.mkv&file_size=4000000000&channel=cinemahub&channel_msg_id=789&public_url=https%3A%2F%2Ft.me%2Fcinemahub%2F789";
            var req = TelegramDownloadProvider.Current.ParseTelegramUrlOrTask(tgUri);

            Assert.NotNull(req);
            Assert.Equal("XYZ_999", req.FileId);
            Assert.Equal("Movie_4K.mkv", req.FileName);
            Assert.Equal(4000000000, req.FileSize);
            Assert.Equal("cinemahub", req.ChatId);
            Assert.Equal(789, req.MessageId);
            Assert.Equal("https://t.me/cinemahub/789", req.DirectStreamUrl);
        }

        [Fact]
        public async Task TelegramDownloadProvider_DownloadAsync_FailsCleanlyWhenChunksFailWithoutZeroPadding()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"prrx_fail_test_{System.Guid.NewGuid():N}.bin");
            try
            {
                var req = new TelegramDownloadRequest
                {
                    Url = "tg://file?file_id=NON_EXISTENT_FILE_ID_999999&file_name=test.bin&file_size=10485760",
                    DestinationFilePath = tempFile,
                    FileName = "test.bin",
                    FileSize = 10485760 // 10 MB
                };

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
                var result = await TelegramDownloadProvider.Current.DownloadAsync(req, null, cts.Token);

                // Must fail cleanly because fake zero buffer was removed and endpoint doesn't exist
                Assert.False(result);
                Assert.False(File.Exists(tempFile), "Temporary destination file should be cleaned up on failure");
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                try { if (File.Exists(tempFile + ".prrx_tg_part")) File.Delete(tempFile + ".prrx_tg_part"); } catch { }
            }
        }

        [Fact]
        public async Task TelegramDownloadProvider_ResolveDirectStreamUrlAsync_ResolvesCdnFromPublicUrlParam()
        {
            var cdnUrl = "https://cdn4.telesco.pe/file/test_stream.mp4";
            var tgUri = $"tg://file?file_id=TEST_123&file_name=video.mp4&file_size=50000000&public_url={Uri.EscapeDataString(cdnUrl)}";

            var resolved = await TelegramDownloadProvider.Current.ResolveDirectStreamUrlAsync(tgUri);
            Assert.Equal(cdnUrl, resolved);
        }

        [Fact]
        public void DownloadFileInfoViewModel_ExtractsTelegramMetadataImmediately()
        {
            var tgUri = "tg://file?file_id=VID_777&file_name=Sample_Episode.mkv&file_size=157286400&channel=NecflixsLK&channel_msg_id=7445";
            var vm = new DownloadFileInfoViewModel(tgUri, Path.GetTempPath());

            Assert.Equal("Sample_Episode.mkv", vm.FileName);
            Assert.Equal(157286400, vm.DetectedBytes);
            Assert.Equal(FileCategory.Video, vm.SelectedCategory);
        }
    }
}
