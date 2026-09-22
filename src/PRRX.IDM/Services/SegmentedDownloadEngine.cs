// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PRRX.IDM.Models;
using PRRX.IDM.Native;

namespace PRRX.IDM.Services
{
    public class SegmentProgressEventArgs : EventArgs
    {
        public double OverallPercentage { get; set; }
        public long TotalBytes { get; set; }
        public long DownloadedBytes { get; set; }
        public string TransferRateFormatted { get; set; } = "0 KB/s";
        public string TimeLeftFormatted { get; set; } = "--:--";
        public string StatusMessage { get; set; } = "Receiving data...";
        public bool IsResumeSupported { get; set; } = true;
        public List<DownloadConnectionThread> Threads { get; set; } = new();
    }

    public interface ISegmentedDownloadEngine
    {
        event EventHandler<SegmentProgressEventArgs>? ProgressChanged;
        event EventHandler<string>? DownloadCompleted;
        event EventHandler<string>? DownloadFailed;

        bool IsRunning { get; }
        bool IsPaused { get; }
        SpeedLimiterSettings SpeedLimiter { get; }

        Task<bool> StartDownloadAsync(
            string url,
            string destinationFilePath,
            int threadCount = 32,
            CancellationToken cancellationToken = default,
            string? referer = null,
            string? userAgent = null,
            string? cookies = null,
            Dictionary<string, string>? customHeaders = null);

        void Pause();
        void Resume();
        void Cancel();
        void SetSpeedLimit(bool isEnabled, int maxSpeedKbps);
    }

    public class SegmentedDownloadEngine : ISegmentedDownloadEngine
    {
        public const int HighSpeedBufferSize = 1048576; // 1 MB Turbo High-Speed Buffer

