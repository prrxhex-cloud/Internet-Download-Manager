// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PRRX.IDM.Models;
using PRRX.IDM.Services;

namespace PRRX.IDM.ViewModels
{
    public class VideoDownloaderViewModel : ViewModelBase
    {
        private readonly IMediaEngineService _mediaEngine;
        private readonly IConfigurationService _configService;
        private readonly IHistoryService _historyService;

        private string _inputUrl = string.Empty;
        private bool _isProbing;
        private bool _isDownloading;
        private string _statusMessage = "Ready. Paste any video link to analyze or download instantly.";
        private bool _hasError;
        private double _downloadProgress;
        private string _downloadSpeed = "0 KB/s";
        private string _downloadEta = "--:--";
        private MediaProbeResult? _currentProbeResult;
        private MediaFormat? _selectedFormat;
        private CancellationTokenSource? _downloadCts;
        private readonly ITelegramLinkResolver _telegramResolver = new TelegramLinkResolver();

        public bool IsTelegramUrl => _telegramResolver.IsTelegramUrl(InputUrl);

        public string InputUrl
        {
            get => _inputUrl;
            set
            {
                if (SetProperty(ref _inputUrl, value))
                {
                    HasError = false;
                    OnPropertyChanged(nameof(IsTelegramUrl));
                }
            }
        }

        public bool IsProbing
        {
            get => _isProbing;
            set => SetProperty(ref _isProbing, value);
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set => SetProperty(ref _isDownloading, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        public double DownloadProgress
        {
            get => _downloadProgress;
            set => SetProperty(ref _downloadProgress, value);
        }

        public string DownloadSpeed
        {
            get => _downloadSpeed;
            set => SetProperty(ref _downloadSpeed, value);
        }

        public string DownloadEta
        {
            get => _downloadEta;
            set => SetProperty(ref _downloadEta, value);
        }

        public MediaProbeResult? CurrentProbeResult
        {
            get => _currentProbeResult;
            set => SetProperty(ref _currentProbeResult, value);
        }

        public MediaFormat? SelectedFormat
        {
            get => _selectedFormat;
            set => SetProperty(ref _selectedFormat, value);
        }

        public ObservableCollection<MediaFormat> AvailableFormats { get; } = new();
        public ObservableCollection<DownloadItem> DownloadHistory => _historyService.HistoryItems;

        public ICommand PasteClipboardCommand { get; }
        public ICommand ProbeUrlCommand { get; }
        public ICommand StartDownloadCommand { get; }
        public ICommand CancelDownloadCommand { get; }
        public ICommand OpenDownloadsFolderCommand { get; }
        public ICommand OpenInspectorCommand { get; }
        public ICommand PlayFileCommand { get; }
        public ICommand ShowInFolderCommand { get; }
        public ICommand DeleteHistoryItemCommand { get; }
        public ICommand ClearAllHistoryCommand { get; }

        public VideoDownloaderViewModel(
            IMediaEngineService mediaEngine, 
            IConfigurationService configService,
            IHistoryService historyService)
        {
            _mediaEngine = mediaEngine;
            _configService = configService;
            _historyService = historyService;

            PopulateDefaultFormats();

            PasteClipboardCommand = new RelayCommand(() =>
            {
                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        InputUrl = text.Trim();
                        if (ProbeUrlCommand != null && ProbeUrlCommand.CanExecute(null))
                        {
                            ProbeUrlCommand.Execute(null);
                        }
                    }
                }
            });

            ProbeUrlCommand = new AsyncRelayCommand(ProbeUrlAsync, () => !IsProbing && !IsDownloading);
            StartDownloadCommand = new AsyncRelayCommand(StartDownloadAsync, () => !IsDownloading);
            CancelDownloadCommand = new RelayCommand(CancelDownload, () => IsDownloading);
            OpenDownloadsFolderCommand = new RelayCommand(OpenDownloadsFolder);

            OpenInspectorCommand = new RelayCommand(() =>
            {
                if (string.IsNullOrWhiteSpace(InputUrl)) return;
                var targetDir = !string.IsNullOrWhiteSpace(_configService?.CurrentConfig.DownloadDirectory)
                    ? _configService.CurrentConfig.DownloadDirectory
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

                var vm = new DownloadFileInfoViewModel(InputUrl, targetDir);
                var dlg = new Views.DownloadFileInfoDialog(vm);
                dlg.Show();
                dlg.Closed += (_, _) =>
                {
                    if (vm.DialogResult == DownloadDialogResult.StartNow)
                    {
                        var activeVm = new ActiveDownloadViewModel(vm.Url, vm.SaveAsFullPath);
                        var activeWin = new Views.ActiveDownloadWindow(activeVm);
                        activeWin.Show();
                    }
                };
            });

            PlayFileCommand = new RelayCommand(param =>
            {
                if (param is DownloadItem item)
                {
                    _historyService.OpenFile(item);
                }
            });

            ShowInFolderCommand = new RelayCommand(param =>
            {
                if (param is DownloadItem item)
                {
                    _historyService.OpenFolder(item);
                }
            });

            DeleteHistoryItemCommand = new RelayCommand(param =>
            {
                if (param is DownloadItem item)
                {
                    _historyService.RemoveItem(item);
                }
            });

            ClearAllHistoryCommand = new RelayCommand(() =>
            {
                _historyService.ClearHistory();
            });
        }

