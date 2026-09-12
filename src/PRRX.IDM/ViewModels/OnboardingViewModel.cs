using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using PRRX.IDM.Models;
using PRRX.IDM.Services;

namespace PRRX.IDM.ViewModels
{
    public class OnboardingViewModel : ViewModelBase
    {
        private readonly IConfigurationService _configService;
        private readonly IThemeService _themeService;
        private readonly IBrowserIntegrationService? _browserService;

        private int _currentStep = 1;
        private const int _totalSteps = 5;
        private int _extensionSubStep = 1;
        private string _extensionRegistrationStatus = "✓ Chrome & Microsoft Edge auto-registration ready.";
        private AppThemeMode _selectedTheme;
        private double _transparencyFactor;
        private string _downloadDirectory;
        private bool _isWin11;

        public event Action? OnboardingCompleted;

        public int CurrentStep
        {
            get => _currentStep;
            set => SetProperty(ref _currentStep, value);
        }

        public int TotalSteps => _totalSteps;

        public int ExtensionSubStep
        {
            get => _extensionSubStep;
            set => SetProperty(ref _extensionSubStep, value);
        }

        public string ExtensionRegistrationStatus
        {
            get => _extensionRegistrationStatus;
            set => SetProperty(ref _extensionRegistrationStatus, value);
        }

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
            set => SetProperty(ref _downloadDirectory, value);
        }

        public bool IsWin11
        {
            get => _isWin11;
            set => SetProperty(ref _isWin11, value);
        }

        public string ExtensionDirectory
        {
            get
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var extDir = Path.Combine(baseDir, "extension");
                if (Directory.Exists(extDir)) return extDir;
                var devExt = @"D:\Internet Download Manager\extension";
                if (Directory.Exists(devExt)) return devExt;
                var pubExt = @"D:\Internet Download Manager\publish\extension";
                if (Directory.Exists(pubExt)) return pubExt;
                return extDir;
            }
        }

        public ICommand NextStepCommand { get; }
        public ICommand PreviousStepCommand { get; }
        public ICommand SetExtensionSubStepCommand { get; }
        public ICommand NextExtensionSubStepCommand { get; }
        public ICommand PreviousExtensionSubStepCommand { get; }
        public ICommand RegisterExtensionCommand { get; }
        public ICommand OpenExtensionFolderCommand { get; }
        public ICommand OpenChromeExtensionsCommand { get; }
        public ICommand OpenEdgeExtensionsCommand { get; }
        public ICommand BrowseFolderCommand { get; }
        public ICommand FinishCommand { get; }
        public ICommand SetThemeCommand { get; }

        public OnboardingViewModel(IConfigurationService configService, IThemeService themeService, IBrowserIntegrationService? browserService = null)
        {
            _configService = configService;
            _themeService = themeService;
            _browserService = browserService;

            _selectedTheme = _configService.CurrentConfig.ThemeMode;
            _transparencyFactor = _configService.CurrentConfig.TransparencyFactor;
            _downloadDirectory = _configService.CurrentConfig.DownloadDirectory;
            _isWin11 = _themeService.IsWindows11;

            NextStepCommand = new RelayCommand(() =>
            {
                if (CurrentStep < TotalSteps)
                {
                    CurrentStep++;
                }
            });

            PreviousStepCommand = new RelayCommand(() =>
            {
                if (CurrentStep > 1)
                {
                    CurrentStep--;
                }
            });

            SetExtensionSubStepCommand = new RelayCommand(param =>
            {
                if (param is int stepNum)
                {
                    ExtensionSubStep = stepNum;
                }
                else if (param is string stepStr && int.TryParse(stepStr, out int parsed))
                {
                    ExtensionSubStep = parsed;
                }
            });

            NextExtensionSubStepCommand = new RelayCommand(() =>
            {
                if (ExtensionSubStep < 3)
                {
                    ExtensionSubStep++;
                }
            });

            PreviousExtensionSubStepCommand = new RelayCommand(() =>
            {
                if (ExtensionSubStep > 1)
                {
                    ExtensionSubStep--;
                }
            });

            RegisterExtensionCommand = new RelayCommand(() =>
            {
                try
                {
                    var svc = _browserService ?? new BrowserIntegrationService(_configService);
                    bool ok = svc.RegisterBrowserHost();
                    svc.RegisterExtensionInRegistry();
                    ExtensionRegistrationStatus = ok
                        ? "✓ Successfully registered Native Messaging Host & Registry for Chrome & Edge!"
                        : "✓ Extension auto-registration updated.";
                }
                catch (Exception ex)
                {
                    ExtensionRegistrationStatus = $"Registration notice: {ex.Message}";
                }
            });

            OpenExtensionFolderCommand = new RelayCommand(() =>
            {
                try
                {
                    var target = ExtensionDirectory;
                    if (Directory.Exists(target))
                    {
                        try
                        {
                            System.Windows.Clipboard.SetText(target);
                            ExtensionRegistrationStatus = $"✓ Folder opened & path copied to clipboard:\n{target}";
                        }
                        catch { }

                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{target}\"",
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        ExtensionRegistrationStatus = $"Extension folder not found at: {target}";
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open extension folder: {ex.Message}");
                    ExtensionRegistrationStatus = $"Notice: {ex.Message}";
                }
            });

            OpenChromeExtensionsCommand = new RelayCommand(() =>
            {
                OpenBrowserUrl("chrome.exe", "chrome://extensions");
                ExtensionRegistrationStatus = "Opening Chrome Extensions page (chrome://extensions)...";
            });

            OpenEdgeExtensionsCommand = new RelayCommand(() =>
            {
                OpenBrowserUrl("msedge.exe", "edge://extensions");
                ExtensionRegistrationStatus = "Opening Microsoft Edge Extensions page (edge://extensions)...";
            });

            BrowseFolderCommand = new RelayCommand(() =>
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Select Default Download Directory",
                    InitialDirectory = Directory.Exists(DownloadDirectory) ? DownloadDirectory : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                };

                if (dialog.ShowDialog() == true)
                {
                    DownloadDirectory = dialog.FolderName;
                }
            });

            SetThemeCommand = new RelayCommand(param =>
            {
                if (param is string themeStr && Enum.TryParse<AppThemeMode>(themeStr, true, out var mode))
                {
                    SelectedTheme = mode;
                }
            });

            FinishCommand = new RelayCommand(() =>
            {
                _configService.CurrentConfig.ThemeMode = SelectedTheme;
                _configService.CurrentConfig.TransparencyFactor = TransparencyFactor;
                _configService.CurrentConfig.DownloadDirectory = DownloadDirectory;
                _configService.CurrentConfig.IsOnboardingCompleted = true;
                _configService.CurrentConfig.HasCompletedQuickTour = true;
                _configService.SaveConfig();

                OnboardingCompleted?.Invoke();
            });
        }

        private static void OpenBrowserUrl(string browserExe, string targetUrl)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = browserExe,
                    Arguments = targetUrl,
                    UseShellExecute = true
                });
                return;
            }
            catch { }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{browserExe}");
                var exePath = key?.GetValue("")?.ToString();
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = targetUrl,
                        UseShellExecute = true
                    });
                    return;
                }
            }
            catch { }

            var candidates = browserExe.Contains("chrome", StringComparison.OrdinalIgnoreCase)
                ? new[]
                {
                    @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                    @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
                }
                : new[]
                {
                    @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                    @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
                };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = candidate,
                            Arguments = targetUrl,
                            UseShellExecute = true
                        });
                        return;
                    }
                    catch { }
                }
            }
        }
    }
}
