using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PRRX.IDM.Models;
using PRRX.IDM.Services;

namespace PRRX.IDM.ViewModels
{
    public class OnboardingViewModel : ViewModelBase
    {
        private readonly IConfigurationService _configService;
        private readonly IThemeService _themeService;

        private int _currentStep = 1;
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

        public ICommand NextStepCommand { get; }
        public ICommand PreviousStepCommand { get; }
        public ICommand BrowseFolderCommand { get; }
        public ICommand FinishCommand { get; }
        public ICommand SetThemeCommand { get; }

        public OnboardingViewModel(IConfigurationService configService, IThemeService themeService)
        {
            _configService = configService;
            _themeService = themeService;

            _selectedTheme = _configService.CurrentConfig.ThemeMode;
            _transparencyFactor = _configService.CurrentConfig.TransparencyFactor;
            _downloadDirectory = _configService.CurrentConfig.DownloadDirectory;
            _isWin11 = _themeService.IsWindows11;

            NextStepCommand = new RelayCommand(() =>
            {
                if (CurrentStep < 4)
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
                _configService.SaveConfig();

                OnboardingCompleted?.Invoke();
            });
        }
    }
}
