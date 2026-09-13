// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using PRRX.IDM.Models;
using PRRX.IDM.Security;
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
        public string Token { get; set; } = string.Empty;
        public long TotalBytes { get; set; } = 0;
        public List<BatchLinkItem>? Links { get; set; }
    }

    public interface IBrowserIntegrationService
    {
        bool RegisterBrowserHost();
        bool RegisterExtensionInRegistry(string? baseDir = null);
        bool UnregisterBrowserHostAndExtension(string? baseDir = null);
        void StartIpcServer(int? httpPort = null);
        void StopIpcServer();
        void HandleIncomingPayload(BrowserDownloadPayload payload);
        int ActiveHttpPort { get; }
    }

    public class BrowserIntegrationService : IBrowserIntegrationService
    {
        public const string PrimaryExtensionId = "gxmlbxjcnsciidchdsonpfohambheiijcp";
        public const string FixedExtensionId = "mjcomdjfgmiphnekplhmgdepbhafbjal";
        public const string LegacyExtensionId = "jpnkdblibibkbnllncikdeijkbdnmpem";
        public const string PipeName = "PRRX_IDM_IPC_PIPE";
        public const string HostName = "com.prrx.idm";
        public const int DefaultHttpPort = 46543;
        public static int HttpPort => DefaultHttpPort;

        private int _activePort = DefaultHttpPort;
        public int ActiveHttpPort => _activePort;

        private CancellationTokenSource? _cts;
        private TcpListener? _tcpListener;
        private readonly IConfigurationService _configService;
        private readonly ISecurityService _securityService;

        public BrowserIntegrationService(IConfigurationService configService, ISecurityService? securityService = null)
        {
            _configService = configService;
            _securityService = securityService ?? new SecurityService();
        }

        public bool RegisterBrowserHost()
        {
            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
                var exePath = !string.IsNullOrWhiteSpace(currentExe) &&
                              currentExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                              !currentExe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase) &&
                              !currentExe.EndsWith("testhost.exe", StringComparison.OrdinalIgnoreCase)
                    ? currentExe
                    : Path.Combine(appDir, "PRRX.InternetDownloadManager.exe");

                if (!File.Exists(exePath))
                {
                    var publishPath = @"D:\Internet Download Manager\publish\PRRX.InternetDownloadManager.exe";
                    if (File.Exists(publishPath)) exePath = publishPath;
                }

                var manifestDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PRRX Cooperation", "NativeMessaging");
                Directory.CreateDirectory(manifestDir);

                var manifestPath = Path.Combine(manifestDir, $"{HostName}.json");

                // Discover loaded extension IDs from Chrome and Edge profiles
                var discoveredIds = DiscoverInstalledExtensionIds();
                discoveredIds.Add(PrimaryExtensionId);
                discoveredIds.Add(FixedExtensionId);
                discoveredIds.Add(LegacyExtensionId);

                var extPaths = new[]
                {
                    @"D:\Internet Download Manager\extension",
                    @"D:\Internet Download Manager\publish\extension",
                    Path.Combine(appDir, "extension"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extension")
                };

                foreach (var p in extPaths)
                {
                    var id = GenerateExtensionIdFromPath(p);
                    if (!string.IsNullOrEmpty(id)) discoveredIds.Add(id);
                }

                var allowedOriginsBuilder = new StringBuilder();
                int idx = 0;
                foreach (var id in discoveredIds)
                {
                    if (idx > 0) allowedOriginsBuilder.Append(",\n");
                    allowedOriginsBuilder.Append($"    \"chrome-extension://{id}/\"");
                    idx++;
                }

                var manifestContent = $@"{{
  ""name"": ""{HostName}"",
  ""description"": ""PRRX Internet Download Manager Native Messaging Host"",
  ""path"": ""{exePath.Replace("\\", "\\\\")}"",
  ""type"": ""stdio"",
  ""allowed_origins"": [
{allowedOriginsBuilder}
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

                // Automatically register the extension in Chrome and Edge registries
                RegisterExtensionInRegistry(appDir);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to register browser native messaging host: {ex.Message}");
                return false;
            }
        }

        public bool RegisterExtensionInRegistry(string? baseDir = null)
        {
            try
            {
                var appDir = string.IsNullOrWhiteSpace(baseDir) ? AppDomain.CurrentDomain.BaseDirectory : baseDir;
                var extDir = Path.Combine(appDir, "extension");
                if (!Directory.Exists(extDir))
                {
                    var devExt = @"D:\Internet Download Manager\extension";
                    if (Directory.Exists(devExt)) extDir = devExt;
                }

                if (Directory.Exists(extDir))
                {
                    var pathBasedId = GenerateExtensionIdFromPath(extDir);
                    var idsToRegister = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        PrimaryExtensionId,
                        FixedExtensionId,
                        LegacyExtensionId
                    };
                    if (!string.IsNullOrEmpty(pathBasedId)) idsToRegister.Add(pathBasedId);

                    foreach (var id in idsToRegister)
                    {
                        using (var chromeExtKey = Registry.CurrentUser.CreateSubKey($@"Software\Google\Chrome\Extensions\{id}"))
                        {
                            chromeExtKey.SetValue("path", extDir);
                            chromeExtKey.SetValue("version", "1.2.0");
                        }

                        using (var edgeExtKey = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Edge\Extensions\{id}"))
                        {
                            edgeExtKey.SetValue("path", extDir);
                            edgeExtKey.SetValue("version", "1.2.0");
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to register extension in browser registry: {ex.Message}");
                return false;
            }
        }

        public bool UnregisterBrowserHostAndExtension(string? baseDir = null)
        {
            try
            {
                var appDir = string.IsNullOrWhiteSpace(baseDir) ? AppDomain.CurrentDomain.BaseDirectory : baseDir;
                var extDir = Path.Combine(appDir, "extension");
                var pathBasedId = Directory.Exists(extDir) ? GenerateExtensionIdFromPath(extDir) : string.Empty;

                var idsToUnregister = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PrimaryExtensionId,
                    FixedExtensionId,
                    LegacyExtensionId
                };
                if (!string.IsNullOrEmpty(pathBasedId)) idsToUnregister.Add(pathBasedId);

                foreach (var id in idsToUnregister)
                {
                    try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Google\Chrome\Extensions\{id}", false); } catch { }
                    try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Microsoft\Edge\Extensions\{id}", false); } catch { }
                }

                try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Google\Chrome\NativeMessagingHosts\{HostName}", false); } catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Microsoft\Edge\NativeMessagingHosts\{HostName}", false); } catch { }

                var manifestPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PRRX Cooperation", "NativeMessaging", $"{HostName}.json");
                if (File.Exists(manifestPath))
                {
                    try { File.Delete(manifestPath); } catch { }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string GenerateExtensionIdFromPath(string directoryPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directoryPath)) return string.Empty;
                var fullPath = Path.GetFullPath(directoryPath);
                if (fullPath.Length >= 2 && fullPath[1] == ':')
                {
                    fullPath = char.ToUpperInvariant(fullPath[0]) + fullPath.Substring(1);
                }

                var bytes = Encoding.UTF8.GetBytes(fullPath);
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hash = sha256.ComputeHash(bytes);

                var sb = new StringBuilder(32);
                for (int i = 0; i < 16; i++)
                {
                    byte b = hash[i];
                    int high = (b >> 4) & 0x0F;
                    int low = b & 0x0F;
                    sb.Append((char)('a' + high));
                    sb.Append((char)('a' + low));
                }
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private HashSet<string> DiscoverInstalledExtensionIds()
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roots = new[]
            {
                Path.Combine(localAppData, "Google", "Chrome", "User Data"),
                Path.Combine(localAppData, "Microsoft", "Edge", "User Data")
            };

            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    var candidateDirs = new List<string> { Path.Combine(root, "Default") };
                    try
                    {
                        var profileDirs = Directory.GetDirectories(root, "Profile *", SearchOption.TopDirectoryOnly);
                        candidateDirs.AddRange(profileDirs);
                    }
                    catch { }

                    foreach (var dir in candidateDirs)
                    {
                        var prefPath = Path.Combine(dir, "Preferences");
                        if (!File.Exists(prefPath)) continue;
                        try
                        {
                            var content = File.ReadAllText(prefPath);
                            if (content.Contains("PRRX", StringComparison.OrdinalIgnoreCase))
                            {
                                var matches = Regex.Matches(content, @"([a-p]{32})");
                                foreach (Match m in matches)
                                {
                                    results.Add(m.Value);
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return results;
        }

        public void StartIpcServer(int? httpPort = null)
        {
            _cts = new CancellationTokenSource();
            _activePort = httpPort ?? DefaultHttpPort;
            
            // 1. Start Windows Named Pipe IPC listener
            Task.Run(() => IpcServerWorkerAsync(_cts.Token));

            // 2. Start Ultra-Fast Localhost TCP HTTP Server (zero-dependency, no URLACL, no admin requirement)
            Task.Run(() => TcpHttpServerWorkerAsync(_cts.Token));
        }

        public void StopIpcServer()
        {
            _cts?.Cancel();
            try { _tcpListener?.Stop(); } catch { }
        }

        private async Task TcpHttpServerWorkerAsync(CancellationToken token)
        {
            try
            {
                _tcpListener = new TcpListener(IPAddress.Loopback, _activePort);
                _tcpListener.Start();
                if (_tcpListener.LocalEndpoint is IPEndPoint ep)
                {
                    _activePort = ep.Port;
                }

                while (!token.IsCancellationRequested)
                {
                    var client = await _tcpListener.AcceptTcpClientAsync(token);
                    _ = Task.Run(() => HandleTcpClientAsync(client, token), token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"TCP HTTP Server Error: {ex.Message}");
            }
            finally
            {
                try { _tcpListener?.Stop(); } catch { }
            }
        }

        private async Task HandleTcpClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                try
                {
                    stream.ReadTimeout = 4000;
                    stream.WriteTimeout = 4000;

                    var headerBuffer = new byte[8192];
                    int totalHeaderBytes = 0;
                    int headerEndIndex = -1;

                    while (totalHeaderBytes < headerBuffer.Length)
                    {
                        int bytesRead = await stream.ReadAsync(headerBuffer.AsMemory(totalHeaderBytes, headerBuffer.Length - totalHeaderBytes), token);
                        if (bytesRead <= 0) break;
                        totalHeaderBytes += bytesRead;

                        var headerText = Encoding.ASCII.GetString(headerBuffer, 0, totalHeaderBytes);
                        headerEndIndex = headerText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        if (headerEndIndex >= 0) break;
                    }

                    if (headerEndIndex < 0) return;

                    var headersString = Encoding.ASCII.GetString(headerBuffer, 0, headerEndIndex);
                    var lines = headersString.Split("\r\n");
                    if (lines.Length == 0) return;

                    var requestLine = lines[0].Split(' ');
                    var method = requestLine.Length > 0 ? requestLine[0].ToUpperInvariant() : "GET";
                    var path = requestLine.Length > 1 ? requestLine[1] : "/";

                    string origin = string.Empty;
                    string incomingToken = string.Empty;
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("Origin:", StringComparison.OrdinalIgnoreCase))
                        {
                            var parsedOrigin = line.Substring(7).Trim();
                            if (!string.IsNullOrWhiteSpace(parsedOrigin))
                            {
                                origin = parsedOrigin;
                            }
                        }
                        else if (line.StartsWith("X-PRRX-Token:", StringComparison.OrdinalIgnoreCase))
                        {
                            incomingToken = line.Substring(13).Trim();
                        }
                        else if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                        {
                            var authVal = line.Substring(14).Trim();
                            if (authVal.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                            {
                                incomingToken = authVal.Substring(7).Trim();
                            }
                        }
                    }

                    // Security Gate: Check Origin Header to prevent malicious website CSRF attacks
                    if (!string.IsNullOrWhiteSpace(origin) && !_securityService.ValidateOrigin(origin))
                    {
                        var forbiddenBytes = Encoding.UTF8.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(forbiddenBytes.AsMemory(0, forbiddenBytes.Length), token);
                        await stream.FlushAsync(token);
                        return;
                    }

                    string allowedOrigin = !string.IsNullOrWhiteSpace(origin) && _securityService.ValidateOrigin(origin) ? origin : "*";

                    // Handle CORS Preflight
                    if (method == "OPTIONS")
                    {
                        var optResponse = "HTTP/1.1 200 OK\r\n" +
                                          $"Access-Control-Allow-Origin: {allowedOrigin}\r\n" +
                                          "Access-Control-Allow-Methods: POST, GET, OPTIONS\r\n" +
                                          "Access-Control-Allow-Headers: Content-Type, X-Requested-With, Authorization, Accept, Origin, X-PRRX-Token\r\n" +
                                          "Access-Control-Allow-Private-Network: true\r\n" +
                                          "Access-Control-Allow-Credentials: true\r\n" +
                                          "Access-Control-Max-Age: 86400\r\n" +
                                          "Content-Length: 0\r\n" +
                                          "Connection: close\r\n\r\n";
                        var optBytes = Encoding.UTF8.GetBytes(optResponse);
                        await stream.WriteAsync(optBytes.AsMemory(0, optBytes.Length), token);
                        await stream.FlushAsync(token);
                        return;
                    }

                    if (method == "GET" && path.StartsWith("/api/ping", StringComparison.OrdinalIgnoreCase))
                    {
                        var body = "{\"status\":\"online\",\"version\":\"1.2.0\",\"app\":\"PRRX IDM\",\"vault\":\"sealed\"}";
                        var bodyBytes = Encoding.UTF8.GetBytes(body);
                        var response = $"HTTP/1.1 200 OK\r\n" +
                                       $"Access-Control-Allow-Origin: {allowedOrigin}\r\n" +
                                       "Access-Control-Allow-Private-Network: true\r\n" +
                                       "Access-Control-Allow-Credentials: true\r\n" +
                                       "Content-Type: application/json; charset=utf-8\r\n" +
                                       $"Content-Length: {bodyBytes.Length}\r\n" +
                                       "Connection: close\r\n\r\n";
                        var respBytes = Encoding.UTF8.GetBytes(response);
                        await stream.WriteAsync(respBytes.AsMemory(0, respBytes.Length), token);
                        await stream.WriteAsync(bodyBytes.AsMemory(0, bodyBytes.Length), token);
                        await stream.FlushAsync(token);
                        return;
                    }

                    if (method == "POST")
                    {
                        // Parse Content-Length
                        int contentLength = 0;
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            {
                                int.TryParse(line.Substring(15).Trim(), out contentLength);
                            }
                        }

                        if (contentLength <= 0 || contentLength > 10 * 1024 * 1024)
                        {
                            var badLen = Encoding.UTF8.GetBytes($"HTTP/1.1 400 Bad Request\r\nAccess-Control-Allow-Origin: {allowedOrigin}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                            await stream.WriteAsync(badLen.AsMemory(0, badLen.Length), token);
                            await stream.FlushAsync(token);
                            return;
                        }

                        // Read remaining body bytes
                        int bodyBytesAlreadyRead = totalHeaderBytes - (headerEndIndex + 4);
                        var bodyBuffer = new byte[contentLength];
                        if (bodyBytesAlreadyRead > 0)
                        {
                            Array.Copy(headerBuffer, headerEndIndex + 4, bodyBuffer, 0, Math.Min(bodyBytesAlreadyRead, contentLength));
                        }

                        int bodyReadTotal = Math.Min(bodyBytesAlreadyRead, contentLength);
                        while (bodyReadTotal < contentLength)
                        {
                            int read = await stream.ReadAsync(bodyBuffer.AsMemory(bodyReadTotal, contentLength - bodyReadTotal), token);
                            if (read <= 0) break;
                            bodyReadTotal += read;
                        }

                        var json = Encoding.UTF8.GetString(bodyBuffer, 0, bodyReadTotal);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            var payload = JsonSerializer.Deserialize<BrowserDownloadPayload>(json, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });

                            if (payload != null)
                            {
                                // Strict Authentication Gate:
                                // Request MUST either originate from an authorized browser extension (valid origin)
                                // OR supply a cryptographically valid local IPC token.
                                bool hasValidOrigin = !string.IsNullOrWhiteSpace(origin) && _securityService.ValidateOrigin(origin);
                                string tokenCandidate = !string.IsNullOrWhiteSpace(payload.Token) ? payload.Token : incomingToken;
                                bool hasValidToken = !string.IsNullOrWhiteSpace(tokenCandidate) && _securityService.ValidateIpcToken(tokenCandidate);

                                if (!hasValidOrigin && !hasValidToken)
                                {
                                    var badAuth = Encoding.UTF8.GetBytes($"HTTP/1.1 401 Unauthorized\r\nAccess-Control-Allow-Origin: {allowedOrigin}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                                    await stream.WriteAsync(badAuth.AsMemory(0, badAuth.Length), token);
                                    await stream.FlushAsync(token);
                                    return;
                                }

                                if (Application.Current != null)
                                {
                                    _ = Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, new Action(() =>
                                    {
                                        HandleIncomingPayload(payload);
                                    }));
                                }

                                var body = "{\"status\":\"ok\",\"message\":\"Download initiated in PRRX IDM\"}";
                                var bodyBytes = Encoding.UTF8.GetBytes(body);
                                var response = "HTTP/1.1 200 OK\r\n" +
                                               $"Access-Control-Allow-Origin: {allowedOrigin}\r\n" +
                                               "Access-Control-Allow-Private-Network: true\r\n" +
                                               "Access-Control-Allow-Credentials: true\r\n" +
                                               "Content-Type: application/json; charset=utf-8\r\n" +
                                               $"Content-Length: {bodyBytes.Length}\r\n" +
                                               "Connection: close\r\n\r\n";
                                var respBytes = Encoding.UTF8.GetBytes(response);
                                await stream.WriteAsync(respBytes.AsMemory(0, respBytes.Length), token);
                                await stream.WriteAsync(bodyBytes.AsMemory(0, bodyBytes.Length), token);
                                await stream.FlushAsync(token);
                                return;
                            }
                        }
                    }

                    // Default Bad Request
                    var badResp = Encoding.UTF8.GetBytes($"HTTP/1.1 400 Bad Request\r\nAccess-Control-Allow-Origin: {allowedOrigin}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(badResp.AsMemory(0, badResp.Length), token);
                    await stream.FlushAsync(token);
                }
                catch { }
            }
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
                            // Named Pipe Security Gate: Require cryptographically valid IPC token
                            if (!_securityService.ValidateIpcToken(payload.Token))
                            {
                                Debug.WriteLine("Named pipe IPC request rejected: Invalid or missing token.");
                                continue;
                            }

                            if (Application.Current != null)
                            {
                                _ = Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, new Action(() =>
                                {
                                    HandleIncomingPayload(payload);
                                }));
                            }
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

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        public void HandleIncomingPayload(BrowserDownloadPayload payload)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, new Action(() => HandleIncomingPayload(payload)));
                return;
            }

            if (payload.Action == "show" || payload.Action == "activate")
            {
                if (Application.Current?.MainWindow != null)
                {
                    Application.Current.MainWindow.ShowInTaskbar = true;
                    Application.Current.MainWindow.Visibility = Visibility.Visible;
                    Application.Current.MainWindow.WindowState = WindowState.Normal;
                    Application.Current.MainWindow.Show();
                    Application.Current.MainWindow.Activate();
                    Application.Current.MainWindow.Focus();
                }
                return;
            }

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

                // Do NOT set dlg.Owner = MainWindow to prevent unminimizing or popping up MainWindow
                dlg.Topmost = true;
                dlg.Show();
                dlg.Topmost = false;
                dlg.Activate();
                dlg.Focus();
                try
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(dlg);
                    helper.EnsureHandle();
                    SetForegroundWindow(helper.Handle);
                    BringWindowToTop(helper.Handle);
                }
                catch { }

                dlg.Closed += (_, _) =>
                {
                    MemoryOptimizer.TrimMemory();
                };
            }
            else if (!string.IsNullOrWhiteSpace(payload.Url))
            {
                var vm = new DownloadFileInfoViewModel(payload.Url, defaultDir, payload.PageTitle, payload.TotalBytes);
                if (!string.IsNullOrWhiteSpace(payload.FileName)) vm.FileName = payload.FileName;

                var dlg = new DownloadFileInfoDialog(vm);

                // Do NOT set dlg.Owner = MainWindow to prevent unminimizing or popping up MainWindow
                dlg.Topmost = true;
                dlg.Show();
                dlg.Topmost = false;
                dlg.Activate();
                dlg.Focus();
                try
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(dlg);
                    helper.EnsureHandle();
                    SetForegroundWindow(helper.Handle);
                    BringWindowToTop(helper.Handle);
                }
                catch { }

                dlg.Closed += (_, _) =>
                {
                    if (vm.DialogResult == DownloadDialogResult.StartNow)
                    {
                        var activeVm = new ActiveDownloadViewModel(vm.Url, vm.SaveAsFullPath);
                        var activeWin = new ActiveDownloadWindow(activeVm);
                        activeWin.Closed += (_, _) => MemoryOptimizer.TrimMemory();
                        activeWin.Show();
                        activeWin.Activate();
                    }
                    else
                    {
                        MemoryOptimizer.TrimMemory();
                    }
                };
            }
        }
    }
}
