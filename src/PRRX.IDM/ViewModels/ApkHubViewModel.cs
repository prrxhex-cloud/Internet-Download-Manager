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
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PRRX.IDM.Models;
using PRRX.IDM.Services;

namespace PRRX.IDM.ViewModels
{
    public class ApkHubViewModel : ViewModelBase
    {
        private readonly ICloudResolverService _cloudResolver;
        private readonly IConfigurationService _configService;
        private readonly IHistoryService _historyService;

        private string _searchQuery = string.Empty;
        private string _selectedProvider = "All Stores";
        private bool _isSearching;
        private bool _hasResults;
        private string _statusText = "Search millions of verified Android APKs and MODs across top global repositories.";
        private ApkItem? _selectedApk;
        private CancellationTokenSource? _searchCts;

        public string SearchQuery
        {
            get => _searchQuery;
            set => SetProperty(ref _searchQuery, value);
        }

        public string SelectedProvider
        {
            get => _selectedProvider;
            set
            {
                if (SetProperty(ref _selectedProvider, value))
                {
                    if (!string.IsNullOrWhiteSpace(SearchQuery))
                    {
                        _ = PerformSearchAsync();
                    }
                }
            }
        }

        public bool IsSearching
        {
            get => _isSearching;
            set
            {
                if (SetProperty(ref _isSearching, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool HasResults
        {
            get => _hasResults;
            set => SetProperty(ref _hasResults, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public ApkItem? SelectedApk
        {
            get => _selectedApk;
            set => SetProperty(ref _selectedApk, value);
        }

        public ObservableCollection<string> AvailableProviders { get; } = new()
        {
            "All Stores",
            "Ultra Store (v2)",
            "HappyMod MODs",
            "AN1 MODs",
            "APKPure",
            "Uptodown"
        };

        public ObservableCollection<ApkItem> ApkResults { get; } = new();

        public ICommand SearchCommand { get; }
        public ICommand QuickSearchCommand { get; }
        public ICommand DownloadApkCommand { get; }

        public ApkHubViewModel(
            ICloudResolverService? cloudResolver = null,
            IConfigurationService? configService = null,
            IHistoryService? historyService = null)
        {
            _cloudResolver = cloudResolver ?? new CloudResolverService();
            _configService = configService ?? new ConfigurationService();
            _historyService = historyService ?? new HistoryService();

            SearchCommand = new AsyncRelayCommand(PerformSearchAsync, () => !IsSearching);
            QuickSearchCommand = new RelayCommand(param =>
            {
                if (param is string query)
                {
                    SearchQuery = query;
                    _ = PerformSearchAsync();
                }
            });

            DownloadApkCommand = new RelayCommand(param =>
            {
                if (param is ApkItem item)
                {
                    _ = DownloadApkAsync(item);
                }
            });

            // Populate initial trending discovery batch
            SearchQuery = "WhatsApp";
            _ = PerformSearchAsync();
        }

        public async Task PerformSearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) return;

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            IsSearching = true;
            StatusText = $"Searching {SelectedProvider} for '{SearchQuery}'...";

            try
            {
                var providerKey = SelectedProvider switch
                {
                    "Ultra Store (v2)" => "ultra",
                    "HappyMod MODs" => "happymod",
                    "AN1 MODs" => "an1",
                    "APKPure" => "apkpure",
                    "Uptodown" => "uptodown",
                    _ => "all"
                };

                var searchResult = await _cloudResolver.SearchApkAsync(SearchQuery, providerKey, token);

                if (token.IsCancellationRequested) return;

                ApkResults.Clear();
                if (searchResult.Success && searchResult.Items.Count > 0)
                {
                    foreach (var item in searchResult.Items)
                    {
                        ApkResults.Add(item);
                    }
                    HasResults = true;
                    StatusText = $"Found {ApkResults.Count} APK packages for '{SearchQuery}'";
                }
                else
                {
                    HasResults = false;
                    StatusText = searchResult.ErrorMessage ?? "No matching APK packages found. Try another search.";
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled by subsequent search
            }
            catch (Exception ex)
            {
                HasResults = false;
                StatusText = $"Search failed: {ex.Message}";
            }
            finally
            {
                IsSearching = false;
            }
        }

        public async Task DownloadApkAsync(ApkItem? item)
        {
            if (item == null) return;

            var targetDir = !string.IsNullOrWhiteSpace(_configService.CurrentConfig.DownloadDirectory)
                ? _configService.CurrentConfig.DownloadDirectory
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            var rawLink = item.DownloadUrl ?? item.DetailUrl;
            if (string.IsNullOrWhiteSpace(rawLink))
            {
                StatusText = $"Cannot download '{item.Title}': No download URL provided.";
                return;
            }

            StatusText = $"Resolving direct high-speed download for '{item.Title}'...";

            try
            {
                var directUrl = await _cloudResolver.ResolveApkDownloadUrlAsync(rawLink);
                var downloadUrl = !string.IsNullOrWhiteSpace(directUrl) ? directUrl : rawLink;

                var safeTitle = string.Join("_", item.Title.Split(Path.GetInvalidFileNameChars())).Trim();
                if (!safeTitle.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                {
                    safeTitle += ".apk";
                }
                var destPath = Path.Combine(targetDir, safeTitle);

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    var vm = new DownloadFileInfoViewModel(downloadUrl, targetDir, pageTitle: safeTitle, initialFileName: safeTitle);
                    var dlg = new Views.DownloadFileInfoDialog(vm);
                    dlg.Show();
                    dlg.Closed += (_, _) =>
                    {
                        if (vm.DialogResult == DownloadDialogResult.StartNow)
                        {
                            var activeVm = new ActiveDownloadViewModel(
                                vm.Url,
                                vm.SaveAsFullPath,
                                null,
                                null,
                                null,
                                vm.Referer,
                                vm.UserAgent,
                                vm.Cookies,
                                vm.CustomHeaders,
                                _historyService);

                            if (vm.DetectedBytes.HasValue && vm.DetectedBytes.Value > 0)
                            {
                                activeVm.TotalBytes = vm.DetectedBytes.Value;
                                activeVm.FileSizeFormatted = vm.FileSizeFormatted;
                            }

                            var activeWin = new Views.ActiveDownloadWindow(activeVm);
                            activeWin.Show();
                        }
                    };
                });
                StatusText = $"Queued download for '{item.Title}'.";
            }
            catch (Exception ex)
            {
                StatusText = $"Download initiation failed: {ex.Message}";
            }
        }
    }
}
