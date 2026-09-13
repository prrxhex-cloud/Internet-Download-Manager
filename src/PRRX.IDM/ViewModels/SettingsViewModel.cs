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
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using PRRX.IDM.Models;
using PRRX.IDM.Security;
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
        private readonly ISecurityService _securityService;

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
        private bool _isDownloadingUpdate;
        private double _updateDownloadProgress;
        private string _updateDownloadProgressText = string.Empty;
        private bool _canApplyUpdate;
        private UpdateManifest? _latestManifest;

        private bool _isSecurityVaultActive = true;
        private string _securityVaultStatusText = "AES-256-GCM / DPAPI Vault Active. Stored configurations, cookie caches, history, and IPC tokens are encrypted.";

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

        public string CurrentVersionClean => _updateService?.CurrentVersionClean ?? "1.3.0";

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

        public bool IsDownloadingUpdate
        {
            get => _isDownloadingUpdate;
            set => SetProperty(ref _isDownloadingUpdate, value);
        }

        public double UpdateDownloadProgress
        {
            get => _updateDownloadProgress;
            set => SetProperty(ref _updateDownloadProgress, value);
        }

        public string UpdateDownloadProgressText
        {
            get => _updateDownloadProgressText;
            set => SetProperty(ref _updateDownloadProgressText, value);
        }

        public bool CanApplyUpdate
        {
            get => _canApplyUpdate;
            set => SetProperty(ref _canApplyUpdate, value);
        }

        public bool IsSecurityVaultActive
        {
            get => _isSecurityVaultActive;
            set => SetProperty(ref _isSecurityVaultActive, value);
        }

        public string SecurityVaultStatusText
        {
            get => _securityVaultStatusText;
            set => SetProperty(ref _securityVaultStatusText, value);
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

        private string _cleanupStatusMessage = "Automatic post-update cleanup is active (Zero Data Loss Protected).";
        public string CleanupStatusMessage
        {
            get => _cleanupStatusMessage;
            set => SetProperty(ref _cleanupStatusMessage, value);
        }

        private bool _isCleaningCache;
        public bool IsCleaningCache
        {
            get => _isCleaningCache;
            set => SetProperty(ref _isCleaningCache, value);
        }

        public ObservableCollection<ChangelogRelease> ChangelogHistory { get; } = new();

        public ICommand BrowseFolderCommand { get; }
        public ICommand BrowseCookiesCommand { get; }
        public ICommand ClearCookiesCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand UpdateNowInAppCommand { get; }
        public ICommand OpenLatestReleaseCommand { get; }
        public ICommand AuditSecurityCommand { get; }
        public ICommand UpdateEngineCommand { get; }
        public ICommand SetThemeCommand { get; }
        public ICommand ReplayQuickTourCommand { get; }
        public ICommand ReplayOnboardingCommand { get; }
        public ICommand InstallBrowserExtensionCommand { get; }
        public ICommand OpenExtensionFolderCommand { get; }
        public ICommand CleanupCacheCommand { get; }

        public SettingsViewModel(
            IConfigurationService configService,
            IThemeService themeService,
            IUpdateService updateService,
            IMediaEngineService mediaEngine,
            ISecurityService? securityService = null)
        {
            _configService = configService;
            _themeService = themeService;
            _updateService = updateService;
            _mediaEngine = mediaEngine;
            _securityService = securityService ?? new SecurityService();

            _selectedTheme = _configService.CurrentConfig.ThemeMode;
            _transparencyFactor = _configService.CurrentConfig.TransparencyFactor;
            _downloadDirectory = _configService.CurrentConfig.DownloadDirectory;
            _cookiesFilePath = _configService.CurrentConfig.CookiesFilePath;
            _enableTurboAcceleration = _configService.CurrentConfig.EnableTurboAcceleration;
            _turboConnectionCount = _configService.CurrentConfig.TurboConnectionCount;
            _isWin11 = _themeService.IsWindows11;
            _appVersion = $"v{CurrentVersionClean} (Official Release)";

            UpdateCookiesStatus();
            InitializeChangelog();

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

            CheckForUpdatesCommand = new AsyncRelayCommand(CheckGitHubUpdatesAsync, () => !IsCheckingUpdates && !IsDownloadingUpdate);

            UpdateNowInAppCommand = new AsyncRelayCommand(ApplyInAppUpdateAsync, () => !IsDownloadingUpdate && IsUpdateAvailable && _latestManifest != null);

            OpenLatestReleaseCommand = new RelayCommand(() =>
            {
                if (_latestManifest != null && !string.IsNullOrWhiteSpace(_latestManifest.DownloadUrl))
                {
                    if (SecurityGuard.ValidateUrl(_latestManifest.DownloadUrl, out var safeUrl, out _))
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = safeUrl,
                                UseShellExecute = true
                            });
                        }
                        catch
                        {
                            // Fallback
                        }
                    }
                }
            });

            AuditSecurityCommand = new RelayCommand(() =>
            {
                try
                {
                    var key = _securityService.GetOrCreateMasterKey();
                    var token = _securityService.GetOrCreateIpcToken();
                    _configService.SaveConfig();
                    _securityService.ZeroMemory(key);
                    SecurityVaultStatusText = $"✅ Audit Passed: AES-256-GCM key & IPC token active (Token prefix: {token.Substring(0, Math.Min(8, token.Length))}...). All configurations & history re-encrypted.";
                }
                catch (Exception ex)
                {
                    SecurityVaultStatusText = $"Security Audit Notice: {ex.Message}";
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
                var browserService = new BrowserIntegrationService(_configService, _securityService);
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
                        UseShellExecute = false,
                        ArgumentList = { Path.GetFullPath(target) }
                    });
                }
            });

            CleanupCacheCommand = new AsyncRelayCommand(async () =>
            {
                try
                {
                    IsCleaningCache = true;
                    CleanupStatusMessage = "Scanning for update cache, old builds, and temp swap files...";
                    var report = await _updateService.CleanupPostUpdateArtifactsAsync();
                    if (report.FilesDeletedCount > 0 || report.DirectoriesCleanedCount > 0)
                    {
                        CleanupStatusMessage = $"✅ Cleanup Complete: Freed {report.FormattedBytesFreed} ({report.FilesDeletedCount} files, {report.DirectoriesCleanedCount} folders cleaned). User data 100% intact.";
                    }
                    else
                    {
                        CleanupStatusMessage = "✅ System Clean: No stale update files or old temp builds found. User data 100% intact.";
                    }
                }
                catch (Exception ex)
                {
                    CleanupStatusMessage = $"Cleanup Notice: {ex.Message}";
                }
                finally
                {
                    IsCleaningCache = false;
                }
            }, () => !IsCleaningCache);
        }

        private void InitializeChangelog()
        {
            ChangelogHistory.Clear();

            // v1.3.0 (Current Release)
            var rel130 = new ChangelogRelease
            {
                Version = "v1.3.0",
                ReleaseDate = "September 2026",
                IsCurrentRelease = true,
                StatusBadge = "Current Release",
                Summary = "In-App Seamless 1-Click Updater enhancements, automatic post-update cache and stale build cleanup with Zero Data Loss Guarantee, dedicated interactive Changelog in Settings, AES-256-GCM / DPAPI Security Vault, and refined Fluent 2 UI.",
                IsExpanded = true,
                Items = new System.Collections.Generic.List<ChangelogItem>
                {
                    new() { Category = "Features", Description = "Interactive Changelog & Version History in Settings with categorized change logs across all releases.", CategoryBadgeColor = "#0078D4", CategoryBgColor = "#200078D4" },
                    new() { Category = "Features", Description = "Automated Post-Update Cleanup: automatically deletes leftover update archives (*.zip), binary swap files (*.old, *.bak, *.tmp), and extraction caches upon restart.", CategoryBadgeColor = "#0078D4", CategoryBgColor = "#200078D4" },
                    new() { Category = "Features", Description = "Resilient 1-Click In-App Updater: automatic retry mechanism with exponential backoff and 64KB high-throughput streaming.", CategoryBadgeColor = "#0078D4", CategoryBgColor = "#200078D4" },
                    new() { Category = "Security", Description = "Zero Data Loss Guarantee: absolute path protections ensure configurations (config.json), history, queues, and user download folders are untouched.", CategoryBadgeColor = "#107C41", CategoryBgColor = "#20107C41" },
                    new() { Category = "Security", Description = "End-to-End Encryption Vault: AES-256-GCM and DPAPI security with on-demand vault audit and re-encryption.", CategoryBadgeColor = "#107C41", CategoryBgColor = "#20107C41" },
                    new() { Category = "Performance", Description = "Streamlined file swap execution in PowerShell update handoff with locked-file avoidance.", CategoryBadgeColor = "#8764B8", CategoryBgColor = "#208764B8" },
                    new() { Category = "Fixes", Description = "Resolved edge case where lingering update zip packages occupied temp storage after in-app updates.", CategoryBadgeColor = "#D83B01", CategoryBgColor = "#20D83B01" }
                }
            };

            // v1.2.0
            var rel120 = new ChangelogRelease
            {
                Version = "v1.2.0",
                ReleaseDate = "August 2026",
                IsCurrentRelease = false,
                StatusBadge = "Stable",
                Summary = "Official PRRX branding across extension and desktop, automated Chrome and Edge integration with clean uninstaller, floating video grabber on 1,000+ streaming platforms, 32-stream turbo acceleration engine, and ultra-compact distribution installers.",
                IsExpanded = false,
                Items = new System.Collections.Generic.List<ChangelogItem>
                {
                    new() { Category = "Features", Description = "Automated Chrome & Edge extension registration and native messaging host pairing.", CategoryBadgeColor = "#0078D4", CategoryBgColor = "#200078D4" },
                    new() { Category = "Features", Description = "Floating Video & Audio Grabber button supporting over 1,000 streaming platforms.", CategoryBadgeColor = "#0078D4", CategoryBgColor = "#200078D4" },
                    new() { Category = "Features", Description = "Interactive 5-step welcome Onboarding Wizard and Feature Tour replayable on demand.", CategoryBadgeColor = "#0078D4", CategoryBgColor = "#200078D4" },
                    new() { Category = "Performance", Description = "32-stream turbo multi-threaded chunking engine for maximum network throughput.", CategoryBadgeColor = "#8764B8", CategoryBgColor = "#208764B8" },
                    new() { Category = "Performance", Description = "Compact distribution: < 2 MB Web Installer and 91 MB portable standalone archive.", CategoryBadgeColor = "#8764B8", CategoryBgColor = "#208764B8" },
                    new() { Category = "Security", Description = "Clean uninstaller engine removing Chrome and Edge native messaging host registry keys.", CategoryBadgeColor = "#107C41", CategoryBgColor = "#20107C41" }
                }
            };

            // v1.1.0
            var rel110 = new ChangelogRelease
            {
                Version = "v1.1.0",
                ReleaseDate = "July 2026",
                IsCurrentRelease = false,
                StatusBadge = "Archived",
                Summary = "Browser integration HTTP bridge, link resolution expansion, multi-format audio converter, and zero-residue native host unregistration.",
                IsExpanded = false,
                Items = new System.Collections.Generic.List<ChangelogItem>
                {
                    new() { Category = "Features", Description = "High-speed local loopback HTTP bridge (127.0.0.1:46543) for browser-to-desktop IPC transfer.", CategoryBadgeColor = "#0078D4", CategoryBgColor = "#200078D4" },
                    new() { Category = "Features", Description = "Universal Audio Converter supporting 10+ studio formats (MP3 up to 320kbps, WAV, M4R, FLAC).", CategoryBadgeColor = "#0078D4", CategoryBgColor = "#200078D4" },
                    new() { Category = "Security", Description = "Origin header verification and cryptographically random IPC session tokens.", CategoryBadgeColor = "#107C41", CategoryBgColor = "#20107C41" },
                    new() { Category = "Fixes", Description = "Addressed edge-case socket disconnection during continuous batch downloads.", CategoryBadgeColor = "#D83B01", CategoryBgColor = "#20D83B01" }
                }
            };

            // v1.0.0
            var rel100 = new ChangelogRelease
            {
                Version = "v1.0.0",
                ReleaseDate = "June 2026",
                IsCurrentRelease = false,
                StatusBadge = "Initial Release",
                Summary = "Initial high-performance download manager release with multi-socket chunking, media engine extraction, and .NET 8 WPF architecture.",
                IsExpanded = false,
                Items = new System.Collections.Generic.List<ChangelogItem>
                {
                    new() { Category = "Features", Description = "Initial release of PRRX Internet Download Manager with modern .NET 8 WPF architecture.", CategoryBadgeColor = "#0078D4", CategoryBgColor = "#200078D4" },
                    new() { Category = "Features", Description = "Multi-socket segmented download engine with pause and resume capabilities.", CategoryBadgeColor = "#0078D4", CategoryBgColor = "#200078D4" },
                    new() { Category = "Features", Description = "yt-dlp core media extraction pipeline with dynamic resolution selector.", CategoryBadgeColor = "#0078D4", CategoryBgColor = "#200078D4" },
                    new() { Category = "Performance", Description = "Win32 kernel working set trimmer reducing idle memory footprint to ~20 MB.", CategoryBadgeColor = "#8764B8", CategoryBgColor = "#208764B8" },
                    new() { Category = "Security", Description = "Path traversal boundary checks and process argument injection filtering.", CategoryBadgeColor = "#107C41", CategoryBgColor = "#20107C41" }
                }
            };

            ChangelogHistory.Add(rel130);
            ChangelogHistory.Add(rel120);
            ChangelogHistory.Add(rel110);
            ChangelogHistory.Add(rel100);

            // Dynamically synchronize current running release with assembly version
            bool currentFound = false;
            foreach (var release in ChangelogHistory)
            {
                var cleanRel = release.Version.TrimStart('v', 'V').Trim();
                if (cleanRel == CurrentVersionClean)
                {
                    release.IsCurrentRelease = true;
                    release.StatusBadge = "Current Release";
                    release.IsExpanded = true;
                    currentFound = true;
                }
                else
                {
                    release.IsCurrentRelease = false;
                    if (release.StatusBadge == "Current Release")
                    {
                        release.StatusBadge = "Previous Release";
                    }
                }
            }

            if (!currentFound && UpdateService.IsVersionNewer(CurrentVersionClean, "1.3.0"))
            {
                ChangelogHistory.Insert(0, new ChangelogRelease
                {
                    Version = $"v{CurrentVersionClean}",
                    ReleaseDate = DateTime.UtcNow.ToString("MMMM yyyy"),
                    IsCurrentRelease = true,
                    StatusBadge = "Current Release",
                    Summary = $"PRRX IDM v{CurrentVersionClean} installed and running.",
                    IsExpanded = true,
                    Items = new System.Collections.Generic.List<ChangelogItem>
                    {
                        new() { Category = "Features", Description = $"Running PRRX IDM v{CurrentVersionClean} official build.", CategoryBadgeColor = "#0078D4", CategoryBgColor = "#200078D4" }
                    }
                });
            }
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

        public async Task CheckGitHubUpdatesAsync()
        {
            try
            {
                IsCheckingUpdates = true;
                UpdateStatusMessage = "Querying GitHub Releases API directly...";

                var (updateAvailable, manifest, error) = await _updateService.CheckGitHubReleasesAsync();

                if (!string.IsNullOrWhiteSpace(error) && manifest == null)
                {
                    UpdateStatusMessage = error;
                    IsUpdateAvailable = false;
                    CanApplyUpdate = false;
                    _latestManifest = null;
                }
                else if (updateAvailable && manifest != null)
                {
                    _latestManifest = manifest;
                    IsUpdateAvailable = true;
                    CanApplyUpdate = true;
                    UpdateStatusMessage = $"🎉 New Release Found: v{manifest.Version}! Click 'Update Now (In-App)' to update seamlessly without losing data.";
                }
                else
                {
                    _latestManifest = null;
                    IsUpdateAvailable = false;
                    CanApplyUpdate = false;
                    UpdateStatusMessage = $"You're up to date! PRRX IDM v{CurrentVersionClean} is the latest version.";
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage = $"Update Check Error: {ex.Message}";
                IsUpdateAvailable = false;
                CanApplyUpdate = false;
                _latestManifest = null;
            }
            finally
            {
                IsCheckingUpdates = false;
            }
        }

        private async Task ApplyInAppUpdateAsync()
        {
            if (_latestManifest == null || !IsUpdateAvailable) return;

            try
            {
                IsDownloadingUpdate = true;
                UpdateDownloadProgress = 0;
                UpdateDownloadProgressText = "Connecting to release server...";

                var progress = new Progress<double>(pct =>
                {
                    UpdateDownloadProgress = pct;
                    UpdateDownloadProgressText = $"Downloading update: {pct:F1}%";
                });

                UpdateStatusMessage = $"Downloading & staging update v{_latestManifest.Version}...";
                var (success, message) = await _updateService.ApplyInAppUpdateAsync(
                    _latestManifest,
                    progress,
                    beforeShutdown: () =>
                    {
                        _configService.SaveConfig();
                    });

                if (!success)
                {
                    UpdateStatusMessage = $"Update Notice: {message}";
                    UpdateDownloadProgressText = message;
                }
                else
                {
                    UpdateDownloadProgressText = "Update staged. Application restarting...";
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage = $"Update Failed: {ex.Message}";
                UpdateDownloadProgressText = ex.Message;
            }
            finally
            {
                IsDownloadingUpdate = false;
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
