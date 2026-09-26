// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using PRRX.IDM.Models;

namespace PRRX.IDM.Services
{
    public class TelegramDownloadRequest
    {
        public string Url { get; set; } = string.Empty;
        public string DestinationFilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; } = 0;
        public string FormattedSize { get; set; } = string.Empty;
        public string MediaType { get; set; } = "document";
        public string FileId { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public string ChatId { get; set; } = string.Empty;
        public long MessageId { get; set; } = 0;
        public string Channel { get; set; } = string.Empty;
        public long ChannelMessageId { get; set; } = 0;
        public string DirectStreamUrl { get; set; } = string.Empty;
        public int Concurrency { get; set; } = 32;
    }

    public interface ITelegramDownloadProvider
    {
        bool CanHandle(string? url);
        TelegramDownloadRequest? ParseTelegramUrlOrTask(string url, string? fallbackFileName = null, long? fallbackFileSize = null);
        Task<string?> ResolveDirectStreamUrlAsync(string url, CancellationToken cancellationToken = default);
        Task<bool> DownloadAsync(
            TelegramDownloadRequest request,
            IProgress<SegmentProgressEventArgs>? progress = null,
            CancellationToken cancellationToken = default);
    }

    public class TelegramDownloadProvider : ITelegramDownloadProvider
    {
        public static TelegramDownloadProvider Current { get; } = new();

        private static string? _cachedBotToken;
        private static string? _cachedApiHash;
        private static DateTime _tokenCacheExpiry = DateTime.MinValue;
        private static readonly SemaphoreSlim _tokenLock = new(1, 1);
        private const string DefaultWorkerBase = "https://prrx-api.sayurusenavirathna70.workers.dev";

        public static async Task<string?> GetBotTokenAsync(CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(_cachedBotToken) && DateTime.UtcNow < _tokenCacheExpiry)
            {
                return _cachedBotToken;
            }

            await _tokenLock.WaitAsync(cancellationToken);
            try
            {
                if (!string.IsNullOrWhiteSpace(_cachedBotToken) && DateTime.UtcNow < _tokenCacheExpiry)
                {
                    return _cachedBotToken;
                }

                using var req = new HttpRequestMessage(HttpMethod.Get, $"{DefaultWorkerBase}/api/telegram/client-config");
                using var resp = await SharedClient.SendAsync(req, cancellationToken);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("token", out var tokenElem) &&
                        tokenElem.GetString() is { } token && !string.IsNullOrWhiteSpace(token))
                    {
                        _cachedBotToken = token.Trim();
                        _tokenCacheExpiry = DateTime.UtcNow.AddMinutes(30);
                    }
                    if (doc.RootElement.TryGetProperty("api_hash", out var hashElem) &&
                        hashElem.GetString() is { } hash && !string.IsNullOrWhiteSpace(hash))
                    {
                        _cachedApiHash = hash.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TelegramDownloadProvider] Failed to fetch client-config: {ex.Message}");
            }
            finally
            {
                _tokenLock.Release();
            }

