using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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

            // 2. Check if invoked with payload or URL and another instance is already running
            if (CheckAndForwardToRunningInstance(e.Args))
            {
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

                // Detect any startup payload or URL from command line
                string? pendingPayloadJson = null;
                for (int i = 0; i < e.Args.Length; i++)
                {
                    if (e.Args[i] == "--payload" && i + 1 < e.Args.Length)
                    {
                        try
                        {
                            var bytes = Convert.FromBase64String(e.Args[i + 1]);
                            pendingPayloadJson = Encoding.UTF8.GetString(bytes);
                        }
                        catch { }
                    }
                    else if (e.Args[i] == "--url" && i + 1 < e.Args.Length)
                    {
                        pendingPayloadJson = JsonSerializer.Serialize(new BrowserDownloadPayload
                        {
                            Action = "download",
                            Url = e.Args[i + 1]
                        });
                    }
                    else if (e.Args[i].StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                             e.Args[i].StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        pendingPayloadJson = JsonSerializer.Serialize(new BrowserDownloadPayload
                        {
                            Action = "download",
                            Url = e.Args[i]
                        });
                    }
                }

                bool hasStartupPayload = !string.IsNullOrWhiteSpace(pendingPayloadJson);

                // Onboarding Wizard check on first run
                if (!_configService.CurrentConfig.IsOnboardingCompleted)
                {
                    if (!hasStartupPayload)
                    {
                        var onboardingVm = new OnboardingViewModel(_configService, _themeService);
                        var onboardingWindow = new OnboardingWindow(onboardingVm);

                        _themeService.ApplyTheme(_configService.CurrentConfig.ThemeMode, onboardingWindow);

                        onboardingWindow.ShowDialog();
                    }

                    _configService.CurrentConfig.IsOnboardingCompleted = true;
                    _configService.SaveConfig();
                }

                // First-time Interactive Quick Tour / Setup Guide check
                if (!_configService.CurrentConfig.HasCompletedQuickTour)
                {
                    if (!hasStartupPayload)
                    {
                        var tourVm = new QuickTourViewModel(_configService, _themeService);
                        var tourWindow = new QuickTourWindow(tourVm);

                        _themeService.ApplyTheme(_configService.CurrentConfig.ThemeMode, tourWindow);

                        tourWindow.ShowDialog();
                    }

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

                // Process any incoming payload from command line arguments
                if (!string.IsNullOrWhiteSpace(pendingPayloadJson))
                {
                    try
                    {
                        var payload = JsonSerializer.Deserialize<BrowserDownloadPayload>(pendingPayloadJson, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        if (payload != null)
                        {
                            Dispatcher.BeginInvoke(new Action(() => _browserService?.HandleIncomingPayload(payload)), DispatcherPriority.Loaded);
                        }
                    }
                    catch { }
                }
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

                // Forward to running instance via Named Pipe or HTTP bridge
                bool forwarded = false;
                try
                {
                    using var pipeClient = new System.IO.Pipes.NamedPipeClientStream(".", BrowserIntegrationService.PipeName, System.IO.Pipes.PipeDirection.Out);
                    pipeClient.Connect(1000);
                    using var writer = new StreamWriter(pipeClient, Encoding.UTF8);
                    writer.Write(json);
                    writer.Flush();
                    forwarded = true;
                }
                catch
                {
                    // Fallback to local HTTP bridge if pipe was unavailable
                    try
                    {
                        using var client = new TcpClient();
                        var connectTask = client.ConnectAsync(IPAddress.Loopback, BrowserIntegrationService.HttpPort);
                        if (connectTask.Wait(500))
                        {
                            using var stream = client.GetStream();
                            var bodyBytes = Encoding.UTF8.GetBytes(json);
                            var request = $"POST /api/download HTTP/1.1\r\n" +
                                          $"Host: 127.0.0.1:{BrowserIntegrationService.HttpPort}\r\n" +
                                          $"Content-Type: application/json\r\n" +
                                          $"Content-Length: {bodyBytes.Length}\r\n" +
                                          $"Connection: close\r\n\r\n";
                            var reqBytes = Encoding.UTF8.GetBytes(request);
                            stream.Write(reqBytes, 0, reqBytes.Length);
                            stream.Write(bodyBytes, 0, bodyBytes.Length);
                            stream.Flush();
                            forwarded = true;
                        }
                    }
                    catch { }
                }

                if (!forwarded)
                {
                    // Main app not running - launch main process with payload
                    var appExe = Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(appExe))
                    {
                        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = appExe,
                            Arguments = $"--payload {b64}",
                            UseShellExecute = true
                        });
                    }
                }

                // Send OK acknowledgement to browser
                var responseJson = "{\"status\":\"ok\"}";
                var responseBytes = Encoding.UTF8.GetBytes(responseJson);
                var responseLen = BitConverter.GetBytes(responseBytes.Length);
                stdout.Write(responseLen, 0, 4);
                stdout.Write(responseBytes, 0, responseBytes.Length);
                stdout.Flush();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Native messaging error: {ex.Message}");
            }
        }

        private static bool CheckAndForwardToRunningInstance(string[] args)
        {
            if (args == null || args.Length == 0) return false;

            string? url = null;
            string? payloadJson = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--payload" && i + 1 < args.Length)
                {
                    try
                    {
                        var bytes = Convert.FromBase64String(args[i + 1]);
                        payloadJson = Encoding.UTF8.GetString(bytes);
                    }
                    catch { }
                }
                else if (args[i].StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         args[i].StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = args[i];
                }
            }

            if (payloadJson == null && url == null) return false;

            try
            {
                var body = payloadJson ?? JsonSerializer.Serialize(new BrowserDownloadPayload
                {
                    Action = "download",
                    Url = url!
                });

                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(IPAddress.Loopback, BrowserIntegrationService.HttpPort);
                if (!connectTask.Wait(400)) return false;

                using var stream = client.GetStream();
                stream.WriteTimeout = 1000;
                var bodyBytes = Encoding.UTF8.GetBytes(body);
                var request = $"POST /api/download HTTP/1.1\r\n" +
                              $"Host: 127.0.0.1:{BrowserIntegrationService.HttpPort}\r\n" +
                              $"Content-Type: application/json\r\n" +
                              $"Content-Length: {bodyBytes.Length}\r\n" +
                              $"Connection: close\r\n\r\n";
                var reqBytes = Encoding.UTF8.GetBytes(request);
                stream.Write(reqBytes, 0, reqBytes.Length);
                stream.Write(bodyBytes, 0, bodyBytes.Length);
                stream.Flush();

                return true;
            }
            catch
            {
                return false;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _browserService?.StopIpcServer();
            base.OnExit(e);
        }
    }
}
