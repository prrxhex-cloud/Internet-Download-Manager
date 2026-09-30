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
    public class SiteGrabberViewModel : ViewModelBase
    {
        private readonly ISiteSpiderGrabberService _spiderService;
        private readonly IScheduleService _scheduleService;
        private readonly IConfigurationService _configService;

        private GrabberProject _currentProject;
        private GrabberFoundFile? _selectedFile;
        private bool _isBusy;
        private bool _isCrawling;
        private bool _isDownloading;
        private bool _isScheduled;
        private string _statusMessage = "Ready to explore website structure and capture media";
        private CancellationTokenSource? _activeCts;

        public GrabberProject CurrentProject
        {
            get => _currentProject;
            set => SetProperty(ref _currentProject, value);
        }

        public ObservableCollection<GrabberFoundFile> FoundFiles { get; } = new();

        public GrabberFoundFile? SelectedFile
        {
            get => _selectedFile;
            set => SetProperty(ref _selectedFile, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public bool IsCrawling
        {
            get => _isCrawling;
            set => SetProperty(ref _isCrawling, value);
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set => SetProperty(ref _isDownloading, value);
        }

        public bool IsScheduled
        {
            get => _isScheduled;
            set => SetProperty(ref _isScheduled, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool HasFoundFiles => FoundFiles.Count > 0;

        public ICommand StartCrawlCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand DownloadSelectedCommand { get; }
        public ICommand DownloadAllCommand { get; }
        public ICommand ScheduleCommand { get; }
        public ICommand UnscheduleCommand { get; }
        public ICommand BrowseDirectoryCommand { get; }
        public ICommand ClearResultsCommand { get; }
        public ICommand OpenTargetFolderCommand { get; }

        public SiteGrabberViewModel(
            ISiteSpiderGrabberService spiderService,
            IScheduleService scheduleService,
            IConfigurationService configService)
        {
            _spiderService = spiderService;
            _scheduleService = scheduleService;
            _configService = configService;

            var defaultDownloadDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                "PRRX_Grabber");

            _currentProject = new GrabberProject
            {
                ProjectName = "Web Project " + DateTime.Now.ToString("yyyyMMdd_HHmm"),
                StartUrl = "https://",
                SaveDirectory = defaultDownloadDir,
                ExplorationDepth = 2,
                FilterPreset = GrabberFilterPreset.AllImages,
                StayOnDomain = true,
                SaveStructure = true,
                ScheduleMode = GrabberScheduleMode.RunImmediately,
                PeriodicHours = 6
            };

            StartCrawlCommand = new RelayCommand(async _ => await ExecuteCrawlAsync(), _ => !IsBusy);
            StopCommand = new RelayCommand(_ => ExecuteStop(), _ => IsBusy);
            DownloadSelectedCommand = new RelayCommand(async _ => await ExecuteDownloadSelectedAsync(), _ => !IsBusy && SelectedFile != null);
            DownloadAllCommand = new RelayCommand(async _ => await ExecuteDownloadAllAsync(), _ => !IsBusy && FoundFiles.Count > 0);
            ScheduleCommand = new RelayCommand(_ => ExecuteSchedule(), _ => !IsScheduled);
            UnscheduleCommand = new RelayCommand(_ => ExecuteUnschedule(), _ => IsScheduled);
            BrowseDirectoryCommand = new RelayCommand(_ => ExecuteBrowseDirectory());
            ClearResultsCommand = new RelayCommand(_ => ExecuteClearResults(), _ => !IsBusy);
            OpenTargetFolderCommand = new RelayCommand(_ => ExecuteOpenTargetFolder());
        }

        private async Task ExecuteCrawlAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentProject.StartUrl) || CurrentProject.StartUrl == "https://")
            {
                StatusMessage = "Please enter a valid start website address.";
                return;
            }

            IsBusy = true;
            IsCrawling = true;
            StatusMessage = "Initiating site spider crawl...";

            _activeCts?.Dispose();
            _activeCts = new CancellationTokenSource();

            var progress = new Progress<string>(msg =>
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusMessage = msg;
                });
            });

            try
            {
                await _spiderService.CrawlWebsiteAsync(
                    CurrentProject,
                    foundFile =>
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            FoundFiles.Add(foundFile);
                            OnPropertyChanged(nameof(HasFoundFiles));
                        });
                    },
                    progress,
                    _activeCts.Token);

                StatusMessage = $"Crawl complete. Discovered {CurrentProject.FilesFound} items.";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Crawl cancelled by user.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Crawl error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                IsCrawling = false;
                MemoryOptimizer.TrimMemory();
            }
        }

        private void ExecuteStop()
        {
            _activeCts?.Cancel();
            StatusMessage = "Stopping task...";
        }

        private async Task ExecuteDownloadAllAsync()
        {
            if (FoundFiles.Count == 0) return;

            IsBusy = true;
            IsDownloading = true;
            StatusMessage = $"Starting download of {FoundFiles.Count} files...";

            _activeCts?.Dispose();
            _activeCts = new CancellationTokenSource();

            try
            {
                await _spiderService.DownloadFilesAsync(
                    CurrentProject,
                    FoundFiles.ToList(),
                    file =>
                    {
                        // Progress callback triggers INPC update in UI
                    },
                    _activeCts.Token);

                StatusMessage = $"Downloads complete. {CurrentProject.FilesDownloaded} files saved.";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Download cancelled by user.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Download error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                IsDownloading = false;
                MemoryOptimizer.TrimMemory();
            }
        }

        private async Task ExecuteDownloadSelectedAsync()
        {
            if (SelectedFile == null) return;

            IsBusy = true;
            IsDownloading = true;
            StatusMessage = $"Downloading {SelectedFile.FileName}...";

            _activeCts?.Dispose();
            _activeCts = new CancellationTokenSource();

            try
            {
                await _spiderService.DownloadFilesAsync(
                    CurrentProject,
                    new[] { SelectedFile },
                    _ => { },
                    _activeCts.Token);

                StatusMessage = $"Downloaded {SelectedFile.FileName}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                IsDownloading = false;
            }
        }

        private void ExecuteSchedule()
        {
            _scheduleService.ScheduleGrabber(CurrentProject, async proj =>
            {
                await _spiderService.CrawlWebsiteAsync(
                    proj,
                    file => Application.Current?.Dispatcher?.Invoke(() => FoundFiles.Add(file)),
                    new Progress<string>(s => StatusMessage = s),
                    CancellationToken.None);

                if (FoundFiles.Count > 0)
                {
                    await _spiderService.DownloadFilesAsync(proj, FoundFiles.ToList(), _ => { }, CancellationToken.None);
                }
            });

            IsScheduled = true;
            StatusMessage = $"Project scheduled ({CurrentProject.ScheduleMode}).";
        }

        private void ExecuteUnschedule()
        {
            _scheduleService.UnscheduleGrabber(CurrentProject.Id);
            IsScheduled = false;
            StatusMessage = "Schedule removed.";
        }

        private void ExecuteBrowseDirectory()
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Folder to Save Grabbed Files",
                InitialDirectory = Directory.Exists(CurrentProject.SaveDirectory) ? CurrentProject.SaveDirectory : ""
            };
            if (dlg.ShowDialog() == true)
            {
                CurrentProject.SaveDirectory = dlg.FolderName;
            }
        }

        private void ExecuteClearResults()
        {
            FoundFiles.Clear();
            CurrentProject.FilesFound = 0;
            CurrentProject.PagesCrawled = 0;
            CurrentProject.FilesDownloaded = 0;
            CurrentProject.TotalBytesDownloaded = 0;
            OnPropertyChanged(nameof(HasFoundFiles));
            StatusMessage = "Results cleared.";
        }

        private void ExecuteOpenTargetFolder()
        {
            try
            {
                if (!Directory.Exists(CurrentProject.SaveDirectory))
                {
                    Directory.CreateDirectory(CurrentProject.SaveDirectory);
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = CurrentProject.SaveDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not open folder: {ex.Message}";
            }
        }
    }
}
