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
    }
}
