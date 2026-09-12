using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using PRRX.IDM.Models;
using PRRX.IDM.Services;
using PRRX.IDM.Views;

namespace PRRX.IDM.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly IConfigurationService _configService;
        private readonly IThemeService _themeService;
        private readonly IUpdateService _updateService;
        private readonly IMediaEngineService _mediaEngine;

        private AppThemeMode _selectedTheme;
        private double _transparencyFactor;
        private string _downloadDirectory;
        private string _cookiesFilePath;
        private bool _isCookiesLoaded;
        private string _cookiesStatusDisplay = string.Empty;
        private bool _enableTurboAcceleration;
        private int _turboConnectionCount;
        private bool _isWin11;
        private string _appVersion;
        private string _updateStatusMessage = "Ready to query GitHub Releases API.";
        private bool _isCheckingUpdates;
        private bool _isUpdateAvailable;
        private UpdateManifest? _latestManifest;

        private bool _isUpdatingEngine;
        private string _engineStatusMessage = "Ready. Click to verify latest online extraction algorithms.";

        public AppThemeMode SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (SetProperty(ref _selectedTheme, value))
                {
                    _themeService.ApplyTheme(value);
                }
            }
        }

        public double TransparencyFactor
        {
            get => _transparencyFactor;
            set
            {
                if (SetProperty(ref _transparencyFactor, value))
                {
                    _themeService.SetTransparency(value);
                }
            }
        }

        public string DownloadDirectory
        {
            get => _downloadDirectory;
            set
            {
                if (SetProperty(ref _downloadDirectory, value))
                {
                    _configService.CurrentConfig.DownloadDirectory = value;
                    _configService.SaveConfig();
                }
            }
        }

        public string CookiesFilePath
        {
            get => _cookiesFilePath;
            set
            {
                if (SetProperty(ref _cookiesFilePath, value))
                {
                    _configService.CurrentConfig.CookiesFilePath = value;
                    _configService.CurrentConfig.IsCookiesEnabled = !string.IsNullOrWhiteSpace(value);
                    _configService.SaveConfig();
                    UpdateCookiesStatus();
                }
            }
        }

        public bool IsCookiesLoaded
        {
            get => _isCookiesLoaded;
            set => SetProperty(ref _isCookiesLoaded, value);
        }

        public string CookiesStatusDisplay
        {
            get => _cookiesStatusDisplay;
            set => SetProperty(ref _cookiesStatusDisplay, value);
        }

        public bool EnableTurboAcceleration
        {
            get => _enableTurboAcceleration;
            set
            {
                if (SetProperty(ref _enableTurboAcceleration, value))
                {
                    _configService.CurrentConfig.EnableTurboAcceleration = value;
                    _configService.SaveConfig();
                }
            }
        }

        public int TurboConnectionCount
        {
            get => _turboConnectionCount;
            set
            {
                if (SetProperty(ref _turboConnectionCount, value))
                {
                    _configService.CurrentConfig.TurboConnectionCount = value;
                    _configService.SaveConfig();
                }
            }
        }

        public ObservableCollection<int> AvailableStreamCounts { get; } = new()
        {
            4,
            8,
            16,
            24,
            32
        };

        public bool IsWin11
        {
            get => _isWin11;
            set => SetProperty(ref _isWin11, value);
        }

        public string AppVersion
        {
            get => _appVersion;
            set => SetProperty(ref _appVersion, value);
        }

        public string UpdateStatusMessage
        {
            get => _updateStatusMessage;
            set => SetProperty(ref _updateStatusMessage, value);
        }

        public bool IsCheckingUpdates
        {
            get => _isCheckingUpdates;
            set => SetProperty(ref _isCheckingUpdates, value);
        }

        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set => SetProperty(ref _isUpdateAvailable, value);
        }

        public bool IsUpdatingEngine
        {
            get => _isUpdatingEngine;
            set => SetProperty(ref _isUpdatingEngine, value);
        }

        public string EngineStatusMessage
        {
            get => _engineStatusMessage;
            set => SetProperty(ref _engineStatusMessage, value);
        }

        private string _browserStatusMessage = "Ready to install / link Chrome & Edge extension.";
        public string BrowserStatusMessage
        {
            get => _browserStatusMessage;
            set => SetProperty(ref _browserStatusMessage, value);
        }

        public ICommand BrowseFolderCommand { get; }
        public ICommand BrowseCookiesCommand { get; }
        public ICommand ClearCookiesCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand OpenLatestReleaseCommand { get; }
        public ICommand UpdateEngineCommand { get; }
        public ICommand SetThemeCommand { get; }
        public ICommand ReplayQuickTourCommand { get; }
        public ICommand ReplayOnboardingCommand { get; }
        public ICommand InstallBrowserExtensionCommand { get; }
        public ICommand OpenExtensionFolderCommand { get; }

        public SettingsViewModel(
            IConfigurationService configService,
            IThemeService themeService,
            IUpdateService updateService,
            IMediaEngineService mediaEngine)
        {
            _configService = configService;
            _themeService = themeService;
            _updateService = updateService;
            _mediaEngine = mediaEngine;

            _selectedTheme = _configService.CurrentConfig.ThemeMode;
            _transparencyFactor = _configService.CurrentConfig.TransparencyFactor;
            _downloadDirectory = _configService.CurrentConfig.DownloadDirectory;
            _cookiesFilePath = _configService.CurrentConfig.CookiesFilePath;
            _enableTurboAcceleration = _configService.CurrentConfig.EnableTurboAcceleration;
            _turboConnectionCount = _configService.CurrentConfig.TurboConnectionCount;
            _isWin11 = _themeService.IsWindows11;
            _appVersion = "v1.2.0 (Official Release)";

            UpdateCookiesStatus();

            BrowseFolderCommand = new RelayCommand(() =>
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "Choose Default Download Directory",
                    InitialDirectory = Directory.Exists(DownloadDirectory) ? DownloadDirectory : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                };

                if (dialog.ShowDialog() == true)
                {
                    DownloadDirectory = dialog.FolderName;
                }
            });

            BrowseCookiesCommand = new RelayCommand(() =>
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Select Netscape YouTube cookies.txt file",
                    Filter = "Cookie Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                    InitialDirectory = Directory.Exists(@"D:\Internet Download Manager") ? @"D:\Internet Download Manager" : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                };

                if (dialog.ShowDialog() == true)
                {
                    _configService.CurrentConfig.IsCookiesEnabled = true;
                    CookiesFilePath = dialog.FileName;
                }
            });

            ClearCookiesCommand = new RelayCommand(() =>
            {
                // Deactivate and remove any cached cookies
                _configService.CurrentConfig.IsCookiesEnabled = false;
                _configService.CurrentConfig.CookiesFilePath = string.Empty;
                _configService.SaveConfig();
                _cookiesFilePath = string.Empty;
                OnPropertyChanged(nameof(CookiesFilePath));

                try
                {
                    var p1 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cookies.txt");
                    if (File.Exists(p1)) File.Delete(p1);

                    var p2 = @"D:\Internet Download Manager\cookies.txt";
                    if (File.Exists(p2)) File.Delete(p2);

                    var p3 = @"D:\Internet Download Manager\publish\cookies.txt";
                    if (File.Exists(p3)) File.Delete(p3);
                }
                catch
                {
                    // Ignore background deletion errors
                }

                UpdateCookiesStatus();
            });

            CheckForUpdatesCommand = new AsyncRelayCommand(CheckGitHubUpdatesAsync, () => !IsCheckingUpdates);

            OpenLatestReleaseCommand = new RelayCommand(() =>
            {
                if (_latestManifest != null && !string.IsNullOrWhiteSpace(_latestManifest.DownloadUrl))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = _latestManifest.DownloadUrl,
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                        // Fallback
                    }
                }
            });

            UpdateEngineCommand = new AsyncRelayCommand(UpdateEngineAsync, () => !IsUpdatingEngine);

            SetThemeCommand = new RelayCommand(param =>
            {
                if (param is string themeStr && Enum.TryParse<AppThemeMode>(themeStr, true, out var mode))
                {
                    SelectedTheme = mode;
                }
            });

            ReplayQuickTourCommand = new RelayCommand(() =>
            {
                var tourVm = new QuickTourViewModel(_configService, _themeService);
                var tourWindow = new QuickTourWindow(tourVm);
                _themeService.ApplyTheme(_configService.CurrentConfig.ThemeMode, tourWindow);
                tourWindow.ShowDialog();
            });

            ReplayOnboardingCommand = new RelayCommand(() =>
            {
                var onboardingVm = new OnboardingViewModel(_configService, _themeService);
                var onboardingWindow = new OnboardingWindow(onboardingVm);
                _themeService.ApplyTheme(_configService.CurrentConfig.ThemeMode, onboardingWindow);
                onboardingWindow.ShowDialog();
            });

            InstallBrowserExtensionCommand = new RelayCommand(() =>
            {
                var browserService = new BrowserIntegrationService(_configService);
                bool ok = browserService.RegisterBrowserHost();
                BrowserStatusMessage = ok 
                    ? "✓ Successfully registered Native Messaging Host for Chrome and Microsoft Edge!" 
                    : "Warning: Could not write Registry keys. Run PRRX IDM once as Administrator.";
            });

            OpenExtensionFolderCommand = new RelayCommand(() =>
            {
                var baseAppDir = AppDomain.CurrentDomain.BaseDirectory;
                var extDir = Path.Combine(baseAppDir, "extension");
                var devExtDir = @"D:\Internet Download Manager\extension";

                var target = Directory.Exists(extDir) ? extDir : devExtDir;
                if (Directory.Exists(target))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{target}\"",
                        UseShellExecute = true
                    });
                }
            });
        }

        private void UpdateCookiesStatus()
        {
            var activePath = _mediaEngine.ActiveCookiesPath;
            if (_configService.CurrentConfig.IsCookiesEnabled && !string.IsNullOrWhiteSpace(activePath) && File.Exists(activePath))
            {
                IsCookiesLoaded = true;
                CookiesStatusDisplay = $"Active: {Path.GetFileName(activePath)} ({activePath})";
            }
            else
            {
                IsCookiesLoaded = false;
                CookiesStatusDisplay = "No cookies loaded. Public single-video extraction mode active.";
            }
        }

        private async Task CheckGitHubUpdatesAsync()
        {
            try
            {
                IsCheckingUpdates = true;
                UpdateStatusMessage = "Querying GitHub Releases API directly...";

                var (updateAvailable, manifest, error) = await _updateService.CheckGitHubReleasesAsync();

                if (!string.IsNullOrWhiteSpace(error))
                {
                    UpdateStatusMessage = error;
                    IsUpdateAvailable = false;
                }
                else if (updateAvailable && manifest != null)
                {
                    _latestManifest = manifest;
                    IsUpdateAvailable = true;
                    UpdateStatusMessage = $"🎉 New Release Found: v{manifest.Version}! Click 'Open GitHub Release' to update.";
                }
                else
                {
                    IsUpdateAvailable = false;
                    UpdateStatusMessage = "You are currently running the latest official version.";
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage = $"Update Check Error: {ex.Message}";
                IsUpdateAvailable = false;
            }
            finally
            {
                IsCheckingUpdates = false;
            }
        }

        private async Task UpdateEngineAsync()
        {
            try
            {
                IsUpdatingEngine = true;
                EngineStatusMessage = "Connecting to online distribution repository to update yt-dlp...";

                var (success, message) = await _mediaEngine.UpdateEngineAsync();

                EngineStatusMessage = success ? $"✅ {message}" : $"⚠️ {message}";
            }
            catch (Exception ex)
            {
                EngineStatusMessage = $"Engine Update Failed: {ex.Message}";
            }
            finally
            {
                IsUpdatingEngine = false;
            }
        }
    }
}
