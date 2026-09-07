using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using PRRX.IDM.Models;
using PRRX.IDM.ViewModels;
using PRRX.IDM.Views;

namespace PRRX.IDM.Services
{
    public class BrowserDownloadPayload
    {
        public string Action { get; set; } = "download"; // "download" or "batch"
        public string Url { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string PageTitle { get; set; } = string.Empty;
        public string Cookies { get; set; } = string.Empty;
        public List<BatchLinkItem>? Links { get; set; }
    }

    public interface IBrowserIntegrationService
    {
        bool RegisterBrowserHost();
        void StartIpcServer();
        void StopIpcServer();
    }

    public class BrowserIntegrationService : IBrowserIntegrationService
    {
        public const string PipeName = "PRRX_IDM_IPC_PIPE";
        public const string HostName = "com.prrx.idm";

        private CancellationTokenSource? _cts;
        private readonly IConfigurationService _configService;

        public BrowserIntegrationService(IConfigurationService configService)
        {
            _configService = configService;
        }

        public bool RegisterBrowserHost()
        {
            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(appDir, "PRRX.InternetDownloadManager.exe");
                var manifestDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PRRX Cooperation", "NativeMessaging");
                Directory.CreateDirectory(manifestDir);

                var manifestPath = Path.Combine(manifestDir, $"{HostName}.json");

                var manifestContent = $@"{{
  ""name"": ""{HostName}"",
  ""description"": ""PRRX Internet Download Manager Native Messaging Host"",
  ""path"": ""{exePath.Replace("\\", "\\\\")}"",
  ""type"": ""stdio"",
  ""allowed_origins"": [
    ""chrome-extension://*/*""
  ]
}}";

                File.WriteAllText(manifestPath, manifestContent, Encoding.UTF8);

                // Register for Google Chrome
                using (var chromeKey = Registry.CurrentUser.CreateSubKey($@"Software\Google\Chrome\NativeMessagingHosts\{HostName}"))
                {
                    chromeKey.SetValue("", manifestPath);
                }

                // Register for Microsoft Edge
                using (var edgeKey = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Edge\NativeMessagingHosts\{HostName}"))
                {
                    edgeKey.SetValue("", manifestPath);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to register browser native messaging host: {ex.Message}");
                return false;
            }
        }

        public void StartIpcServer()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => IpcServerWorkerAsync(_cts.Token));
        }

        public void StopIpcServer()
        {
            _cts?.Cancel();
        }

        private async Task IpcServerWorkerAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var json = await reader.ReadToEndAsync(token);

                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var payload = JsonSerializer.Deserialize<BrowserDownloadPayload>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (payload != null)
                        {
                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                HandleIncomingPayload(payload);
                            });
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"IPC Error: {ex.Message}");
                    await Task.Delay(500, token);
                }
            }
        }

        private void HandleIncomingPayload(BrowserDownloadPayload payload)
        {
            var defaultDir = _configService.CurrentConfig.DownloadDirectory;
            if (string.IsNullOrWhiteSpace(defaultDir))
            {
                defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }

            if (payload.Action == "batch" && payload.Links != null && payload.Links.Count > 0)
            {
                var req = new BatchDownloadRequest
                {
                    SourcePageUrl = payload.Url,
                    PageTitle = string.IsNullOrWhiteSpace(payload.PageTitle) ? "Web Page" : payload.PageTitle,
                    Links = payload.Links
                };

                var vm = new BatchDownloadViewModel(req, defaultDir);
                var dlg = new BatchDownloadDialog(vm);
                dlg.Show();
            }
            else if (!string.IsNullOrWhiteSpace(payload.Url))
            {
                var vm = new DownloadFileInfoViewModel(payload.Url, defaultDir);
                if (!string.IsNullOrWhiteSpace(payload.FileName)) vm.FileName = payload.FileName;

                var dlg = new DownloadFileInfoDialog(vm);
                dlg.Show();
                dlg.Activate();

                dlg.Closed += (_, _) =>
                {
                    if (vm.DialogResult == DownloadDialogResult.StartNow)
                    {
                        var activeVm = new ActiveDownloadViewModel(vm.Url, vm.SaveAsFullPath);
                        var activeWin = new ActiveDownloadWindow(activeVm);
                        activeWin.Show();
                    }
                };
            }
        }
    }
}
