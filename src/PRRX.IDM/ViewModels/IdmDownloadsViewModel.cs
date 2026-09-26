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
using System.Linq;
using System.Windows.Input;
using PRRX.IDM.Models;
using PRRX.IDM.Services;

namespace PRRX.IDM.ViewModels
{
    public class IdmDownloadsViewModel : ViewModelBase
    {
        private readonly IConfigurationService _configService;
        private readonly IHistoryService _historyService;
        private readonly IBrowserIntegrationService _browserService;

        private string _browserStatusMessage = "Checking browser extension bridge...";
        private bool _isExtensionRegistered;
        private string _activeHttpPort = "6880";
        private string _searchFilter = string.Empty;

        public ObservableCollection<DownloadItem> BrowserDownloads { get; } = new();

        public string BrowserStatusMessage
        {
            get => _browserStatusMessage;
            set => SetProperty(ref _browserStatusMessage, value);
        }

        public bool IsExtensionRegistered
        {
            get => _isExtensionRegistered;
            set => SetProperty(ref _isExtensionRegistered, value);
        }

        public string ActiveHttpPort
        {
            get => _activeHttpPort;
            set => SetProperty(ref _activeHttpPort, value);
        }

        public string SearchFilter
        {
            get => _searchFilter;
            set
            {
                if (SetProperty(ref _searchFilter, value))
                {
                    RefreshBrowserDownloads();
                }
            }
        }

        public bool EnableBrowserIntegration
        {
            get => _configService.CurrentConfig.EnableBrowserIntegration;
            set
            {
                if (_configService.CurrentConfig.EnableBrowserIntegration != value)
                {
                    _configService.CurrentConfig.EnableBrowserIntegration = value;
                    _configService.SaveConfig();
                    OnPropertyChanged();
                }
            }
        }

        public bool EnableFloatingPanel
        {
            get => _configService.CurrentConfig.EnableFloatingPanel;
            set
            {
                if (_configService.CurrentConfig.EnableFloatingPanel != value)
                {
                    _configService.CurrentConfig.EnableFloatingPanel = value;
                    _configService.SaveConfig();
                    OnPropertyChanged();
                }
            }
        }

        public ICommand RegisterBrowserHostCommand { get; }
        public ICommand OpenExtensionFolderCommand { get; }
        public ICommand RefreshStatusCommand { get; }
        public ICommand PlayFileCommand { get; }
        public ICommand ShowInFolderCommand { get; }
        public ICommand DeleteHistoryItemCommand { get; }
        public ICommand ClearBrowserDownloadsCommand { get; }

        public IdmDownloadsViewModel(
            IConfigurationService configService,
            IHistoryService historyService,
            IBrowserIntegrationService browserService)
        {
            _configService = configService;
            _historyService = historyService;
            _browserService = browserService;

            ActiveHttpPort = _browserService.ActiveHttpPort.ToString();

            // Populate initial filtered list of browser-initiated downloads
            RefreshBrowserDownloads();

            // Hook collection changes so new extension downloads show up live
            if (_historyService.HistoryItems is ObservableCollection<DownloadItem> obs)
            {
                obs.CollectionChanged += (_, _) => RefreshBrowserDownloads();
            }

            CheckExtensionStatus();

            RegisterBrowserHostCommand = new RelayCommand(() =>
            {
                try
                {
                    _browserService.RegisterBrowserHost();
                    CheckExtensionStatus();
                }
                catch (Exception ex)
                {
                    BrowserStatusMessage = $"Registration failed: {ex.Message}";
                }
            });

            OpenExtensionFolderCommand = new RelayCommand(() =>
            {
                try
                {
                    var baseAppDir = AppDomain.CurrentDomain.BaseDirectory;
                    var localExt = Path.Combine(baseAppDir, "extension");
                    var target = Directory.Exists(localExt) ? localExt : baseAppDir;

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = target,
                        UseShellExecute = true
                    });
                }
                catch { }
            });

            RefreshStatusCommand = new RelayCommand(CheckExtensionStatus);

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
                    RefreshBrowserDownloads();
                }
            });

            ClearBrowserDownloadsCommand = new RelayCommand(() =>
            {
                var toRemove = BrowserDownloads.ToList();
                foreach (var item in toRemove)
                {
                    _historyService.RemoveItem(item);
                }
                RefreshBrowserDownloads();
            });
        }

        public void CheckExtensionStatus()
        {
            try
            {
                var manifestExists = _browserService.IsHostRegistered;
                IsExtensionRegistered = manifestExists;

                BrowserStatusMessage = manifestExists
                    ? "✓ IDM Browser Integration Module Active (Chrome, Edge, Brave, Opera connected)"
                    : "⚠ Extension Host not registered. Click 'Register Browser Integration' below.";
            }
            catch (Exception ex)
            {
                BrowserStatusMessage = $"Status check warning: {ex.Message}";
            }
        }

        private void RefreshBrowserDownloads()
        {
            BrowserDownloads.Clear();
            var allItems = _historyService.HistoryItems;

            var filtered = allItems.Where(i =>
            {
                bool isBrowser = i.IsBrowserInitiated ||
                    (!string.IsNullOrWhiteSpace(i.Source) && (
                        i.Source.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
                        i.Source.Contains("Extension", StringComparison.OrdinalIgnoreCase) ||
                        i.Source.Contains("Browser", StringComparison.OrdinalIgnoreCase) ||
                        i.Source.Contains("Grabber", StringComparison.OrdinalIgnoreCase)));

                if (!string.IsNullOrWhiteSpace(_searchFilter))
                {
                    return isBrowser && (
                        (i.Title?.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (i.Url?.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ?? false));
                }
                return isBrowser;
            });

            foreach (var item in filtered)
            {
                BrowserDownloads.Add(item);
            }
        }
    }
}
