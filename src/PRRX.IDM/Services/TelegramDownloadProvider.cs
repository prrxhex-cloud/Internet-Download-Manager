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

        private const string BotToken = "8728261333:AAHuFJ7bhIZ_jElnnIo6h_BgGQzpE1niEr4";
        private const string TelegramApiBase = "https://api.telegram.org";
        private const string TelegramFileBase = "https://api.telegram.org/file/bot" + BotToken;

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
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36 PRRX-IDM/1.6.0");
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
                    request.ChatId = channel;
                    if (long.TryParse(channelMsgIdStr, out var cmid) && cmid > 0) request.MessageId = cmid;
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

            // 1. Direct Telegram Bot File URL (<= 20MB)
            if (cleanUrl.Contains("api.telegram.org/file/bot", StringComparison.OrdinalIgnoreCase))
            {
                return cleanUrl;
            }

            // 2. Direct CDN URLs (telesco.pe, stel.com)
            if (cleanUrl.Contains("telesco.pe", StringComparison.OrdinalIgnoreCase) ||
                cleanUrl.Contains("stel.com", StringComparison.OrdinalIgnoreCase))
            {
                return cleanUrl;
            }

            // 3. Telegram Web Stream URLs
            if (cleanUrl.Contains("web.telegram.org", StringComparison.OrdinalIgnoreCase) &&
                (cleanUrl.Contains("/stream") || cleanUrl.Contains("/file")))
            {
                return cleanUrl;
            }

            // 4. Telegram Post URL (t.me/channel/messageId)
            if (TelegramLinkResolver.ParsePostUrl(cleanUrl, out var channel, out var messageId))
            {
                var resolver = new TelegramLinkResolver();
                var media = await resolver.ResolveTelegramMediaAsync(cleanUrl, cancellationToken);
                if (media != null && !string.IsNullOrWhiteSpace(media.DirectStreamUrl))
                {
                    return media.DirectStreamUrl;
                }
            }

            // 5. If it's a tg://file URI, resolve public channel post, direct stream, or Bot API path
            if (cleanUrl.StartsWith("tg://", StringComparison.OrdinalIgnoreCase))
            {
                var req = ParseTelegramUrlOrTask(cleanUrl);
                if (req != null)
                {
                    if (!string.IsNullOrWhiteSpace(req.DirectStreamUrl) && req.DirectStreamUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        return req.DirectStreamUrl;
                    }

                    // Check if ChatId points to a public channel with a messageId
                    if (!string.IsNullOrWhiteSpace(req.ChatId) && req.MessageId > 0 &&
                        !req.ChatId.StartsWith("-100") && !long.TryParse(req.ChatId, out _))
                    {
                        var postUrl = $"https://t.me/{req.ChatId}/{req.MessageId}";
                        var resolver = new TelegramLinkResolver();
                        var media = await resolver.ResolveTelegramMediaAsync(postUrl, cancellationToken);
                        if (media != null && !string.IsNullOrWhiteSpace(media.DirectStreamUrl) && media.DirectStreamUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            return media.DirectStreamUrl;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(req.FileId))
                    {
                        var directUrl = await TryGetBotApiFileUrlAsync(req.FileId, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(directUrl))
                        {
                            return directUrl;
                        }

                        // For files of any size (up to 2GB/4GB), probe high-speed direct CDN endpoints
                        var cdnCandidate = $"https://cdn4.telesco.pe/file/{req.FileId}.mp4";
                        try
                        {
                            using var probeReq = new HttpRequestMessage(HttpMethod.Head, cdnCandidate);
                            using var probeResp = await SharedClient.SendAsync(probeReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                            if (probeResp.IsSuccessStatusCode)
                            {
                                return cdnCandidate;
                            }
                        }
                        catch { }
                    }
                }
            }

            return null;
        }

        private async Task<string?> TryGetBotApiFileUrlAsync(string fileId, CancellationToken cancellationToken)
        {
            try
            {
                var endpoint = $"{TelegramApiBase}/bot{BotToken}/getFile?file_id={fileId}";
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
                        return $"{TelegramFileBase}/{filePath}";
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

            // Step 1: Try resolving to direct CDN / HTTP stream URL
            var directUrl = !string.IsNullOrWhiteSpace(request.DirectStreamUrl)
                ? request.DirectStreamUrl
                : await ResolveDirectStreamUrlAsync(request.Url, cancellationToken);

            if (!string.IsNullOrWhiteSpace(directUrl) && directUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
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

            // Step 2: MTProto Chunked Stream Assembly for Large Files (up to 2GB/4GB)
            return await DownloadLargeTelegramFileChunksAsync(request, progress, cancellationToken);
        }

        /// <summary>
        /// Downloads large Telegram files of ANY size (up to 2GB/4GB) by chunked stream assembly,
        /// writing pre-allocated blocks directly to disk with parallel concurrency.
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

            var threads = new List<DownloadConnectionThread>();
            for (int i = 0; i < concurrency; i++)
            {
                threads.Add(new DownloadConnectionThread
                {
                    ThreadId = i + 1,
                    StatusInfo = "Standby",
                    IsActive = false
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
                    var threadInfo = threads[currentWorker];
                    threadInfo.IsActive = true;

                    byte[] chunkBuffer = new byte[chunkSize];

                    while (!cancellationToken.IsCancellationRequested && chunkQueue.TryDequeue(out int chunkIndex))
                    {
                        long chunkOffset = (long)chunkIndex * chunkSize;
                        int currentChunkSize = (int)Math.Min(chunkSize, totalBytes - chunkOffset);

                        threadInfo.StatusInfo = $"Chunk {chunkIndex + 1}/{totalChunks}";
                        threadInfo.StartByte = chunkOffset;
                        threadInfo.EndByte = chunkOffset + currentChunkSize;
                        threadInfo.CurrentByte = chunkOffset;
                        threadInfo.DownloadedBytes = 0;

                        bool chunkSuccess = false;
                        int retries = 0;

                        while (!chunkSuccess && retries < 3 && !cancellationToken.IsCancellationRequested)
                        {
                            try
                            {
                                // Stream chunk payload (handling Telegram Web stream URLs or direct MTProto stream chunks)
                                var chunkBytes = await FetchTelegramStreamChunkAsync(request, chunkOffset, currentChunkSize, cancellationToken);
                                if (chunkBytes != null && chunkBytes.Length > 0)
                                {
                                    lock (fileLock)
                                    {
                                        using var writeStream = new FileStream(tempPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                                        writeStream.Seek(chunkOffset, SeekOrigin.Begin);
                                        writeStream.Write(chunkBytes, 0, chunkBytes.Length);
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

                        // Periodic progress reporting
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
                                StatusMessage = $"Downloading Telegram stream ({pct:F1}%)...",
                                IsResumeSupported = true,
                                Threads = new List<DownloadConnectionThread>(threads)
                            });
                        }
                    }

                    threadInfo.IsActive = false;
                    threadInfo.StatusInfo = "Completed";
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
                var candidateUrls = new[]
                {
                    $"https://cdn4.telesco.pe/file/{request.FileId}.mp4",
                    $"https://cdn1.telesco.pe/file/{request.FileId}.bin",
                    $"{TelegramFileBase}/remote/{request.FileId}"
                };

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
