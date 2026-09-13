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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PRRX.IDM.Models;
using PRRX.IDM.Services;

namespace PRRX.IDM.ViewModels
{
    public class ThumbnailViewModel : ViewModelBase
    {
        private readonly IThumbnailService _thumbnailService;
        private readonly IConfigurationService _configService;

        private string _inputUrl = string.Empty;
        private string _currentVideoId = string.Empty;
        private bool _isLoading;
        private string _statusMessage = "Enter any YouTube video link to extract high-resolution artwork & thumbnails.";
        private BitmapImage? _previewImage;
        private ThumbnailItem? _selectedThumbnail;

        public string InputUrl
        {
            get => _inputUrl;
            set => SetProperty(ref _inputUrl, value);
        }

        public string CurrentVideoId
        {
            get => _currentVideoId;
            set => SetProperty(ref _currentVideoId, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public BitmapImage? PreviewImage
        {
            get => _previewImage;
            set => SetProperty(ref _previewImage, value);
        }

        public ThumbnailItem? SelectedThumbnail
        {
            get => _selectedThumbnail;
            set
            {
                if (SetProperty(ref _selectedThumbnail, value) && value != null)
                {
                    _ = LoadPreviewAsync(value.Url);
                }
            }
        }

        public ObservableCollection<ThumbnailItem> AvailableThumbnails { get; } = new();

        public ICommand PasteClipboardCommand { get; }
        public ICommand ExtractThumbnailsCommand { get; }
        public ICommand DownloadThumbnailCommand { get; }
        public ICommand DownloadSpecificThumbnailCommand { get; }
        public ICommand CopyToClipboardCommand { get; }
        public ICommand CopyUrlCommand { get; }
        public ICommand OpenOutputFolderCommand { get; }

        public ThumbnailViewModel(IThumbnailService thumbnailService, IConfigurationService configService)
        {
            _thumbnailService = thumbnailService;
            _configService = configService;

            PasteClipboardCommand = new RelayCommand(() =>
            {
                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        InputUrl = text.Trim();
                        ExtractThumbnailsCommand?.Execute(null);
                    }
                }
            });

            ExtractThumbnailsCommand = new AsyncRelayCommand(ExtractThumbnailsAsync, () => !IsLoading);
            DownloadThumbnailCommand = new AsyncRelayCommand(DownloadThumbnailAsync, () => !IsLoading && SelectedThumbnail != null);
            
            DownloadSpecificThumbnailCommand = new AsyncRelayCommand(async param =>
            {
                if (param is ThumbnailItem item)
                {
                    await DownloadItemAsync(item);
                }
            });

            CopyUrlCommand = new RelayCommand(param =>
            {
                if (param is ThumbnailItem item && !string.IsNullOrWhiteSpace(item.Url))
                {
                    Clipboard.SetText(item.Url);
                    StatusMessage = $"Direct image URL copied to clipboard!";
                }
            });

            CopyToClipboardCommand = new RelayCommand(CopyImageToClipboard, () => PreviewImage != null);
            OpenOutputFolderCommand = new RelayCommand(OpenOutputFolder);
        }

        private async Task ExtractThumbnailsAsync()
        {
            await Task.Yield();

            if (string.IsNullOrWhiteSpace(InputUrl))
            {
                StatusMessage = "Please enter a valid YouTube video link.";
                return;
            }

            var videoId = _thumbnailService.ExtractYouTubeVideoId(InputUrl);
            if (string.IsNullOrWhiteSpace(videoId))
            {
                StatusMessage = "Invalid YouTube URL format. Could not detect Video ID.";
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = "Extracting maximum-resolution thumbnail streams...";
                CurrentVideoId = videoId;
                AvailableThumbnails.Clear();
                PreviewImage = null;

                var items = _thumbnailService.GetThumbnailResolutions(videoId);
                foreach (var item in items)
                {
                    AvailableThumbnails.Add(item);
                }

                if (AvailableThumbnails.Count > 0)
                {
                    SelectedThumbnail = AvailableThumbnails[0]; // Maximum Resolution
                    StatusMessage = $"Extracted {AvailableThumbnails.Count} resolutions for Video ID: {videoId}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadPreviewAsync(string url)
        {
            try
            {
                IsLoading = true;
                var bitmap = await _thumbnailService.LoadThumbnailImageAsync(url);
                if (bitmap != null)
                {
                    PreviewImage = bitmap;
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task DownloadThumbnailAsync()
        {
            if (SelectedThumbnail != null)
            {
                await DownloadItemAsync(SelectedThumbnail);
            }
        }

        private async Task DownloadItemAsync(ThumbnailItem item)
        {
            try
            {
                IsLoading = true;
                StatusMessage = $"Downloading {item.ResolutionTitle}...";
                var targetFolder = _configService.CurrentConfig.DownloadDirectory;

                var savedPath = await _thumbnailService.SaveThumbnailAsync(
                    item.Url,
                    CurrentVideoId,
                    item.ResolutionTitle,
                    targetFolder);

                StatusMessage = $"Saved: {Path.GetFileName(savedPath)} to Downloads folder!";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Download failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void CopyImageToClipboard()
        {
            if (PreviewImage != null)
            {
                _thumbnailService.CopyThumbnailToClipboard(PreviewImage);
                StatusMessage = "HD Thumbnail image copied to clipboard!";
            }
        }

        private void OpenOutputFolder()
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

        public void UnloadResources()
        {
            PreviewImage = null;
        }
    }
}
