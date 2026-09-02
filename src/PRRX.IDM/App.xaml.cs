using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PRRX.IDM.Models;
using PRRX.IDM.Services;
using PRRX.IDM.ViewModels;
using PRRX.IDM.Views;

namespace PRRX.IDM
{
    public partial class App : Application
    {
        private IConfigurationService? _configService;
        private IThemeService? _themeService;
        private IMediaEngineService? _mediaEngine;
        private IThumbnailService? _thumbnailService;
        private IUpdateService? _updateService;
        private IHistoryService? _historyService;

        static App()
        {
            // Enable hardware acceleration and high-refresh-rate animation rendering
            try
            {
                RenderOptions.ProcessRenderMode = RenderMode.Default;
                Timeline.DesiredFrameRateProperty.OverrideMetadata(
                    typeof(Timeline),
                    new FrameworkPropertyMetadata(120));
            }
            catch
            {
                // Ignore if already set
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Prevent silent shutdown when dialogs close
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Global exception logging and catchers
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            base.OnStartup(e);

            try
            {
                _configService = new ConfigurationService();
                _themeService = new ThemeService(_configService);
                _mediaEngine = new MediaEngineService(_configService);
                _thumbnailService = new ThumbnailService();
                _updateService = new UpdateService();
                _historyService = new HistoryService();

                // Initial global theme application
                _themeService.ApplyTheme(_configService.CurrentConfig.ThemeMode);

                // Initialize continuous memory trimming & working set compaction
                MemoryOptimizer.InitializeAutoTrimmer();

                // Onboarding Wizard check on first run
                if (!_configService.CurrentConfig.IsOnboardingCompleted)
                {
                    var onboardingVm = new OnboardingViewModel(_configService, _themeService);
                    var onboardingWindow = new OnboardingWindow(onboardingVm);

                    _themeService.ApplyTheme(_configService.CurrentConfig.ThemeMode, onboardingWindow);

                    onboardingWindow.ShowDialog();

                    _configService.CurrentConfig.IsOnboardingCompleted = true;
                    _configService.SaveConfig();
                }

                // First-time Interactive Quick Tour / Setup Guide check
                if (!_configService.CurrentConfig.HasCompletedQuickTour)
                {
                    var tourVm = new QuickTourViewModel(_configService, _themeService);
                    var tourWindow = new QuickTourWindow(tourVm);

                    _themeService.ApplyTheme(_configService.CurrentConfig.ThemeMode, tourWindow);

                    tourWindow.ShowDialog();

                    _configService.CurrentConfig.HasCompletedQuickTour = true;
                    _configService.SaveConfig();
                }

                // Launch Main Application Window
                var mainVm = new MainViewModel(
                    _configService,
                    _themeService,
                    _mediaEngine,
                    _thumbnailService,
                    _updateService,
                    _historyService);

                var mainWindow = new MainWindow(mainVm);
                MainWindow = mainWindow;

                // Hook low-memory working set trimmer to window lifecycle
                MemoryOptimizer.HookWindow(mainWindow);

                // Bind main window close to application shutdown
                ShutdownMode = ShutdownMode.OnMainWindowClose;

                _themeService.ApplyTheme(_configService.CurrentConfig.ThemeMode, mainWindow);

                mainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fatal Error during PRRX IDM startup:\n\n{ex.Message}\n\nStack:\n{ex.StackTrace}",
                    "PRRX IDM Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown(-1);
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                $"Unexpected Application Error:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
                "PRRX IDM Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"Fatal System Error:\n\n{ex.Message}",
                    "PRRX IDM Critical Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