        private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 128,
            EnableMultipleHttp2Connections = true,
            InitialHttp2StreamWindowSize = 8 * 1024 * 1024,
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(15)
        }) { Timeout = TimeSpan.FromSeconds(45) };

        public event EventHandler<SegmentProgressEventArgs>? ProgressChanged;
        public event EventHandler<string>? DownloadCompleted;
        public event EventHandler<string>? DownloadFailed;

        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }
        public SpeedLimiterSettings SpeedLimiter { get; } = new();

        private CancellationTokenSource? _cts;
        private ManualResetEventSlim _pauseEvent = new(true);
        private readonly List<DownloadConnectionThread> _threads = new();
        private long _totalBytes;
        private long _totalDownloadedBytes;
        private Stopwatch _speedStopwatch = new();
        private long _lastBytesMeasured;
        private Timer? _progressTimer;

        public void SetSpeedLimit(bool isEnabled, int maxSpeedKbps)
        {
            SpeedLimiter.IsEnabled = isEnabled;
            SpeedLimiter.MaxSpeedKbps = Math.Max(10, maxSpeedKbps);
        }

        public void Pause()
        {
            if (IsRunning && !IsPaused)
            {
                IsPaused = true;
                _pauseEvent.Reset();
                foreach (var t in _threads)
                {
                    if (t.IsActive) t.StatusInfo = "Paused";
                }
                ReportProgress("Paused by user");
            }
        }

        public void Resume()
        {
            if (IsRunning && IsPaused)
            {
                IsPaused = false;
                _pauseEvent.Set();
                foreach (var t in _threads)
                {
                    if (t.IsActive) t.StatusInfo = "Receiving data...";
                }
                ReportProgress("Resuming download...");
            }
        }

        public void Cancel()
        {
            _cts?.Cancel();
            _pauseEvent.Set();
            IsRunning = false;
            IsPaused = false;
            _progressTimer?.Dispose();
        }

        public async Task<bool> StartDownloadAsync(
            string url,
            string destinationFilePath,
            int threadCount = 16,
            CancellationToken cancellationToken = default,
            string? referer = null,
            string? userAgent = null,
            string? cookies = null,
            Dictionary<string, string>? customHeaders = null)
        {
            string? tempDir = null;
            try
            {
                IsRunning = true;
                IsPaused = false;
                _pauseEvent.Set();
                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                // Telegram Provider Integration (Handle Telegram files of ANY size up to 4GB)
                if (TelegramDownloadProvider.Current.CanHandle(url))
                {
                    var tgReq = TelegramDownloadProvider.Current.ParseTelegramUrlOrTask(url, Path.GetFileName(destinationFilePath));
                    if (tgReq != null)
                    {
                        tgReq.DestinationFilePath = destinationFilePath;

                        if (url.StartsWith("tg://", StringComparison.OrdinalIgnoreCase))
                        {
                            var progressReporter = new Progress<SegmentProgressEventArgs>(args =>
                            {
                                ProgressChanged?.Invoke(this, args);
                            });

                            bool success = await TelegramDownloadProvider.Current.DownloadAsync(tgReq, progressReporter, _cts.Token);
                            IsRunning = false;
                            if (success)
                            {
                                PRRX.IDM.Security.SecurityGuard.ApplyMarkOfTheWeb(destinationFilePath, url);
                                DownloadCompleted?.Invoke(this, destinationFilePath);
                                return true;
                            }
                            else
                            {
                                DownloadFailed?.Invoke(this, "Telegram stream download cancelled or failed: stream endpoint not reachable.");
                                return false;
                            }
                        }

                        var resolvedStream = await TelegramDownloadProvider.Current.ResolveDirectStreamUrlAsync(url, _cts.Token);
                        if (!string.IsNullOrWhiteSpace(resolvedStream) && TelegramLinkResolver.IsValidDirectStreamUrl(resolvedStream))
                        {
                            url = resolvedStream;
                        }
                    }
                }

                _threads.Clear();
                _totalDownloadedBytes = 0;
                _speedStopwatch.Restart();
                _lastBytesMeasured = 0;

                _totalBytes = -1;
                bool acceptRanges = false;

                // 1. Probe HEAD to detect Content-Length and Accept-Ranges (with fast 2.5s timeout)
                try
                {
                    using var headCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                    headCts.CancelAfter(TimeSpan.FromSeconds(2.5));

                    using var headReq = new HttpRequestMessage(HttpMethod.Head, url);
                    ApplyStandardHeaders(headReq, url, referer, userAgent, cookies, customHeaders);

                    using var headResp = await HttpClient.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, headCts.Token);
                    if (headResp.IsSuccessStatusCode)
                    {
                        _totalBytes = headResp.Content.Headers.ContentLength ?? -1;
                        acceptRanges = headResp.Headers.AcceptRanges.Contains("bytes") || headResp.Content.Headers.ContentRange != null;
                    }
                }
                catch
                {
                    // Fallback to GET with Range immediately
                }

                // 2. If HEAD failed, returned non-2xx, or didn't yield size/ranges: probe with GET Range: bytes=0-0 (with fast 3s timeout)
                if ((_totalBytes <= 0 || !acceptRanges) && !_cts.IsCancellationRequested)
                {
                    try
                    {
                        using var getCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                        getCts.CancelAfter(TimeSpan.FromSeconds(3.0));

                        using var getReq = new HttpRequestMessage(HttpMethod.Get, url);
                        ApplyStandardHeaders(getReq, url, referer, userAgent, cookies, customHeaders);
                        getReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);

                        using var getResp = await HttpClient.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead, getCts.Token);
                        if (getResp.StatusCode == System.Net.HttpStatusCode.PartialContent)
                        {
                            acceptRanges = true;
                            if (getResp.Content.Headers.ContentRange?.Length.HasValue == true)
                            {
                                _totalBytes = getResp.Content.Headers.ContentRange.Length.Value;
                            }
                            else if (getResp.Content.Headers.TryGetValues("Content-Range", out var crVals))
                            {
                                foreach (var val in crVals)
                                {
                                    var slashIdx = val.LastIndexOf('/');
                                    if (slashIdx >= 0 && long.TryParse(val.AsSpan(slashIdx + 1).Trim(), out var total))
                                    {
                                        _totalBytes = total;
                                        break;
                                    }
                                }
                            }
                        }
                        else if (getResp.StatusCode == System.Net.HttpStatusCode.OK && getResp.Content.Headers.ContentLength.HasValue)
                        {
                            _totalBytes = getResp.Content.Headers.ContentLength.Value;
                            acceptRanges = false;
                        }
                    }
                    catch
                    {
                    }
                }

                // Maximize download throughput: adaptive high-speed multi-socket pooling (up to 32–64 parallel streams)
                threadCount = MultiSegmentDownloader.CalculateOptimalConcurrency(_totalBytes, threadCount);

                // If server doesn't support ranges or size unknown, single-stream download
                if (!acceptRanges || _totalBytes <= 0)
                {
                    threadCount = 1;
                }

                var targetDir = Path.GetDirectoryName(destinationFilePath);
                if (!string.IsNullOrWhiteSpace(targetDir)) Directory.CreateDirectory(targetDir);

                tempDir = Path.Combine(targetDir ?? ".", $".prrx_tmp_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                // Initialize Threads and Byte Ranges
                var segmentSize = _totalBytes > 0 ? Math.Max(1, _totalBytes / threadCount) : -1;
                for (int i = 0; i < threadCount; i++)
                {
                    long start = _totalBytes > 0 ? (i * segmentSize) : 0;
                    long end = _totalBytes > 0 ? ((i == threadCount - 1) ? _totalBytes - 1 : (start + segmentSize - 1)) : -1;

                    _threads.Add(new DownloadConnectionThread
                    {
                        ThreadId = i + 1,
                        StartByte = start,
                        EndByte = end,
                        CurrentByte = start,
                        DownloadedBytes = 0,
                        StatusInfo = "Connecting...",
                        ProgressPercentage = 0,
                        IsActive = true
                    });
                }

                // Start live 100ms UI ticker
                _progressTimer = new Timer(_ =>
                {
                    if (IsRunning && !IsPaused)
                    {
                        ReportProgress("Receiving data...");
                    }
                }, null, 100, 100);

                // Launch parallel segment download workers
                var tasks = new List<Task>();
                for (int i = 0; i < threadCount; i++)
                {
                    int index = i;
                    tasks.Add(Task.Run(() => DownloadSegmentWorkerAsync(
                        url,
                        _threads[index],
                        Path.Combine(tempDir, $"part_{index}.tmp"),
                        _cts.Token,
                        referer,
                        userAgent,
                        cookies,
                        customHeaders)));
                }

                await Task.WhenAll(tasks);

                // Dispose live progress timer before assembly to avoid overwriting assembly status
                _progressTimer?.Dispose();
                _progressTimer = null;

                // All segments completed - Assemble parts into final target file with fast Win32 pre-allocation
                ReportProgress("Assembling segments into final file...");
                using (var outputStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, HighSpeedBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    if (_totalBytes > 0)
                    {
                        // Pre-allocate sparse disk space to eliminate file-allocation stalls and fragmentation
                        NativeFileHelper.FastPreallocate(outputStream, _totalBytes);
                    }

                    var copyBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(HighSpeedBufferSize); // 1 MB high-throughput pooled buffer
                    try
                    {
                        for (int i = 0; i < threadCount; i++)
                        {
                            var partPath = Path.Combine(tempDir, $"part_{i}.tmp");
                            if (File.Exists(partPath))
                            {
                                using (var partStream = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read, HighSpeedBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                                {
                                    int read;
                                    while ((read = await partStream.ReadAsync(copyBuffer.AsMemory(0, HighSpeedBufferSize), _cts.Token)) > 0)
                                    {
                                        await outputStream.WriteAsync(copyBuffer.AsMemory(0, read), _cts.Token);
                                    }
                                }
                                try { File.Delete(partPath); } catch { } // Free disk space immediately
                            }
                        }
                        if (outputStream.Position != outputStream.Length)
                        {
                            outputStream.SetLength(outputStream.Position);
                        }
                        await outputStream.FlushAsync(_cts.Token);
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<byte>.Shared.Return(copyBuffer);
                    }
                }

                IsRunning = false;
                ReportProgress("Complete - Downloaded successfully");
                PRRX.IDM.Security.SecurityGuard.ApplyMarkOfTheWeb(destinationFilePath, url, referer);
                DownloadCompleted?.Invoke(this, destinationFilePath);
                MemoryOptimizer.TrimMemory();
                return true;
            }
            catch (OperationCanceledException)
            {
                IsRunning = false;
                ReportProgress("Download Cancelled");
                return false;
            }
            catch (Exception ex)
            {
                IsRunning = false;
                DownloadFailed?.Invoke(this, ex.Message);
                return false;
            }
            finally
            {
                _progressTimer?.Dispose();
                _progressTimer = null;
                if (!string.IsNullOrWhiteSpace(tempDir) && Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        private async Task DownloadSegmentWorkerAsync(
            string url,
            DownloadConnectionThread thread,
            string tempPartPath,
            CancellationToken token,
            string? referer = null,
            string? userAgent = null,
            string? cookies = null,
            Dictionary<string, string>? customHeaders = null)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyStandardHeaders(request, url, referer, userAgent, cookies, customHeaders);

                if (thread.StartByte >= 0 && thread.EndByte >= thread.StartByte)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(thread.StartByte, thread.EndByte);
                }

                thread.StatusInfo = "Send GET...";
                using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();

                // If total file size was not resolved during initial probing, capture it now from the actual GET response
                if (_totalBytes <= 0)
                {
                    if (response.Content.Headers.ContentRange?.Length.HasValue == true)
                    {
                        _totalBytes = response.Content.Headers.ContentRange.Length.Value;
                    }
                    else if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > 0)
                    {
                        _totalBytes = response.Content.Headers.ContentLength.Value;
                    }

                    if (_totalBytes > 0 && (thread.EndByte < thread.StartByte || thread.EndByte <= 0))
                    {
                        thread.EndByte = _totalBytes - 1;
                    }
                }

                thread.StatusInfo = "Receiving data...";
                using var contentStream = await response.Content.ReadAsStreamAsync(token);
                using var fileStream = new FileStream(tempPartPath, FileMode.Create, FileAccess.Write, FileShare.None, HighSpeedBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

                var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(HighSpeedBufferSize);
                try
                {
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, HighSpeedBufferSize), token)) > 0)
                    {
                        _pauseEvent.Wait(token);

                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
                        thread.DownloadedBytes += bytesRead;
                        thread.CurrentByte += bytesRead;
                        Interlocked.Add(ref _totalDownloadedBytes, bytesRead);

                        long threadTotal = (thread.EndByte >= thread.StartByte && thread.StartByte >= 0)
                            ? (thread.EndByte - thread.StartByte + 1)
                            : -1;

                        if (threadTotal > 0)
                        {
                            thread.ProgressPercentage = Math.Min(100.0, (thread.DownloadedBytes / (double)threadTotal) * 100.0);
                        }
                        else
                        {
                            thread.ProgressPercentage = 0.0;
                        }
                        thread.FormattedDownloaded = FormatBytes(thread.DownloadedBytes);

                        // Speed limiter throttling (delay injection per block)
                        if (SpeedLimiter.IsEnabled && SpeedLimiter.MaxSpeedKbps > 0)
                        {
                            var maxBytesPerSec = SpeedLimiter.MaxSpeedKbps * 1024L;
                            var targetDelayMs = (bytesRead * 1000L) / Math.Max(1000L, maxBytesPerSec);
                            if (targetDelayMs > 0)
                            {
                                await Task.Delay((int)targetDelayMs, token);
                            }
                        }
                    }
                    await fileStream.FlushAsync(token);
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                }

                thread.StatusInfo = "Segment Merged";
                thread.ProgressPercentage = 100.0;
                thread.IsActive = false;
            }
            catch (Exception ex)
            {
                thread.StatusInfo = token.IsCancellationRequested ? "Cancelled" : $"Error: {ex.Message}";
                thread.IsActive = false;
                throw;
            }
        }

        private void ReportProgress(string statusMsg)
        {
            var elapsedSec = Math.Max(0.1, _speedStopwatch.Elapsed.TotalSeconds);
            var currentBytes = Interlocked.Read(ref _totalDownloadedBytes);
            var bytesDelta = currentBytes - _lastBytesMeasured;
            _lastBytesMeasured = currentBytes;

            var bytesPerSec = bytesDelta / 0.1; // 100ms interval
            var speedFormatted = $"{FormatBytes((long)bytesPerSec)}/s";

            double percent = _totalBytes > 0 ? (currentBytes / (double)_totalBytes) * 100.0 : 0.0;
            percent = Math.Clamp(percent, 0.0, 100.0);

            // Calculate ETA
            string eta = "--:--";
            if (bytesPerSec > 0 && _totalBytes > currentBytes)
            {
                var remainingSec = (_totalBytes - currentBytes) / bytesPerSec;
                var ts = TimeSpan.FromSeconds(remainingSec);
                eta = ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}" : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
            }

            bool isCompletedStatus = !IsRunning && statusMsg.StartsWith("Complete");
            if (isCompletedStatus)
            {
                currentBytes = _totalBytes > 0 ? _totalBytes : currentBytes;
                percent = 100.0;
                speedFormatted = "Finished";
                eta = "00:00";
            }

            ProgressChanged?.Invoke(this, new SegmentProgressEventArgs
            {
                OverallPercentage = percent,
                TotalBytes = _totalBytes,
                DownloadedBytes = currentBytes,
                TransferRateFormatted = speedFormatted,
                TimeLeftFormatted = eta,
                StatusMessage = statusMsg,
                IsResumeSupported = true,
                Threads = _threads.Select(t => new DownloadConnectionThread
                {
                    ThreadId = t.ThreadId,
                    StartByte = t.StartByte,
                    EndByte = t.EndByte,
                    CurrentByte = isCompletedStatus ? (t.EndByte >= t.StartByte && t.StartByte >= 0 ? t.EndByte + 1 : t.CurrentByte) : t.CurrentByte,
                    DownloadedBytes = isCompletedStatus ? (t.EndByte >= t.StartByte && t.StartByte >= 0 ? Math.Max(t.DownloadedBytes, t.EndByte - t.StartByte + 1) : t.DownloadedBytes) : t.DownloadedBytes,
                    FormattedDownloaded = isCompletedStatus ? FormatBytes(t.EndByte >= t.StartByte && t.StartByte >= 0 ? Math.Max(t.DownloadedBytes, t.EndByte - t.StartByte + 1) : t.DownloadedBytes) : t.FormattedDownloaded,
                    StatusInfo = isCompletedStatus ? "Complete" : t.StatusInfo,
                    ProgressPercentage = isCompletedStatus ? 100.0 : t.ProgressPercentage,
                    IsActive = !isCompletedStatus && t.IsActive
                }).ToList()
            });
        }

        public static void ApplyStandardHeaders(
            HttpRequestMessage request,
            string url,
            string? referer = null,
            string? userAgent = null,
            string? cookies = null,
            Dictionary<string, string>? customHeaders = null)
        {
            var ua = !string.IsNullOrWhiteSpace(userAgent)
                ? userAgent
                : "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
            request.Headers.TryAddWithoutValidation("User-Agent", ua);
            request.Headers.TryAddWithoutValidation("Accept", "*/*");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua", "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\", \"Not-A.Brand\";v=\"99\"");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");

            // Smart Referer Attachment:
            // If explicit referer is provided, use it.
            // If none provided and target is cv-cloud.top (anti-hotlink CDN), use https://cinevibes.lk/
            // Otherwise default to the origin of the target URL to bypass common origin/hotlink filters.
            if (!string.IsNullOrWhiteSpace(referer))
            {
                request.Headers.TryAddWithoutValidation("Referer", referer);
            }
            else if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                if (uri.Host.Contains("cv-cloud", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.TryAddWithoutValidation("Referer", "https://cinevibes.lk/");
                }
                else
                {
                    request.Headers.TryAddWithoutValidation("Referer", $"https://{uri.Host}/");
                }
            }

            // Session Cookies attachment
            if (!string.IsNullOrWhiteSpace(cookies))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookies);
            }

            // Custom headers
            if (customHeaders != null)
            {
                foreach (var kvp in customHeaders)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                    {
                        request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                    }
                }
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            if (bytes >= 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
            if (bytes >= 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
            if (bytes >= 1024) return $"{(bytes / 1024.0):F1} KB";
            return $"{bytes} B";
        }
    }
}
