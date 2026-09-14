// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using PRRX.IDM.Models;
using PRRX.IDM.Services;

namespace PRRX.IDM.ViewModels
{
    public class ActiveDownloadViewModel : ViewModelBase
    {
        private readonly ISegmentedDownloadEngine _downloadEngine;
        private readonly ISystemPowerService _powerService;
        private readonly ICloudIntelligenceService _cloudService;

        private string _url = string.Empty;
        private string _fileName = string.Empty;
        private string _destinationFilePath = string.Empty;
        private string _statusText = "Connecting...";
        private string _fileSizeFormatted = "Calculating...";
        private string _downloadedFormatted = "0 KB (0.0%)";
        private string _transferRateFormatted = "0 KB/s";
        private string _timeLeftFormatted = "--:--";
        private string _resumeCapability = "Yes";
        private double _overallPercentage = 0.0;
        private long _totalBytes = 1;
        private bool _isDetailsVisible = true;
        private bool _isPaused = false;
        private bool _isCompleted = false;

        // Speed Limiter
        private bool _useSpeedLimiter = false;
        private int _maxSpeedKbps = 1024;
        private bool _rememberSpeedLimiter = true;

        // Completion Options
        private bool _showCompleteDialog = true;
        private bool _exitWhenDone = false;
        private bool _shutdownWhenDone = false;
        private CompletionAction _selectedShutdownAction = CompletionAction.ShutdownComputer;
        private bool _forceProcesses = false;

        public ObservableCollection<DownloadConnectionThread> ConnectionThreads { get; } = new();

        public string Url { get => _url; set => SetProperty(ref _url, value); }
        public string FileName { get => _fileName; set => SetProperty(ref _fileName, value); }
        public string DestinationFilePath { get => _destinationFilePath; set => SetProperty(ref _destinationFilePath, value); }
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
        public string FileSizeFormatted { get => _fileSizeFormatted; set => SetProperty(ref _fileSizeFormatted, value); }
        public string DownloadedFormatted { get => _downloadedFormatted; set => SetProperty(ref _downloadedFormatted, value); }
        public string TransferRateFormatted { get => _transferRateFormatted; set => SetProperty(ref _transferRateFormatted, value); }
        public string TimeLeftFormatted { get => _timeLeftFormatted; set => SetProperty(ref _timeLeftFormatted, value); }
        public string ResumeCapability { get => _resumeCapability; set => SetProperty(ref _resumeCapability, value); }
        public double OverallPercentage { get => _overallPercentage; set => SetProperty(ref _overallPercentage, value); }
        public long TotalBytes { get => _totalBytes; set => SetProperty(ref _totalBytes, value); }
        
        public bool IsDetailsVisible 
        { 
            get => _isDetailsVisible; 
            set 
            { 
                if (SetProperty(ref _isDetailsVisible, value))
                {
                    OnPropertyChanged(nameof(DetailsToggleText));
                }
            } 
        }

