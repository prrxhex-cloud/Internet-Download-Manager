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
        private IBrowserIntegrationService? _browserService;

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
            // 1. Check if invoked by Chrome or Edge as Native Messaging Host
            if (e.Args.Length > 0 && (e.Args[0].StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) || e.Args[0] == "--native-messaging-host"))
            {
                RunNativeMessagingMode();
                Shutdown(0);
                return;
            }

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
                _browserService = new BrowserIntegrationService(_configService);

                // Auto-register Chrome and Edge Native Messaging and start IPC listener
                _browserService.RegisterBrowserHost();
                _browserService.StartIpcServer();

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

        private static void RunNativeMessagingMode()
        {
            try
            {
                using var stdin = Console.OpenStandardInput();
                using var stdout = Console.OpenStandardOutput();

                var lengthBuffer = new byte[4];
                if (stdin.Read(lengthBuffer, 0, 4) < 4) return;

                int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                if (messageLength <= 0 || messageLength > 10 * 1024 * 1024) return;

                var messageBuffer = new byte[messageLength];
                int totalRead = 0;
                while (totalRead < messageLength)
                {
                    int read = stdin.Read(messageBuffer, totalRead, messageLength - totalRead);
                    if (read <= 0) break;
                    totalRead += read;
                }

                var json = System.Text.Encoding.UTF8.GetString(messageBuffer, 0, totalRead);

                // Forward to local IPC Pipe Server
                try
                {
                    using var pipeClient = new System.IO.Pipes.NamedPipeClientStream(".", BrowserIntegrationService.PipeName, System.IO.Pipes.PipeDirection.Out);
                    pipeClient.Connect(1500);
                    using var writer = new StreamWriter(pipeClient, System.Text.Encoding.UTF8);
                    writer.Write(json);
                    writer.Flush();
                }
                catch
                {
                    // Main app not running - launch main process
                    var appExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(appExe))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = appExe,
                            UseShellExecute = true
                        });
                    }
                }

                // Send OK acknowledgement to browser
                var responseJson = "{\"status\":\"ok\"}";
                var responseBytes = System.Text.Encoding.UTF8.GetBytes(responseJson);
                var responseLen = BitConverter.GetBytes(responseBytes.Length);
                stdout.Write(responseLen, 0, 4);
                stdout.Write(responseBytes, 0, responseBytes.Length);
                stdout.Flush();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Native messaging error: {ex.Message}");
            }
        }
    }
}
