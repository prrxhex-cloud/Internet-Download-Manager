using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PRRX.IDM.Models;

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
            int threadCount = 16,
            CancellationToken cancellationToken = default);

        void Pause();
        void Resume();
        void Cancel();
        void SetSpeedLimit(bool isEnabled, int maxSpeedKbps);
    }

    public class SegmentedDownloadEngine : ISegmentedDownloadEngine
    {
        private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 32,
            EnableMultipleHttp2Connections = true
        }) { Timeout = TimeSpan.FromSeconds(30) };

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
            CancellationToken cancellationToken = default)
        {
            try
            {
                IsRunning = true;
                IsPaused = false;
                _pauseEvent.Set();
                _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _threads.Clear();
                _totalDownloadedBytes = 0;
                _speedStopwatch.Restart();
                _lastBytesMeasured = 0;

                // 1. Probe Head to detect Content-Length and Accept-Ranges
                using var headReq = new HttpRequestMessage(HttpMethod.Head, url);
                headReq.Headers.Add("User-Agent", "PRRX-IDM/1.0");

                HttpResponseMessage headResp;
                try
                {
                    headResp = await HttpClient.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, _cts.Token);
                }
                catch
                {
                    // Fallback to GET with small range
                    using var getReq = new HttpRequestMessage(HttpMethod.Get, url);
                    getReq.Headers.Add("User-Agent", "PRRX-IDM/1.0");
                    headResp = await HttpClient.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead, _cts.Token);
                }

                _totalBytes = headResp.Content.Headers.ContentLength ?? -1;
                bool acceptRanges = headResp.Headers.AcceptRanges.Contains("bytes") || headResp.Content.Headers.ContentRange != null;

                // If server doesn't support ranges or size unknown, single-stream download
                if (!acceptRanges || _totalBytes <= 0)
                {
                    threadCount = 1;
                }

                var targetDir = Path.GetDirectoryName(destinationFilePath);
                if (!string.IsNullOrWhiteSpace(targetDir)) Directory.CreateDirectory(targetDir);

                var tempDir = Path.Combine(targetDir ?? ".", $".prrx_tmp_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                // Initialize Threads and Byte Ranges
                var segmentSize = _totalBytes > 0 ? _totalBytes / threadCount : -1;
                for (int i = 0; i < threadCount; i++)
                {
                    long start = i * segmentSize;
                    long end = (i == threadCount - 1) ? _totalBytes - 1 : (start + segmentSize - 1);

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
                        _cts.Token)));
                }

                await Task.WhenAll(tasks);

                // All segments completed - Assemble parts into final target file
                ReportProgress("Assembling segments into final file...");
                using (var outputStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                {
                    for (int i = 0; i < threadCount; i++)
                    {
                        var partPath = Path.Combine(tempDir, $"part_{i}.tmp");
                        if (File.Exists(partPath))
                        {
                            using var partStream = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
                            await partStream.CopyToAsync(outputStream, _cts.Token);
                        }
                    }
                }

                // Cleanup temp folder
                try { Directory.Delete(tempDir, true); } catch { }

                IsRunning = false;
                _progressTimer?.Dispose();
                ReportProgress("Download Completed!");
                DownloadCompleted?.Invoke(this, destinationFilePath);
                return true;
            }
            catch (OperationCanceledException)
            {
                IsRunning = false;
                _progressTimer?.Dispose();
                ReportProgress("Download Cancelled");
                return false;
            }
            catch (Exception ex)
            {
                IsRunning = false;
                _progressTimer?.Dispose();
                DownloadFailed?.Invoke(this, ex.Message);
                return false;
            }
        }

        private async Task DownloadSegmentWorkerAsync(
            string url,
            DownloadConnectionThread thread,
            string tempPartPath,
            CancellationToken token)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "PRRX-IDM/1.0");

                if (thread.StartByte >= 0 && thread.EndByte >= thread.StartByte)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(thread.StartByte, thread.EndByte);
                }

                thread.StatusInfo = "Send GET...";
                using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();

                thread.StatusInfo = "Receiving data...";
                using var contentStream = await response.Content.ReadAsStreamAsync(token);
                using var fileStream = new FileStream(tempPartPath, FileMode.Create, FileAccess.Write, FileShare.None, 32768, true);

                var buffer = new byte[16384];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    _pauseEvent.Wait(token);

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);
                    thread.DownloadedBytes += bytesRead;
                    thread.CurrentByte += bytesRead;
                    Interlocked.Add(ref _totalDownloadedBytes, bytesRead);

                    long threadTotal = Math.Max(1, thread.EndByte - thread.StartByte + 1);
                    thread.ProgressPercentage = Math.Min(100.0, (thread.DownloadedBytes / (double)threadTotal) * 100.0);
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
                    CurrentByte = t.CurrentByte,
                    DownloadedBytes = t.DownloadedBytes,
                    FormattedDownloaded = t.FormattedDownloaded,
                    StatusInfo = t.StatusInfo,
                    ProgressPercentage = t.ProgressPercentage,
                    IsActive = t.IsActive
                }).ToList()
            });
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
            if (bytes >= 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
            if (bytes >= 1024) return $"{(bytes / 1024.0):F1} KB";
            return $"{bytes} B";
        }
    }
}