        public string DetailsToggleText => IsDetailsVisible ? "<< Hide details" : "Show details >>";

        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                if (SetProperty(ref _isPaused, value))
                {
                    OnPropertyChanged(nameof(PauseButtonText));
                }
            }
        }

        public string PauseButtonText => IsPaused ? "Resume" : "Pause";
        
        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (SetProperty(ref _isCompleted, value))
                {
                    OnPropertyChanged(nameof(CancelButtonText));
                }
            }
        }

        public string CancelButtonText => IsCompleted ? "Close" : "Cancel";

        // Speed Limiter Bindings
        public bool UseSpeedLimiter
        {
            get => _useSpeedLimiter;
            set
            {
                if (SetProperty(ref _useSpeedLimiter, value))
                {
                    _downloadEngine.SetSpeedLimit(value, MaxSpeedKbps);
                }
            }
        }

        public int MaxSpeedKbps
        {
            get => _maxSpeedKbps;
            set
            {
                if (SetProperty(ref _maxSpeedKbps, value))
                {
                    if (UseSpeedLimiter) _downloadEngine.SetSpeedLimit(true, value);
                }
            }
        }

        public bool RememberSpeedLimiter { get => _rememberSpeedLimiter; set => SetProperty(ref _rememberSpeedLimiter, value); }

        // Completion Options Bindings
        public bool ShowCompleteDialog { get => _showCompleteDialog; set => SetProperty(ref _showCompleteDialog, value); }
        public bool ExitWhenDone { get => _exitWhenDone; set => SetProperty(ref _exitWhenDone, value); }
        public bool ShutdownWhenDone { get => _shutdownWhenDone; set => SetProperty(ref _shutdownWhenDone, value); }
        public CompletionAction SelectedShutdownAction { get => _selectedShutdownAction; set => SetProperty(ref _selectedShutdownAction, value); }
        public bool ForceProcesses { get => _forceProcesses; set => SetProperty(ref _forceProcesses, value); }

        public List<CompletionAction> AvailableShutdownActions { get; } = new()
        {
            CompletionAction.ShutdownComputer,
            CompletionAction.SleepComputer,
            CompletionAction.RestartComputer
        };

        // Commands
        public ICommand ToggleDetailsCommand { get; }
        public ICommand TogglePauseCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand OpenFolderCommand { get; }

        public event Action? RequestClose;

        public ActiveDownloadViewModel(
            string url,
            string destinationFilePath,
            ISegmentedDownloadEngine? downloadEngine = null,
            ISystemPowerService? powerService = null,
            ICloudIntelligenceService? cloudService = null)
        {
            _url = url;
            _destinationFilePath = destinationFilePath;
            _fileName = Path.GetFileName(destinationFilePath);
            _downloadEngine = downloadEngine ?? new SegmentedDownloadEngine();
            _powerService = powerService ?? new SystemPowerService();
            _cloudService = cloudService ?? new CloudIntelligenceService();

            _downloadEngine.ProgressChanged += OnEngineProgressChanged;
            _downloadEngine.DownloadCompleted += OnEngineDownloadCompleted;
            _downloadEngine.DownloadFailed += OnEngineDownloadFailed;

            ToggleDetailsCommand = new RelayCommand(_ => IsDetailsVisible = !IsDetailsVisible);

            TogglePauseCommand = new RelayCommand(_ =>
            {
                if (IsPaused)
                {
                    _downloadEngine.Resume();
                    IsPaused = false;
                }
                else
                {
                    _downloadEngine.Pause();
                    IsPaused = true;
                }
            });

            CancelCommand = new RelayCommand(_ =>
            {
                if (!IsCompleted)
                {
                    _downloadEngine.Cancel();
                }
                RequestClose?.Invoke();
            });

            OpenFileCommand = new RelayCommand(_ =>
            {
                if (File.Exists(DestinationFilePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = DestinationFilePath,
                        UseShellExecute = true
                    });
                }
            });

            OpenFolderCommand = new RelayCommand(_ =>
            {
                var dir = Path.GetDirectoryName(DestinationFilePath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    var fullPath = Path.GetFullPath(DestinationFilePath);
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        UseShellExecute = false
                    };
                    psi.ArgumentList.Add($"/select,{fullPath}");
                    System.Diagnostics.Process.Start(psi);
                }
            });
        }

        public void Start()
        {
            _downloadEngine.StartDownloadAsync(Url, DestinationFilePath, 0);

            // Announce to LAN P2P matchmaker non-blocking
            _ = Task.Run(async () =>
            {
                try
                {
                    var hash = _cloudService.ExtractOrComputeSha256(Url, FileName);
                    if (!string.IsNullOrWhiteSpace(hash))
                    {
                        await _cloudService.AnnounceLanPeerAsync(
                            Environment.MachineName,
                            hash,
                            localLanIp: null,
                            port: 6881,
                            completedChunks: 0,
                            totalChunks: 100);
                    }
                }
                catch { }
            });
        }

        private void OnEngineProgressChanged(object? sender, SegmentProgressEventArgs e)
        {
            DispatchToUi(() =>
            {
                if (IsCompleted) return;

                OverallPercentage = e.OverallPercentage;
                TotalBytes = e.TotalBytes > 0 ? e.TotalBytes : 1;
                FileSizeFormatted = FormatBytes(e.TotalBytes);
                DownloadedFormatted = $"{FormatBytes(e.DownloadedBytes)} ( {e.OverallPercentage:F2} % )";
                TransferRateFormatted = e.TransferRateFormatted;
                TimeLeftFormatted = e.TimeLeftFormatted;
                StatusText = e.StatusMessage;
                ResumeCapability = e.IsResumeSupported ? "Yes" : "No";

                if (ConnectionThreads.Count != e.Threads.Count)
                {
                    ConnectionThreads.Clear();
                    foreach (var t in e.Threads)
                    {
                        ConnectionThreads.Add(t);
                    }
                }
                else
                {
                    for (int i = 0; i < e.Threads.Count; i++)
                    {
                        var src = e.Threads[i];
                        var dst = ConnectionThreads[i];
                        dst.StartByte = src.StartByte;
                        dst.EndByte = src.EndByte;
                        dst.CurrentByte = src.CurrentByte;
                        dst.DownloadedBytes = src.DownloadedBytes;
                        dst.FormattedDownloaded = src.FormattedDownloaded;
                        dst.StatusInfo = src.StatusInfo;
                        dst.ProgressPercentage = src.ProgressPercentage;
                        dst.IsActive = src.IsActive;
                    }
                }
            });
        }

        private void OnEngineDownloadCompleted(object? sender, string finalPath)
        {
            DispatchToUi(() =>
            {
                IsCompleted = true;
                StatusText = "Complete - Downloaded successfully";
                OverallPercentage = 100.0;
                TransferRateFormatted = "Finished";
                TimeLeftFormatted = "00:00";
                DownloadedFormatted = $"{FileSizeFormatted} ( 100.00 % )";

                foreach (var t in ConnectionThreads)
                {
                    t.IsActive = false;
                    t.ProgressPercentage = 100.0;
                    t.StatusInfo = "Complete";
                    if (t.EndByte >= t.StartByte && t.StartByte >= 0)
                    {
                        t.DownloadedBytes = Math.Max(t.DownloadedBytes, t.EndByte - t.StartByte + 1);
                    }
                    t.FormattedDownloaded = FormatBytes(t.DownloadedBytes);
                }

                // Post-download automation check
                if (ShutdownWhenDone)
                {
                    _powerService.ExecuteCompletionAction(SelectedShutdownAction, ForceProcesses);
                }
                else if (ExitWhenDone)
                {
                    _powerService.ExecuteCompletionAction(CompletionAction.ExitApplication);
                }
            });

            // Post-download community reputation reporting (non-blocking)
            _ = Task.Run(async () =>
            {
                try
                {
                    if (File.Exists(finalPath))
                    {
                        var fileHash = await _cloudService.ComputeFileSha256Async(finalPath);
                        if (!string.IsNullOrWhiteSpace(fileHash))
                        {
                            var fi = new FileInfo(finalPath);
                            var name = Path.GetFileName(finalPath);

                            // 1. Authoritative binary SHA-256 report and LAN peer announcement (100% completed)
                            await _cloudService.ReportReputationAsync(fileHash, name, fi.Length, "safe");
                            await _cloudService.AnnounceLanPeerAsync(
                                Environment.MachineName,
                                fileHash,
                                localLanIp: null,
                                port: 6881,
                                completedChunks: 100,
                                totalChunks: 100);

                            // 2. Also register the pre-download URL fingerprint so future queries prior to download match
                            var urlHash = _cloudService.ExtractOrComputeSha256(Url, name);
                            if (!string.IsNullOrWhiteSpace(urlHash) && !string.Equals(urlHash, fileHash, StringComparison.OrdinalIgnoreCase))
                            {
                                await _cloudService.ReportReputationAsync(urlHash, name, fi.Length, "safe");
                                await _cloudService.AnnounceLanPeerAsync(
                                    Environment.MachineName,
                                    urlHash,
                                    localLanIp: null,
                                    port: 6881,
                                    completedChunks: 100,
                                    totalChunks: 100);
                            }
                        }
                    }
                }
                catch
                {
                    // Fail silently - never disrupt user experience
                }
            });
        }

        private void OnEngineDownloadFailed(object? sender, string error)
        {
            DispatchToUi(() =>
            {
                StatusText = $"Error: {error}";
            });
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
