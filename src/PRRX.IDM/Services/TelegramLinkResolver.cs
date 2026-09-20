// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using PRRX.IDM.Models;

namespace PRRX.IDM.Services
{
    public class TelegramResolvedMedia
    {
        public string SourceUrl { get; set; } = string.Empty;
        public string DirectStreamUrl { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string MediaType { get; set; } = "video"; // video, audio, document, photo
        public string Title { get; set; } = string.Empty;
        public string ChannelName { get; set; } = string.Empty;
        public string FormattedSize { get; set; } = string.Empty;
        public long EstimatedSizeBytes { get; set; } = 0;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public bool IsDirectDownloadable => !string.IsNullOrWhiteSpace(DirectStreamUrl);
    }

    public interface ITelegramLinkResolver
    {
        bool IsTelegramUrl(string? url);
        bool IsTelegramPostUrl(string? url, out string channel, out string messageId);
        bool IsTelegramBotFileUrl(string? url, out string botToken, out string filePath);
        Task<TelegramResolvedMedia?> ResolveTelegramMediaAsync(string url, CancellationToken cancellationToken = default);
    }

    public class TelegramLinkResolver : ITelegramLinkResolver
    {
        private static readonly Regex TelegramPostRegex = new(
            @"^https?:\/\/(?:www\.)?(?:t|telegram)\.me\/([a-zA-Z0-9_+]+)\/(\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex TelegramBotFileRegex = new(
            @"^https?:\/\/api\.telegram\.org\/file\/bot([a-zA-Z0-9:_-]+)\/(.+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex GeneralTelegramRegex = new(
            @"^(?:tg:\/\/|https?:\/\/(?:www\.)?(?:t|telegram)\.me\/)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Modern OpenGraph & Meta Tag extractors (Clean & stable standard tags)
        private static readonly Regex OgVideoRegex = new(
            @"<meta[^>]+(?:property|name)=[""'](?:og:video(?::url|:secure_url)?|twitter:player:stream)[""'][^>]+content=[""']([^""']+)[""']",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex OgAudioRegex = new(
            @"<meta[^>]+(?:property|name)=[""'](?:og:audio(?::url|:secure_url)?)[""'][^>]+content=[""']([^""']+)[""']",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex OgTitleRegex = new(
            @"<meta[^>]+(?:property|name)=[""']og:title[""'][^>]+content=[""']([^""']+)[""']",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex OgImageRegex = new(
            @"<meta[^>]+(?:property|name)=[""']og:image[""'][^>]+content=[""']([^""']+)[""']",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // HTML5 Media element extractors (robust for single quotes, double quotes, and arbitrary attribute ordering)
        private static readonly Regex VideoSrcRegex = new(
            @"<video\b[^>]*?\bsrc=[""']?([^""'>\s]+)[""']?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AudioSrcRegex = new(
            @"<audio\b[^>]*?\bsrc=[""']?([^""'>\s]+)[""']?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SourceTagRegex = new(
            @"<source\b[^>]*?\bsrc=[""']?([^""'>\s]+)[""']?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DocumentWrapRegex = new(
            @"<a\b[^>]*?\bclass=[""'][^""']*tgme_widget_message_document_wrap[^""']*[""'][^>]*?\bhref=[""']?([^""'>\s]+)[""']?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DocumentTitleRegex = new(
            @"<div\b[^>]*?\bclass=[""'][^""']*tgme_widget_message_document_title[^""']*[""'][^>]*>([^<]+)<\/div>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DocumentExtraRegex = new(
            @"<div\b[^>]*?\bclass=[""'][^""']*tgme_widget_message_document_extra[^""']*[""'][^>]*>([^<]+)<\/div>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex MessageTextRegex = new(
            @"<div\b[^>]*?\bclass=[""'][^""']*tgme_widget_message_text[^""']*[""'][^>]*>(.*?)<\/div>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = true
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        static TelegramLinkResolver()
        {
            if (!SharedHttpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                SharedHttpClient.DefaultRequestHeaders.Add("User-Agent", 
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36 PRRX-IDM/1.6.0");
            }
        }

        public bool IsTelegramUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            var trimmed = url.Trim();
            return GeneralTelegramRegex.IsMatch(trimmed) || 
                   TelegramBotFileRegex.IsMatch(trimmed) ||
                   TelegramDownloadProvider.Current.CanHandle(trimmed);
        }

        public static bool ParsePostUrl(string? url, out string channel, out string messageId)
        {
            channel = string.Empty;
            messageId = string.Empty;
            if (string.IsNullOrWhiteSpace(url)) return false;

            var match = TelegramPostRegex.Match(url.Trim());
            if (match.Success)
            {
                channel = match.Groups[1].Value;
                messageId = match.Groups[2].Value;
                return true;
            }
            return false;
        }

        public bool IsTelegramPostUrl(string? url, out string channel, out string messageId)
        {
            return ParsePostUrl(url, out channel, out messageId);
        }

        public bool IsTelegramBotFileUrl(string? url, out string botToken, out string filePath)
        {
            botToken = string.Empty;
            filePath = string.Empty;
            if (string.IsNullOrWhiteSpace(url)) return false;

            var match = TelegramBotFileRegex.Match(url.Trim());
            if (match.Success)
            {
                botToken = match.Groups[1].Value;
                filePath = match.Groups[2].Value;
                return true;
            }
            return false;
        }

        public async Task<TelegramResolvedMedia?> ResolveTelegramMediaAsync(string url, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var cleanUrl = url.Trim();

            // 1. Check if it is a tg://file URI (remote task of any size)
            if (cleanUrl.StartsWith("tg://", StringComparison.OrdinalIgnoreCase))
            {
                var req = TelegramDownloadProvider.Current.ParseTelegramUrlOrTask(cleanUrl);
                if (req != null)
                {
                    return new TelegramResolvedMedia
                    {
                        SourceUrl = cleanUrl,
                        DirectStreamUrl = cleanUrl,
                        FileName = req.FileName,
                        Title = req.FileName,
                        MediaType = req.MediaType,
                        FormattedSize = req.FormattedSize,
                        EstimatedSizeBytes = req.FileSize,
                        ChannelName = req.ChatId
                    };
                }
            }

            // 2. Check if it is a Telegram Bot File link
            if (IsTelegramBotFileUrl(cleanUrl, out _, out var botPath))
            {
                var fileName = Path.GetFileName(botPath);
                if (string.IsNullOrWhiteSpace(fileName)) fileName = "telegram_file.bin";

                return new TelegramResolvedMedia
                {
                    SourceUrl = cleanUrl,
                    DirectStreamUrl = cleanUrl,
                    FileName = fileName,
                    Title = fileName,
                    MediaType = GuessMediaTypeFromExtension(fileName),
                    FormattedSize = "Direct Bot Stream"
                };
            }

            // 3. Check if it is a Telegram Post URL (t.me/channel/id)
            if (!IsTelegramPostUrl(cleanUrl, out var channel, out var messageId))
            {
                return null;
            }

            try
            {
                var embedUrl = $"https://t.me/{channel}/{messageId}?embed=1";
                using var request = new HttpRequestMessage(HttpMethod.Get, embedUrl);
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
                request.Headers.Add("Referer", $"https://t.me/{channel}/{messageId}");

                using var response = await SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                return ParseTelegramEmbedHtml(html, cleanUrl, channel, messageId);
            }
            catch
            {
                return null;
            }
        }

        public static TelegramResolvedMedia? ParseTelegramEmbedHtml(string html, string sourceUrl, string channel, string messageId)
        {
            if (string.IsNullOrWhiteSpace(html)) return null;

            var resolved = new TelegramResolvedMedia
            {
                SourceUrl = sourceUrl,
                ChannelName = channel
            };

            // 1. Look for Video: HTML5 video tag, source tag, or OpenGraph video
            var videoMatch = VideoSrcRegex.Match(html);
            if (videoMatch.Success)
            {
                resolved.DirectStreamUrl = HttpUtility.HtmlDecode(videoMatch.Groups[1].Value);
                resolved.MediaType = "video";
                resolved.FileName = $"{channel}_{messageId}.mp4";
            }
            else
            {
                var ogVideo = OgVideoRegex.Match(html);
                if (ogVideo.Success)
                {
                    resolved.DirectStreamUrl = HttpUtility.HtmlDecode(ogVideo.Groups[1].Value);
                    resolved.MediaType = "video";
                    resolved.FileName = $"{channel}_{messageId}.mp4";
                }
            }

            // 2. Look for Audio: HTML5 audio tag or OpenGraph audio
            if (string.IsNullOrEmpty(resolved.DirectStreamUrl))
            {
                var audioMatch = AudioSrcRegex.Match(html);
                if (audioMatch.Success)
                {
                    resolved.DirectStreamUrl = HttpUtility.HtmlDecode(audioMatch.Groups[1].Value);
                    resolved.MediaType = "audio";
                    resolved.FileName = $"{channel}_{messageId}.mp3";
                }
                else
                {
                    var ogAudio = OgAudioRegex.Match(html);
                    if (ogAudio.Success)
                    {
                        resolved.DirectStreamUrl = HttpUtility.HtmlDecode(ogAudio.Groups[1].Value);
                        resolved.MediaType = "audio";
                        resolved.FileName = $"{channel}_{messageId}.mp3";
                    }
                }
            }

            // 3. Look for Source Tag
            if (string.IsNullOrEmpty(resolved.DirectStreamUrl))
            {
                var srcMatch = SourceTagRegex.Match(html);
                if (srcMatch.Success)
                {
                    resolved.DirectStreamUrl = HttpUtility.HtmlDecode(srcMatch.Groups[1].Value);
                    resolved.MediaType = "video";
                    resolved.FileName = $"{channel}_{messageId}.mp4";
                }
            }

            // 4. Look for Document Wrap
            if (string.IsNullOrEmpty(resolved.DirectStreamUrl))
            {
                var docMatch = DocumentWrapRegex.Match(html);
                if (docMatch.Success)
                {
                    resolved.DirectStreamUrl = HttpUtility.HtmlDecode(docMatch.Groups[1].Value);
                    resolved.MediaType = "document";
                }
            }

            // 5. Extract Document Title or OpenGraph Title
            var titleMatch = DocumentTitleRegex.Match(html);
            if (titleMatch.Success)
            {
                var title = HttpUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim();
                if (!string.IsNullOrWhiteSpace(title))
                {
                    resolved.Title = title;
                    resolved.FileName = title;
                }
            }
            else
            {
                var ogTitle = OgTitleRegex.Match(html);
                if (ogTitle.Success)
                {
                    var title = HttpUtility.HtmlDecode(ogTitle.Groups[1].Value).Trim();
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        resolved.Title = title;
                        if (string.IsNullOrWhiteSpace(resolved.FileName) || resolved.FileName.StartsWith($"{channel}_{messageId}"))
                        {
                            var safeName = Security.SecurityGuard.SanitizeFileName(title);
                            if (!string.IsNullOrWhiteSpace(safeName))
                            {
                                var defaultExt = resolved.MediaType switch
                                {
                                    "video" => ".mp4",
                                    "audio" => ".mp3",
                                    "photo" => ".jpg",
                                    _ => ".bin"
                                };
                                if (!Path.HasExtension(safeName)) safeName += defaultExt;
                                resolved.FileName = safeName;
                            }
                        }
                    }
                }
            }

            // 6. Extract Document Extra / Size
            var extraMatch = DocumentExtraRegex.Match(html);
            if (extraMatch.Success)
            {
                var extra = HttpUtility.HtmlDecode(extraMatch.Groups[1].Value).Trim();
                resolved.FormattedSize = extra;
            }

            // 7. Extract Thumbnail from OpenGraph image
            var ogImg = OgImageRegex.Match(html);
            if (ogImg.Success)
            {
                resolved.ThumbnailUrl = HttpUtility.HtmlDecode(ogImg.Groups[1].Value);
            }

            // 8. Extract Message Text caption if title not yet set
            if (string.IsNullOrWhiteSpace(resolved.Title))
            {
                var textMatch = MessageTextRegex.Match(html);
                if (textMatch.Success)
                {
                    var rawText = Regex.Replace(textMatch.Groups[1].Value, "<.*?>", string.Empty);
                    rawText = HttpUtility.HtmlDecode(rawText).Trim();
                    if (!string.IsNullOrWhiteSpace(rawText))
                    {
                        var preview = rawText.Length > 60 ? rawText.Substring(0, 57) + "..." : rawText;
                        resolved.Title = preview;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(resolved.Title))
            {
                resolved.Title = $"{channel} Post #{messageId}";
            }

            if (string.IsNullOrWhiteSpace(resolved.FileName))
            {
                resolved.FileName = $"{channel}_{messageId}.bin";
            }

            return resolved;
        }

        private static string GuessMediaTypeFromExtension(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".mp4" or ".mkv" or ".mov" or ".webm" or ".avi" => "video",
                ".mp3" or ".m4a" or ".flac" or ".ogg" or ".wav" => "audio",
                ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" => "photo",
                _ => "document"
            };
        }
    }
}