        private void PopulateDefaultFormats()
        {
            AvailableFormats.Clear();
            AvailableFormats.Add(new MediaFormat { FormatId = "bestvideo[height<=1080]+bestaudio/best[height<=1080]/best", Resolution = "1080p Full HD", Extension = "mp4", HasVideo = true, HasAudio = true });
            AvailableFormats.Add(new MediaFormat { FormatId = "bestvideo[height<=720]+bestaudio/best[height<=720]/best", Resolution = "720p HD", Extension = "mp4", HasVideo = true, HasAudio = true });
            AvailableFormats.Add(new MediaFormat { FormatId = "bestvideo[height<=480]+bestaudio/best[height<=480]/best", Resolution = "480p", Extension = "mp4", HasVideo = true, HasAudio = true });
            AvailableFormats.Add(new MediaFormat { FormatId = "bestvideo[height<=360]+bestaudio/best[height<=360]/best", Resolution = "360p", Extension = "mp4", HasVideo = true, HasAudio = true });
            AvailableFormats.Add(new MediaFormat { FormatId = "bestvideo[height<=240]+bestaudio/best[height<=240]/best", Resolution = "240p", Extension = "mp4", HasVideo = true, HasAudio = true });
            AvailableFormats.Add(new MediaFormat { FormatId = "bestvideo[height<=144]+bestaudio/best[height<=144]/best", Resolution = "144p", Extension = "mp4", HasVideo = true, HasAudio = true });
            SelectedFormat = AvailableFormats[0];
        }

        private async Task ProbeUrlAsync()
        {
            if (string.IsNullOrWhiteSpace(InputUrl))
            {
                StatusMessage = "Please enter a valid video link.";
                HasError = true;
                return;
            }

            try
            {
                IsProbing = true;
                HasError = false;
                StatusMessage = "Analyzing media link & extracting rich metadata...";

                var (result, error) = await _mediaEngine.ProbeMediaAsync(InputUrl);

                if (result != null)
                {
                    CurrentProbeResult = result;
                    AvailableFormats.Clear();
                    foreach (var fmt in result.Formats)
                    {
                        AvailableFormats.Add(fmt);
                    }

                    if (AvailableFormats.Count > 0)
                    {
                        SelectedFormat = AvailableFormats[0];
                    }

                    StatusMessage = $"Ready: {result.Title}";
                }
                else
                {
                    HasError = true;
                    StatusMessage = !string.IsNullOrWhiteSpace(error) ? error : "Could not retrieve media details. Direct download is still available.";
                    if (AvailableFormats.Count == 0) PopulateDefaultFormats();
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                StatusMessage = $"Analysis Error: {ex.Message}";
                if (AvailableFormats.Count == 0) PopulateDefaultFormats();
            }
            finally
            {
                IsProbing = false;
            }
        }

        private async Task StartDownloadAsync()
        {
            if (string.IsNullOrWhiteSpace(InputUrl))
            {
                StatusMessage = "Please enter a video link first.";
                HasError = true;
                return;
            }

            try
            {
                IsDownloading = true;
                HasError = false;
                DownloadProgress = 0;
                DownloadSpeed = "0 KB/s";
                DownloadEta = "--:--";
                StatusMessage = "Connecting 16 parallel turbo acceleration streams...";
                _downloadCts = new CancellationTokenSource();

                var progressReporter = new Progress<DownloadProgressReport>(report =>
                {
                    DownloadProgress = report.Percentage;
                    DownloadSpeed = report.Speed;
                    DownloadEta = report.Eta;
                    StatusMessage = report.StatusMessage;
                    if (report.IsError) HasError = true;
                });

                var outputFolder = _configService.CurrentConfig.DownloadDirectory;
                var format = SelectedFormat?.FormatId ?? "bestvideo+bestaudio/best";

                var success = await _mediaEngine.DownloadVideoAsync(
                    InputUrl,
                    format,
                    outputFolder,
                    progressReporter,
                    _downloadCts.Token);

                if (success)
                {
                    DownloadProgress = 100;
                    StatusMessage = "Turbo Download completed successfully!";
                    HasError = false;

                    var item = new DownloadItem
                    {
                        Url = InputUrl,
                        Title = CurrentProbeResult?.Title ?? $"Video_{DateTime.Now:yyyyMMdd_HHmmss}",
                        PublisherName = CurrentProbeResult?.PublisherName ?? "Online Creator",
                        ThumbnailUrl = CurrentProbeResult?.ThumbnailUrl ?? string.Empty,
                        SelectedQuality = SelectedFormat?.Resolution ?? "1080p Full HD",
                        TargetFilePath = outputFolder,
                        Status = DownloadStatus.Completed,
                        FormattedTime = DateTime.Now.ToString("MMM dd, h:mm tt"),
                        CompletedAt = DateTime.Now
                    };

                    _historyService.AddItem(item);
                }
                else
                {
                    if (!HasError)
                    {
                        HasError = true;
                        StatusMessage = "Download failed. Please verify your cookies or check network connection.";
                    }
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Download cancelled by user.";
            }
            catch (Exception ex)
            {
                HasError = true;
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsDownloading = false;
            }
        }

        private void CancelDownload()
        {
            _downloadCts?.Cancel();
        }

        private void OpenDownloadsFolder()
        {
            var folder = _configService.CurrentConfig.DownloadDirectory;
            if (Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
        }
    }
}
