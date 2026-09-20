// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
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
        private ITelegramBotSyncService? _telegramBotSyncService;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

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
                ApplyProcessPriority(_configService.CurrentConfig.ProcessPriority);
                _themeService = new ThemeService(_configService);
                _mediaEngine = new MediaDownloaderService(_configService);
                _thumbnailService = new ThumbnailService();
                _updateService = new UpdateService();
                _historyService = new HistoryService();
                _browserService = new BrowserIntegrationService(_configService);
                _telegramBotSyncService = new TelegramBotSyncService(_configService);

                _telegramBotSyncService.TaskReceived += (s, task) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            var defaultDir = _configService?.CurrentConfig.DownloadDirectory;
                            if (string.IsNullOrWhiteSpace(defaultDir))
                            {
                                defaultDir = Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                    "Downloads");
                            }

                            var pageTitle = !string.IsNullOrWhiteSpace(task.Source)
                                ? $"Telegram File ({task.Source})"
                                : "Telegram File (@PRRX_IDM_Bot)";

                            var vm = new DownloadFileInfoViewModel(
                                task.Url,
                                defaultDir,
                                pageTitle,
                                task.FileSize > 0 ? task.FileSize : null,
                                task.FileName,
                                null,
                                task.Source);

                            if (!string.IsNullOrWhiteSpace(task.FileName))
                            {
                                vm.FileName = task.FileName;
                            }

                            if (task.FileSize > 0)
                            {
                                vm.FileSizeFormatted = !string.IsNullOrWhiteSpace(task.FormattedSize)
                                    ? task.FormattedSize
                                    : TelegramDownloadProvider.FormatBytes(task.FileSize);
                            }

                            var dlg = new DownloadFileInfoDialog(vm);
                            dlg.Topmost = true;
                            dlg.Show();
                            dlg.Activate();
                            dlg.Focus();

                            try
                            {
                                var helper = new WindowInteropHelper(dlg);
                                helper.EnsureHandle();
                                SetForegroundWindow(helper.Handle);
                                BringWindowToTop(helper.Handle);
                            }
                            catch { }

                            dlg.Loaded += (_, _) =>
                            {
                                _ = System.Threading.Tasks.Task.Delay(150).ContinueWith(_ =>
                                    dlg.Dispatcher.BeginInvoke(new Action(() => dlg.Topmost = false)));
                            };

                            dlg.Closed += (_, _) =>
                            {
                                vm.CancelProbe();
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
                                        vm.CustomHeaders);

                                    var activeWin = new ActiveDownloadWindow(activeVm);
                                    activeWin.Closed += (_, _) => MemoryOptimizer.TrimMemory();
                                    activeWin.Show();
                                    activeWin.Activate();
                                }
                                else if (vm.DialogResult == DownloadDialogResult.DownloadLater)
                                {
                                    _historyService?.AddItem(new DownloadItem
                                    {
                                        Title = vm.FileName,
                                        Url = vm.Url,
                                        TargetFilePath = vm.SaveAsFullPath,
                                        FileSizeFormatted = vm.FileSizeFormatted,
                                        Status = DownloadStatus.Queued,
                                        Type = vm.SelectedCategory == FileCategory.Music ? MediaType.Audio : MediaType.Video,
                                        CreatedAt = DateTime.UtcNow
                                    });
                                    MemoryOptimizer.TrimMemory();
                                }
                                else
                                {
                                    MemoryOptimizer.TrimMemory();
                                }
                            };
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Telegram Bot Sync] Task processing error: {ex.Message}");
                        }
                    }), DispatcherPriority.Send);
                };

                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await _telegramBotSyncService.InitializeAsync();
                        if (_configService.CurrentConfig.IsTelegramBotSyncEnabled)
                        {
                            _telegramBotSyncService.Start();
                        }
                    }
                    catch { }
                });

                // Auto-register Chrome and Edge Native Messaging in background to prevent startup I/O lag
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    try { _browserService.RegisterBrowserHost(); } catch { }
                });
                _browserService.StartIpcServer();

                // Auto-clean post-update cache, temp build archives, and stale binary swap files in background
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await _updateService.CleanupPostUpdateArtifactsAsync();
                    }
                    catch
                    {
                        // Non-critical background cleanup
                    }
                });

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
                bool isSilentLaunch = false;
                for (int i = 0; i < e.Args.Length; i++)
                {
                    var arg = e.Args[i];
                    if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("--preload", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("--background", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("/preload", StringComparison.OrdinalIgnoreCase))
                    {
                        isSilentLaunch = true;
                        break;
                    }
                }

                // Onboarding Wizard check on first run
                if (!_configService.CurrentConfig.IsOnboardingCompleted)
                {
                    if (!hasStartupPayload && !isSilentLaunch)
                    {
                        var onboardingVm = new OnboardingViewModel(_configService, _themeService, _browserService);
                        var onboardingWindow = new OnboardingWindow(onboardingVm);

                        _themeService.ApplyTheme(_configService.CurrentConfig.ThemeMode, onboardingWindow);

                        onboardingWindow.ShowDialog();

                        _configService.CurrentConfig.IsOnboardingCompleted = true;
                        _configService.CurrentConfig.HasCompletedQuickTour = true;
                        _configService.SaveConfig();
                    }
                }

                // First-time Interactive Quick Tour / Setup Guide check
                if (!_configService.CurrentConfig.HasCompletedQuickTour)
                {
                    if (!hasStartupPayload && !isSilentLaunch)
                    {
                        var tourVm = new QuickTourViewModel(_configService, _themeService);
                        var tourWindow = new QuickTourWindow(tourVm);

                        _themeService.ApplyTheme(_configService.CurrentConfig.ThemeMode, tourWindow);

                        tourWindow.ShowDialog();

                        _configService.CurrentConfig.HasCompletedQuickTour = true;
                        _configService.SaveConfig();
                    }
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

                // Ensure app lifecycle survives dialog closings while MainWindow is hidden
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                mainWindow.Closed += (_, _) => Shutdown(0);

                _themeService.ApplyTheme(_configService.CurrentConfig.ThemeMode, mainWindow);

                if (!hasStartupPayload && !isSilentLaunch)
                {
                    mainWindow.Show();
                }
                else
                {
                    mainWindow.WindowState = WindowState.Minimized;
                    mainWindow.Visibility = Visibility.Hidden;
                    mainWindow.ShowInTaskbar = false;
                    System.Threading.Tasks.Task.Delay(600).ContinueWith(_ => MemoryOptimizer.TrimMemory());
                }

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
                            Dispatcher.BeginInvoke(new Action(() => _browserService?.HandleIncomingPayload(payload)), DispatcherPriority.Send);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Startup exception: {ex}");
                MessageBox.Show(
                    $"A startup error occurred:\n\n{ex.Message}",
                    "PRRX IDM Startup Notice",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown(-1);
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Debug.WriteLine($"Dispatcher unhandled exception: {e.Exception}");
            MessageBox.Show(
                $"An operational error occurred:\n\n{e.Exception.Message}",
                "PRRX IDM Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Debug.WriteLine($"Domain unhandled exception: {ex}");
                MessageBox.Show(
                    $"A system error occurred:\n\n{ex.Message}",
                    "PRRX IDM Critical Notice",
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

                // Authenticate payload with secure local IPC token
                var sec = new Security.SecurityService();
                var ipcToken = sec.GetOrCreateIpcToken();
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var dict = new System.Collections.Generic.Dictionary<string, object?>();
                    foreach (var prop in root.EnumerateObject())
                    {
                        dict[prop.Name] = prop.Value.Clone();
                    }
                    dict["token"] = ipcToken;
                    json = JsonSerializer.Serialize(dict);
                }
                catch { }

                // Forward to running instance via Named Pipe or HTTP bridge
                bool forwarded = false;
                try
                {
                    using var pipeClient = new System.IO.Pipes.NamedPipeClientStream(".", BrowserIntegrationService.PipeName, System.IO.Pipes.PipeDirection.Out);
                    pipeClient.Connect(200);
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
                        if (connectTask.Wait(200))
                        {
                            using var stream = client.GetStream();
                            var bodyBytes = Encoding.UTF8.GetBytes(json);
                            var request = $"POST /api/download HTTP/1.1\r\n" +
                                          $"Host: 127.0.0.1:{BrowserIntegrationService.HttpPort}\r\n" +
                                          $"X-PRRX-Token: {ipcToken}\r\n" +
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
                    // Main app not running - launch main process with payload using strict argument lists
                    var appExe = Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(appExe))
                    {
                        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
                        var psi = new ProcessStartInfo
                        {
                            FileName = appExe,
                            UseShellExecute = false
                        };
                        psi.ArgumentList.Add("--payload");
                        psi.ArgumentList.Add(b64);
                        Process.Start(psi);
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
            var sec = new Security.SecurityService();
            var ipcToken = sec.GetOrCreateIpcToken();

            string? url = null;
            string? payloadJson = null;
            bool isSilent = false;

            if (args != null && args.Length > 0)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                        args[i].Equals("--preload", StringComparison.OrdinalIgnoreCase) ||
                        args[i].Equals("--background", StringComparison.OrdinalIgnoreCase) ||
                        args[i].Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                        args[i].Equals("/preload", StringComparison.OrdinalIgnoreCase))
                    {
                        isSilent = true;
                    }
                    else if (args[i] == "--payload" && i + 1 < args.Length)
                    {
                        try
                        {
                            var bytes = Convert.FromBase64String(args[i + 1]);
                            payloadJson = Encoding.UTF8.GetString(bytes);
                        }
                        catch { }
                    }
                    else if (args[i] == "--url" && i + 1 < args.Length)
                    {
                        url = args[i + 1];
                    }
                    else if (args[i].StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                             args[i].StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        url = args[i];
                    }
                }
            }

            BrowserDownloadPayload payloadObj;
            if (!string.IsNullOrWhiteSpace(payloadJson))
            {
                try
                {
                    payloadObj = JsonSerializer.Deserialize<BrowserDownloadPayload>(payloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new BrowserDownloadPayload();
                }
                catch
                {
                    payloadObj = new BrowserDownloadPayload();
                }
            }
            else if (!string.IsNullOrWhiteSpace(url))
            {
                payloadObj = new BrowserDownloadPayload { Action = "download", Url = url };
            }
            else if (isSilent)
            {
                payloadObj = new BrowserDownloadPayload { Action = "sync" };
            }
            else
            {
                payloadObj = new BrowserDownloadPayload { Action = "show" };
            }

            payloadObj.Token = ipcToken;
            var json = JsonSerializer.Serialize(payloadObj);

            // Channel 1: Fast Named Pipe probe (<1ms latency)
            try
            {
                using var pipeClient = new System.IO.Pipes.NamedPipeClientStream(".", BrowserIntegrationService.PipeName, System.IO.Pipes.PipeDirection.Out);
                pipeClient.Connect(150);
                using var writer = new StreamWriter(pipeClient, Encoding.UTF8);
                writer.Write(json);
                writer.Flush();
                return true;
            }
            catch { }

            // Channel 2: Loopback TCP HTTP bridge fallback
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(IPAddress.Loopback, BrowserIntegrationService.HttpPort);
                if (!connectTask.Wait(200)) return false;

                using var stream = client.GetStream();
                stream.WriteTimeout = 1000;
                stream.ReadTimeout = 1000;

                var bodyBytes = Encoding.UTF8.GetBytes(json);
                var request = $"POST /api/download HTTP/1.1\r\n" +
                              $"Host: 127.0.0.1:{BrowserIntegrationService.HttpPort}\r\n" +
                              $"X-PRRX-Token: {ipcToken}\r\n" +
                              $"Content-Type: application/json\r\n" +
                              $"Content-Length: {bodyBytes.Length}\r\n" +
                              $"Connection: close\r\n\r\n";
                var reqBytes = Encoding.UTF8.GetBytes(request);
                stream.Write(reqBytes, 0, reqBytes.Length);
                stream.Write(bodyBytes, 0, bodyBytes.Length);
                stream.Flush();

                var respBuf = new byte[256];
                int read = stream.Read(respBuf, 0, respBuf.Length);
                if (read > 0)
                {
                    var respStr = Encoding.UTF8.GetString(respBuf, 0, read);
                    if (respStr.StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Automatically configures Windows process and network thread scheduling priority.
        /// Defaults to High for optimal multi-socket throughput, falling back gracefully if restricted.
        /// </summary>
        public static void ApplyProcessPriority(ProcessPrioritySetting setting)
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var targetClass = setting switch
                {
                    ProcessPrioritySetting.High => ProcessPriorityClass.High,
                    ProcessPrioritySetting.AboveNormal => ProcessPriorityClass.AboveNormal,
                    ProcessPrioritySetting.Normal => ProcessPriorityClass.Normal,
                    _ => ProcessPriorityClass.High
                };

                try
                {
                    process.PriorityClass = targetClass;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // If running in a restricted or non-elevated user token, fallback gracefully to AboveNormal
                    if (targetClass == ProcessPriorityClass.High)
                    {
                        try
                        {
                            process.PriorityClass = ProcessPriorityClass.AboveNormal;
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[App] Priority allocation warning: {ex.Message}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _telegramBotSyncService?.Stop();
            _browserService?.StopIpcServer();
            base.OnExit(e);
        }
    }
}