            return _cachedBotToken;
        }

        public static async Task<string?> GetApiHashAsync(CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(_cachedApiHash) && DateTime.UtcNow < _tokenCacheExpiry)
            {
                return _cachedApiHash;
            }
            await GetBotTokenAsync(cancellationToken);
            return _cachedApiHash;
        }

        private static readonly Regex TelegramUriRegex = new(
            @"^(?:tg:\/\/|https?:\/\/(?:[a-zA-Z0-9_-]+\.)?(?:t\.me|telegram\.me|telegram\.org|telesco\.pe|stel\.com))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AllowAutoRedirect = true,
            MaxConnectionsPerServer = 64,
            EnableMultipleHttp2Connections = true
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static TelegramDownloadProvider()
        {
            if (!SharedClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                SharedClient.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36 PRRX-IDM/1.8.0");
            }
        }

        public bool CanHandle(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            var trimmed = url.Trim();
            return TelegramUriRegex.IsMatch(trimmed) ||
                   trimmed.StartsWith("tg://", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Contains("api.telegram.org/file/bot", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Contains("telesco.pe", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Contains("web.telegram.org", StringComparison.OrdinalIgnoreCase);
        }

        public TelegramDownloadRequest? ParseTelegramUrlOrTask(string url, string? fallbackFileName = null, long? fallbackFileSize = null)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var cleanUrl = url.Trim();

            var request = new TelegramDownloadRequest
            {
                Url = cleanUrl,
                FileName = fallbackFileName ?? "telegram_file.bin",
                FileSize = fallbackFileSize ?? 0
            };

            // 1. Check if tg://file or tg:// stream URI
            if (cleanUrl.StartsWith("tg://", StringComparison.OrdinalIgnoreCase))
            {
                ParseTgUri(cleanUrl, request);
                return request;
            }

            // 2. Check if direct bot file URL
            if (cleanUrl.Contains("api.telegram.org/file/bot", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(cleanUrl);
                var seg = Path.GetFileName(uri.AbsolutePath);
                if (!string.IsNullOrWhiteSpace(seg))
                {
                    request.FileName = Uri.UnescapeDataString(seg);
                }
                request.DirectStreamUrl = cleanUrl;
                request.MediaType = GuessMediaType(request.FileName, null);
                if (request.FileSize > 0)
                {
                    request.FormattedSize = FormatBytes(request.FileSize);
                }
                return request;
            }

            // 3. Check if Telesco.pe or CDN stream URL
            if (cleanUrl.Contains("telesco.pe", StringComparison.OrdinalIgnoreCase) ||
                cleanUrl.Contains("stel.com", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(cleanUrl);
                var seg = Path.GetFileName(uri.AbsolutePath);
                if (!string.IsNullOrWhiteSpace(seg))
                {
                    request.FileName = Uri.UnescapeDataString(seg);
                }
                request.DirectStreamUrl = cleanUrl;
                request.MediaType = cleanUrl.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? "video" : "document";
                return request;
            }

            // 4. Check if t.me/channel/messageId post
            if (TelegramLinkResolver.ParsePostUrl(cleanUrl, out var channel, out var msgId))
            {
                request.ChatId = channel;
                if (long.TryParse(msgId, out var mid)) request.MessageId = mid;
                request.FileName = $"{channel}_{msgId}.bin";
                return request;
            }

            return request;
        }

        private static void ParseTgUri(string tgUrl, TelegramDownloadRequest request)
        {
            try
            {
                var queryIndex = tgUrl.IndexOf('?');
                if (queryIndex < 0) return;

                var queryString = tgUrl.Substring(queryIndex + 1);
                var queryParams = HttpUtility.ParseQueryString(queryString);

                var fileId = queryParams["file_id"] ?? queryParams["id"] ?? string.Empty;
                var fileName = queryParams["file_name"] ?? queryParams["name"] ?? string.Empty;
                var fileSizeStr = queryParams["file_size"] ?? queryParams["size"] ?? "0";
                var mimeType = queryParams["mime_type"] ?? queryParams["mime"] ?? string.Empty;
                var chatId = queryParams["chat_id"] ?? string.Empty;
                var msgIdStr = queryParams["message_id"] ?? queryParams["msg_id"] ?? "0";
                var channel = queryParams["channel"] ?? string.Empty;
                var channelMsgIdStr = queryParams["channel_msg_id"] ?? "0";
                var publicUrl = queryParams["public_url"] ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(fileId)) request.FileId = fileId;
                if (!string.IsNullOrWhiteSpace(fileName)) request.FileName = Uri.UnescapeDataString(fileName);
                if (long.TryParse(fileSizeStr, out var size) && size > 0) request.FileSize = size;
                if (!string.IsNullOrWhiteSpace(mimeType)) request.MimeType = Uri.UnescapeDataString(mimeType);
                if (!string.IsNullOrWhiteSpace(chatId)) request.ChatId = chatId;
                if (long.TryParse(msgIdStr, out var mid)) request.MessageId = mid;
                if (!string.IsNullOrWhiteSpace(channel))
                {
                    request.Channel = channel;
                    request.ChatId = channel;
                    if (long.TryParse(channelMsgIdStr, out var cmid) && cmid > 0)
                    {
                        request.ChannelMessageId = cmid;
                        request.MessageId = cmid;
                    }
                }
                else if (long.TryParse(channelMsgIdStr, out var cmidOnly) && cmidOnly > 0)
                {
                    request.ChannelMessageId = cmidOnly;
                }
                if (!string.IsNullOrWhiteSpace(publicUrl))
                {
                    request.DirectStreamUrl = Uri.UnescapeDataString(publicUrl);
                }

                request.FormattedSize = FormatBytes(request.FileSize);
                request.MediaType = GuessMediaType(request.FileName, request.MimeType);
            }
            catch { }
        }

        public async Task<string?> ResolveDirectStreamUrlAsync(string url, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var cleanUrl = url.Trim();

            // 1. If it's a tg://file URI, resolve official Bot API path (<=20MB) or explicit non-teaser CDN URL
            if (cleanUrl.StartsWith("tg://", StringComparison.OrdinalIgnoreCase))
            {
                var req = ParseTelegramUrlOrTask(cleanUrl);
                if (req != null)
                {
                    // A. Check if DirectStreamUrl is already an explicit direct CDN stream URL (not telesco.pe teaser)
                    if (!string.IsNullOrWhiteSpace(req.DirectStreamUrl) &&
                        !req.DirectStreamUrl.Contains("telesco.pe", StringComparison.OrdinalIgnoreCase) &&
                        TelegramLinkResolver.IsValidDirectStreamUrl(req.DirectStreamUrl))
                    {
                        return req.DirectStreamUrl;
                    }

                    // B. If file is <= 20MB, try official Bot API getFile
                    if (!string.IsNullOrWhiteSpace(req.FileId) && (req.FileSize <= 0 || req.FileSize <= 20 * 1024 * 1024))
                    {
                        var directUrl = await TryGetBotApiFileUrlAsync(req.FileId, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(directUrl))
                        {
                            return directUrl;
                        }
                    }
                }

                // For all other tg:// files (up to 2GB/4GB), MTProto is authoritative
                return null;
            }

            // 2. Direct Telegram Bot File URL (<= 20MB)
            if (cleanUrl.Contains("api.telegram.org/file/bot", StringComparison.OrdinalIgnoreCase))
            {
                return cleanUrl;
            }

            // 3. Direct CDN URLs (telesco.pe, stel.com)
            if (cleanUrl.Contains("telesco.pe", StringComparison.OrdinalIgnoreCase) ||
                cleanUrl.Contains("stel.com", StringComparison.OrdinalIgnoreCase))
            {
                return cleanUrl;
            }

            // 4. Telegram Web Stream URLs
            if (cleanUrl.Contains("web.telegram.org", StringComparison.OrdinalIgnoreCase) &&
                (cleanUrl.Contains("/stream") || cleanUrl.Contains("/file")))
            {
                return cleanUrl;
            }

            // 5. Telegram Post URL (t.me/channel/messageId)
            if (TelegramLinkResolver.ParsePostUrl(cleanUrl, out var channel, out var messageId))
            {
                var resolver = new TelegramLinkResolver();
                var media = await resolver.ResolveTelegramMediaAsync(cleanUrl, cancellationToken);
                if (media != null && !string.IsNullOrWhiteSpace(media.DirectStreamUrl) && TelegramLinkResolver.IsValidDirectStreamUrl(media.DirectStreamUrl))
                {
                    return media.DirectStreamUrl;
                }
            }

            return null;
        }

        private async Task<string?> TryGetBotApiFileUrlAsync(string fileId, CancellationToken cancellationToken)
        {
            try
            {
                var botToken = await GetBotTokenAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(botToken)) return null;

                var endpoint = $"https://api.telegram.org/bot{botToken}/getFile?file_id={fileId}";
                using var resp = await SharedClient.GetAsync(endpoint, cancellationToken);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("ok", out var okElem) && okElem.GetBoolean() &&
                        doc.RootElement.TryGetProperty("result", out var resElem) &&
                        resElem.TryGetProperty("file_path", out var pathElem) &&
                        pathElem.GetString() is { } filePath && !string.IsNullOrWhiteSpace(filePath))
                    {
                        return $"https://api.telegram.org/file/bot{botToken}/{filePath}";
                    }
                }
            }
            catch { }

            return null;
        }

        public async Task<bool> DownloadAsync(
            TelegramDownloadRequest request,
            IProgress<SegmentProgressEventArgs>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.DestinationFilePath))
            {
                throw new ArgumentException("Destination file path must be specified.", nameof(request.DestinationFilePath));
            }

            var destDir = Path.GetDirectoryName(request.DestinationFilePath);
            if (!string.IsNullOrWhiteSpace(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // Step 1: For tg:// URIs and Telegram tasks, ALWAYS download via Native MTProto Engine directly from Telegram DCs
            if (request.Url.StartsWith("tg://", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(request.FileId))
            {
                bool mtprotoSuccess = await TryDownloadViaMtprotoAsync(request, progress, cancellationToken);
                if (mtprotoSuccess)
                {
                    return true;
                }
            }

            // Step 2: Try resolving to direct CDN / HTTP stream URL (non-telescope direct streams)
            var directUrl = !string.IsNullOrWhiteSpace(request.DirectStreamUrl) &&
                            !request.DirectStreamUrl.Contains("telesco.pe", StringComparison.OrdinalIgnoreCase) &&
                            TelegramLinkResolver.IsValidDirectStreamUrl(request.DirectStreamUrl)
                ? request.DirectStreamUrl
                : await ResolveDirectStreamUrlAsync(request.Url, cancellationToken);

            if (!string.IsNullOrWhiteSpace(directUrl) &&
                !directUrl.Contains("telesco.pe", StringComparison.OrdinalIgnoreCase) &&
                TelegramLinkResolver.IsValidDirectStreamUrl(directUrl))
            {
                // Download using native MultiSegmentDownloader with 32 parallel sockets
                var downloader = new MultiSegmentDownloader();
                var tcs = new TaskCompletionSource<bool>();

                downloader.ProgressChanged += (_, args) => progress?.Report(args);
                downloader.DownloadCompleted += (_, _) => tcs.TrySetResult(true);
                downloader.DownloadFailed += (_, _) => tcs.TrySetResult(false);

                using (cancellationToken.Register(() =>
                {
                    downloader.Cancel();
                    tcs.TrySetCanceled(cancellationToken);
                }))
                {
                    var startTask = downloader.StartDownloadAsync(
                        directUrl,
                        request.DestinationFilePath,
                        Math.Max(16, request.Concurrency),
                        cancellationToken);

                    return await tcs.Task;
                }
            }

            // Step 3: Probe fallback chunked streams if direct MTProto was unavailable
            var probeChunk = await FetchTelegramStreamChunkAsync(request, 0, 4096, cancellationToken);
            if (probeChunk == null || probeChunk.Length == 0)
            {
                progress?.Report(new SegmentProgressEventArgs
                {
                    OverallPercentage = 0.0,
                    TotalBytes = request.FileSize,
                    DownloadedBytes = 0,
                    TransferRateFormatted = "0 B/s",
                    TimeLeftFormatted = "--:--",
                    StatusMessage = "Stream Unreachable: Telegram stream could not be established. Ensure channel is public or provide a direct stream link.",
                    IsResumeSupported = false,
                    Threads = new List<DownloadConnectionThread>()
                });
                return false;
            }

            // Step 4: Chunked Stream Assembly Fallback
            return await DownloadLargeTelegramFileChunksAsync(request, progress, cancellationToken);
        }

        /// <summary>
        /// Attempts to download the file directly from Telegram's official Data Centers
        /// using the Native MTProto Engine, completely bypassing the 20 MB Bot API limit.
        /// </summary>
        private async Task<bool> TryDownloadViaMtprotoAsync(
            TelegramDownloadRequest request,
            IProgress<SegmentProgressEventArgs>? progress,
            CancellationToken cancellationToken)
        {
            try
            {
                // 1. Extract channel name from metadata, fileName (@SECL4U), or chatId
                string? targetChannel = !string.IsNullOrWhiteSpace(request.Channel) ? request.Channel : null;
                if (string.IsNullOrWhiteSpace(targetChannel) && !string.IsNullOrWhiteSpace(request.FileName))
                {
                    var match = Regex.Match(request.FileName, @"^@([a-zA-Z0-9_]{3,32})");
                    if (match.Success)
                    {
                        targetChannel = match.Groups[1].Value;
                    }
                }
                if (string.IsNullOrWhiteSpace(targetChannel) && !string.IsNullOrWhiteSpace(request.ChatId) &&
                    !request.ChatId.StartsWith("-100") && !long.TryParse(request.ChatId, out _))
                {
                    targetChannel = request.ChatId;
                }

                long msgId = request.ChannelMessageId > 0 ? request.ChannelMessageId : request.MessageId;

                TL.Document? doc = null;
                // A. If channel username and channel message ID are available
                if (!string.IsNullOrWhiteSpace(targetChannel) && request.ChannelMessageId > 0)
                {
                    doc = await TelegramMtprotoService.Current.GetChannelDocumentAsync(targetChannel, request.ChannelMessageId, cancellationToken);
                }

                // B. If document was sent or forwarded to the bot in its chat
                if (doc == null && request.MessageId > 0)
                {
                    doc = await TelegramMtprotoService.Current.GetChatMessageDocumentAsync(request.MessageId, cancellationToken);
                }

                // C. Fallback: try channel with general msgId
                if (doc == null && !string.IsNullOrWhiteSpace(targetChannel) && msgId > 0)
                {
                    doc = await TelegramMtprotoService.Current.GetChannelDocumentAsync(targetChannel, msgId, cancellationToken);
                }

                var destinationPath = request.DestinationFilePath;
                var tempPath = destinationPath + ".prrx_tg_part";

                long totalBytes = request.FileSize > 0 ? request.FileSize : (doc?.size ?? 0);
                int concurrency = Math.Clamp(request.Concurrency > 0 ? request.Concurrency : 32, 1, 64);
                long segmentSize = totalBytes > 0 ? (long)Math.Ceiling((double)totalBytes / concurrency) : totalBytes;

                var threads = new List<DownloadConnectionThread>();
                for (int i = 0; i < concurrency; i++)
                {
                    long segStart = i * segmentSize;
                    long segEnd = (i == concurrency - 1) ? totalBytes - 1 : Math.Min(totalBytes - 1, segStart + segmentSize - 1);
                    threads.Add(new DownloadConnectionThread
                    {
                        ThreadId = i + 1,
                        StartByte = segStart,
                        EndByte = Math.Max(segStart, segEnd),
                        CurrentByte = segStart,
                        DownloadedBytes = 0,
                        FormattedDownloaded = "0 KB",
                        StatusInfo = "Receiving data...",
                        IsActive = true
                    });
                }

                var stopwatch = Stopwatch.StartNew();
                long lastReportBytes = 0;
                long lastReportTime = stopwatch.ElapsedMilliseconds;

                Action<long, long> progressCallback = (downloaded, total) =>
                {
                    if (total > 0 && totalBytes <= 0) totalBytes = total;

                    var now = stopwatch.ElapsedMilliseconds;
                    if (now - lastReportTime >= 200 || downloaded >= totalBytes)
                    {
                        var timeDiff = (now - lastReportTime) / 1000.0;
                        lastReportTime = now;
                        var bytesDiff = downloaded - lastReportBytes;
                        lastReportBytes = downloaded;

                        double speedBps = timeDiff > 0 ? (bytesDiff / timeDiff) : 0;
                        double pct = totalBytes > 0 ? Math.Clamp((double)downloaded / totalBytes * 100.0, 0, 100) : 0;
                        long remaining = Math.Max(0, totalBytes - downloaded);
                        string eta = speedBps > 1024 ? FormatEta(remaining / speedBps) : "--:--";

                        // Update 32 connection thread visual blocks dynamically
                        if (segmentSize > 0)
                        {
                            for (int i = 0; i < concurrency; i++)
                            {
                                var t = threads[i];
                                if (downloaded >= t.EndByte)
                                {
                                    t.DownloadedBytes = t.EndByte - t.StartByte + 1;
                                    t.ProgressPercentage = 100.0;
                                    t.StatusInfo = "Complete";
                                    t.FormattedDownloaded = FormatBytes(t.DownloadedBytes);
                                }
                                else if (downloaded > t.StartByte)
                                {
                                    t.DownloadedBytes = downloaded - t.StartByte;
                                    long slot = Math.Max(1, t.EndByte - t.StartByte + 1);
                                    t.ProgressPercentage = Math.Clamp((double)t.DownloadedBytes / slot * 100.0, 0, 100);
                                    t.StatusInfo = "Receiving data...";
                                    t.FormattedDownloaded = FormatBytes(t.DownloadedBytes);
                                }
                                else
                                {
                                    t.DownloadedBytes = 0;
                                    t.ProgressPercentage = 0.0;
                                    t.StatusInfo = "Pending";
                                    t.FormattedDownloaded = "0 KB";
                                }
                            }
                        }

                        progress?.Report(new SegmentProgressEventArgs
                        {
                            OverallPercentage = pct,
                            TotalBytes = totalBytes,
                            DownloadedBytes = downloaded,
                            TransferRateFormatted = FormatSpeed(speedBps),
                            TimeLeftFormatted = eta,
                            StatusMessage = $"Downloading Telegram stream ({pct:F1}%)...",
                            IsResumeSupported = true,
                            Threads = new List<DownloadConnectionThread>(threads)
                        });
                    }
                };

                bool downloadOk = false;
                await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 1048576, useAsync: true))
                {
                    if (doc != null)
                    {
                        downloadOk = await TelegramMtprotoService.Current.DownloadDocumentAsync(doc, fs, progressCallback, cancellationToken);
                    }
                    else if (!string.IsNullOrWhiteSpace(request.FileId))
                    {
                        var decoded = TelegramFileIdDecoder.Decode(request.FileId);
                        if (decoded != null && decoded.IsValid)
                        {
                            var location = new TL.InputDocumentFileLocation
                            {
                                id = decoded.Id,
                                access_hash = decoded.AccessHash,
                                file_reference = decoded.FileReference ?? Array.Empty<byte>(),
                                thumb_size = ""
                            };
                            downloadOk = await TelegramMtprotoService.Current.DownloadFileLocationAsync(
                                location, fs, decoded.DcId, totalBytes, progressCallback, cancellationToken);
                        }
                    }
                }

                if (downloadOk && File.Exists(tempPath) && new FileInfo(tempPath).Length > 0)
                {
                    if (File.Exists(destinationPath)) File.Delete(destinationPath);
                    File.Move(tempPath, destinationPath);
                    PRRX.IDM.Security.SecurityGuard.ApplyMarkOfTheWeb(destinationPath, request.DirectStreamUrl ?? request.Url);

                    progress?.Report(new SegmentProgressEventArgs
                    {
                        OverallPercentage = 100.0,
                        TotalBytes = totalBytes,
                        DownloadedBytes = totalBytes,
                        TransferRateFormatted = "Completed",
                        TimeLeftFormatted = "00:00",
                        StatusMessage = "Telegram Download Completed Successfully!",
                        IsResumeSupported = true,
                        Threads = threads
                    });
                    return true;
                }
                else
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TelegramDownloadProvider] MTProto download error: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Downloads large Telegram files of ANY size by chunked stream assembly,
        /// rendering unified 32-segment graphical blocks identically to native downloads.
        /// </summary>
        private async Task<bool> DownloadLargeTelegramFileChunksAsync(
            TelegramDownloadRequest request,
            IProgress<SegmentProgressEventArgs>? progress,
            CancellationToken cancellationToken)
        {
            long totalBytes = request.FileSize;
            if (totalBytes <= 0)
            {
                totalBytes = 10 * 1024 * 1024; // Default fallback allocation
            }

            const int chunkSize = 1048576; // 1 MB high-speed chunks
            int totalChunks = (int)Math.Max(1, Math.Ceiling((double)totalBytes / chunkSize));
            int concurrency = Math.Clamp(request.Concurrency > 0 ? request.Concurrency : 32, 1, 64);

            var destinationPath = request.DestinationFilePath;
            var tempPath = destinationPath + ".prrx_tg_part";

            // Pre-allocate file on disk to eliminate fragmentation and optimize sequential I/O
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 65536, useAsync: true))
            {
                if (totalBytes > 0)
                {
                    fs.SetLength(totalBytes);
                }
            }

            long downloadedBytes = 0;
            var stopwatch = Stopwatch.StartNew();
            long lastReportBytes = 0;
            var lastReportTime = stopwatch.ElapsedMilliseconds;

            // Unify thread visual model: 32 segmented connection blocks across totalBytes
            long segmentSize = totalBytes > 0 ? (long)Math.Ceiling((double)totalBytes / concurrency) : totalBytes;
            var threads = new List<DownloadConnectionThread>();
            for (int i = 0; i < concurrency; i++)
            {
                long segStart = i * segmentSize;
                long segEnd = (i == concurrency - 1) ? totalBytes - 1 : Math.Min(totalBytes - 1, segStart + segmentSize - 1);
                threads.Add(new DownloadConnectionThread
                {
                    ThreadId = i + 1,
                    StartByte = segStart,
                    EndByte = Math.Max(segStart, segEnd),
                    CurrentByte = segStart,
                    DownloadedBytes = 0,
                    FormattedDownloaded = "0 KB",
                    StatusInfo = "Receiving data...",
                    IsActive = true
                });
            }

            var chunkQueue = new ConcurrentQueue<int>();
            for (int i = 0; i < totalChunks; i++)
            {
                chunkQueue.Enqueue(i);
            }

            var activeWorkers = new List<Task>();
            var fileLock = new object();

            for (int workerId = 0; workerId < concurrency; workerId++)
            {
                int currentWorker = workerId;
                activeWorkers.Add(Task.Run(async () =>
                {
                    while (!cancellationToken.IsCancellationRequested && chunkQueue.TryDequeue(out int chunkIndex))
                    {
                        long chunkOffset = (long)chunkIndex * chunkSize;
                        int currentChunkSize = (int)Math.Min(chunkSize, totalBytes - chunkOffset);

                        bool chunkSuccess = false;
                        int retries = 0;

                        while (!chunkSuccess && retries < 3 && !cancellationToken.IsCancellationRequested)
                        {
                            try
                            {
                                var chunkBytes = await FetchTelegramStreamChunkAsync(request, chunkOffset, currentChunkSize, cancellationToken);
                                if (chunkBytes != null && chunkBytes.Length > 0)
                                {
                                    lock (fileLock)
                                    {
                                        using var writeStream = new FileStream(tempPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                                        writeStream.Seek(chunkOffset, SeekOrigin.Begin);
                                        writeStream.Write(chunkBytes, 0, chunkBytes.Length);

                                        // Update corresponding segmented thread block
                                        int tIdx = segmentSize > 0 ? Math.Clamp((int)(chunkOffset / segmentSize), 0, concurrency - 1) : 0;
                                        var targetThread = threads[tIdx];
                                        targetThread.DownloadedBytes += chunkBytes.Length;
                                        targetThread.CurrentByte = chunkOffset + chunkBytes.Length;
                                        targetThread.FormattedDownloaded = FormatBytes(targetThread.DownloadedBytes);
                                        long slotLen = Math.Max(1, targetThread.EndByte - targetThread.StartByte + 1);
                                        targetThread.ProgressPercentage = Math.Clamp((double)targetThread.DownloadedBytes / slotLen * 100.0, 0, 100);
                                        targetThread.StatusInfo = targetThread.DownloadedBytes >= slotLen ? "Complete" : "Receiving data...";
                                    }

                                    Interlocked.Add(ref downloadedBytes, chunkBytes.Length);
                                    chunkSuccess = true;
                                }
                                else
                                {
                                    retries++;
                                    await Task.Delay(200 * retries, cancellationToken);
                                }
                            }
                            catch
                            {
                                retries++;
                                await Task.Delay(300 * retries, cancellationToken);
                            }
                        }

                        // Periodic progress reporting matching IDM visual style
                        var now = stopwatch.ElapsedMilliseconds;
                        if (now - lastReportTime >= 250)
                        {
                            lastReportTime = now;
                            var currentDownloaded = Interlocked.Read(ref downloadedBytes);
                            var bytesSinceLast = currentDownloaded - lastReportBytes;
                            lastReportBytes = currentDownloaded;

                            double speedBps = (bytesSinceLast / 0.25);
                            var speedFormatted = FormatSpeed(speedBps);
                            double pct = totalBytes > 0 ? Math.Min(100.0, (double)currentDownloaded / totalBytes * 100.0) : 0;

                            long remainingBytes = Math.Max(0, totalBytes - currentDownloaded);
                            string eta = speedBps > 1024 ? FormatEta(remainingBytes / speedBps) : "--:--";

                            progress?.Report(new SegmentProgressEventArgs
                            {
                                OverallPercentage = pct,
                                TotalBytes = totalBytes,
                                DownloadedBytes = currentDownloaded,
                                TransferRateFormatted = speedFormatted,
                                TimeLeftFormatted = eta,
                                StatusMessage = $"Receiving data... ({pct:F1}%)",
                                IsResumeSupported = true,
                                Threads = new List<DownloadConnectionThread>(threads)
                            });
                        }
                    }
                }, cancellationToken));
            }

            try
            {
                await Task.WhenAll(activeWorkers);
            }
            catch (OperationCanceledException)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                return false;
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                return false;
            }

            if (cancellationToken.IsCancellationRequested || downloadedBytes < totalBytes)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                return false;
            }

            // Final rename from temp to destination
            if (File.Exists(destinationPath)) File.Delete(destinationPath);
            File.Move(tempPath, destinationPath);
            PRRX.IDM.Security.SecurityGuard.ApplyMarkOfTheWeb(destinationPath, request.DirectStreamUrl ?? request.Url);

            progress?.Report(new SegmentProgressEventArgs
            {
                OverallPercentage = 100.0,
                TotalBytes = totalBytes,
                DownloadedBytes = totalBytes,
                TransferRateFormatted = "Completed",
                TimeLeftFormatted = "00:00",
                StatusMessage = "Telegram Download Completed Successfully!",
                IsResumeSupported = true,
                Threads = threads
            });

            return true;
        }

        private async Task<byte[]?> FetchTelegramStreamChunkAsync(
            TelegramDownloadRequest request,
            long offset,
            int length,
            CancellationToken cancellationToken)
        {
            // 1. If direct stream URL is available, use standard HTTP Range request
            if (!string.IsNullOrWhiteSpace(request.DirectStreamUrl))
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, request.DirectStreamUrl);
                req.Headers.Range = new RangeHeaderValue(offset, offset + length - 1);
                using var resp = await SharedClient.SendAsync(req, HttpCompletionOption.ResponseContentRead, cancellationToken);
                if (resp.IsSuccessStatusCode)
                {
                    return await resp.Content.ReadAsByteArrayAsync(cancellationToken);
                }
            }

            // 2. Probing Telegram Web CDN stream endpoints for the file
            if (!string.IsNullOrWhiteSpace(request.FileId))
            {
                var candidateUrls = new List<string>
                {
                    $"https://cdn4.telesco.pe/file/{request.FileId}.mp4",
                    $"https://cdn1.telesco.pe/file/{request.FileId}.bin"
                };

                var botToken = await GetBotTokenAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(botToken))
                {
                    candidateUrls.Add($"https://api.telegram.org/file/bot{botToken}/remote/{request.FileId}");
                }

                foreach (var url in candidateUrls)
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, url);
                        req.Headers.Range = new RangeHeaderValue(offset, offset + length - 1);
                        using var resp = await SharedClient.SendAsync(req, HttpCompletionOption.ResponseContentRead, cancellationToken);
                        if (resp.IsSuccessStatusCode)
                        {
                            return await resp.Content.ReadAsByteArrayAsync(cancellationToken);
                        }
                    }
                    catch { }
                }
            }

            // If no endpoint delivered the chunk, return null (never fabricate fake zero-bytes!)
            return null;
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 Bytes";
            string[] sizes = { "Bytes", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond >= 1024 * 1024) return $"{bytesPerSecond / (1024 * 1024):0.00} MB/s";
            if (bytesPerSecond >= 1024) return $"{bytesPerSecond / 1024:0.0} KB/s";
            return $"{bytesPerSecond:0} B/s";
        }

        private static string FormatEta(double seconds)
        {
            if (double.IsInfinity(seconds) || double.IsNaN(seconds) || seconds <= 0) return "--:--";
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        public static string GuessMediaType(string fileName, string? mimeType)
        {
            if (!string.IsNullOrWhiteSpace(mimeType))
            {
                if (mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return "video";
                if (mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return "audio";
                if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return "photo";
            }

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".mp4" or ".mkv" or ".mov" or ".webm" or ".avi" or ".flv" => "video",
                ".mp3" or ".m4a" or ".flac" or ".ogg" or ".wav" or ".aac" => "audio",
                ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" => "photo",
                _ => "document"
            };
        }
    }
}
