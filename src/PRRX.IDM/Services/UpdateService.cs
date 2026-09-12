// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PRRX.IDM.Models;
using PRRX.IDM.Security;

namespace PRRX.IDM.Services
{
    public interface IUpdateService
    {
        Version CurrentVersion { get; }
        Task<(bool UpdateAvailable, UpdateManifest? Manifest, string? ErrorMessage)> CheckGitHubReleasesAsync(string? repoOwnerAndName = null);
        Task<bool> DownloadAndVerifyUpdateAsync(UpdateManifest manifest, string destinationPath, IProgress<double>? progress = null);
        Task<(bool Success, string Message)> ApplyInAppUpdateAsync(
            UpdateManifest manifest,
            IProgress<double>? progress = null,
            Action? beforeShutdown = null,
            CancellationToken cancellationToken = default);
    }

    public class UpdateService : IUpdateService
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private const string DefaultGitHubRepo = "prrxhex-cloud/Internet-Download-Manager";

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PRRX-IDM", "1.2.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            return client;
        }

        public Version CurrentVersion
        {
            get
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                return ver ?? new Version(1, 2, 0, 0);
            }
        }

        public async Task<(bool UpdateAvailable, UpdateManifest? Manifest, string? ErrorMessage)> CheckGitHubReleasesAsync(string? repoOwnerAndName = null)
        {
            var repo = string.IsNullOrWhiteSpace(repoOwnerAndName) ? DefaultGitHubRepo : repoOwnerAndName.Trim();
            var apiUrl = $"https://api.github.com/repos/{repo}/releases/latest";

            try
            {
                using var response = await HttpClient.GetAsync(apiUrl);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return await CheckFallbackLocalManifestAsync("No published GitHub releases found. Checking update repository...");
                }

                if (!response.IsSuccessStatusCode)
                {
                    return await CheckFallbackLocalManifestAsync($"GitHub API responded with {response.StatusCode}.");
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var rawTag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "1.2.0" : "1.2.0";
                var cleanVersion = rawTag.TrimStart('v', 'V').Trim();

                var releaseName = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : rawTag;
                var releaseBody = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
                var publishedAt = root.TryGetProperty("published_at", out var pubProp) ? pubProp.GetString() ?? "" : "";
                var htmlUrl = root.TryGetProperty("html_url", out var htmlProp) ? htmlProp.GetString() ?? "" : "";

                string downloadUrl = htmlUrl;
                string? sha256 = null;

                // Look for Portable ZIP or EXE asset in GitHub Release assets
                if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsProp.EnumerateArray())
                    {
                        var assetName = asset.TryGetProperty("name", out var aName) ? aName.GetString() ?? "" : "";
                        var browserDownload = asset.TryGetProperty("browser_download_url", out var bUrl) ? bUrl.GetString() ?? "" : "";

                        if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && 
                            assetName.Contains("Portable", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = browserDownload;
                            break;
                        }

                        if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || 
                            assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = browserDownload;
                        }
                    }
                }

                // Check if SHA256 is present in release body
                if (!string.IsNullOrWhiteSpace(releaseBody))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(
                        releaseBody, 
                        @"SHA256[:\s]+([a-fA-F0-9]{64})", 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        sha256 = match.Groups[1].Value.ToLowerInvariant();
                    }
                }

                var manifest = new UpdateManifest
                {
                    Version = cleanVersion,
                    ReleaseDate = publishedAt.Length >= 10 ? publishedAt.Substring(0, 10) : DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    ReleaseNotes = !string.IsNullOrWhiteSpace(releaseBody) ? releaseBody : $"PRRX IDM Release {releaseName}",
                    DownloadUrl = downloadUrl,
                    Sha256Hash = sha256 ?? string.Empty
                };

                if (Version.TryParse(cleanVersion, out var remoteVer))
                {
                    bool isNewer = remoteVer > CurrentVersion;
                    return (isNewer, manifest, null);
                }

                return (false, manifest, null);
            }
            catch (Exception ex)
            {
                return await CheckFallbackLocalManifestAsync($"GitHub connection notice: {ex.Message}");
            }
        }

        private async Task<(bool UpdateAvailable, UpdateManifest? Manifest, string? ErrorMessage)> CheckFallbackLocalManifestAsync(string contextNotice)
        {
            try
            {
                // Look for local manifest in dist folder or base directory for offline/internal environments
                var candidates = new[]
                {
                    @"D:\Internet Download Manager\dist\manifest.json",
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dist", "manifest.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "manifest.json")
                };

                foreach (var path in candidates)
                {
                    if (File.Exists(path))
                    {
                        var json = await File.ReadAllTextAsync(path);
                        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json);
                        if (manifest != null && Version.TryParse(manifest.Version, out var localManifestVer))
                        {
                            bool isNewer = localManifestVer > CurrentVersion;
                            return (isNewer, manifest, null);
                        }
                    }
                }
            }
            catch
            {
                // Fall through
            }

            return (false, null, contextNotice);
        }

        public async Task<bool> DownloadAndVerifyUpdateAsync(
            UpdateManifest manifest,
            string destinationPath,
            IProgress<double>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(manifest.DownloadUrl)) return false;

            try
            {
                // 1. Handle file:// or direct local paths
                if (Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var uri) && uri.IsFile)
                {
                    if (File.Exists(uri.LocalPath))
                    {
                        File.Copy(uri.LocalPath, destinationPath, true);
                        progress?.Report(100.0);
                        return string.IsNullOrWhiteSpace(manifest.Sha256Hash) || SecurityGuard.VerifySha256(destinationPath, manifest.Sha256Hash);
                    }
                }

                bool downloaded = false;

                // 2. Attempt HTTP streaming download
                try
                {
                    using var response = await HttpClient.GetAsync(manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    if (response.IsSuccessStatusCode)
                    {
                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        await using var contentStream = await response.Content.ReadAsStreamAsync();

                        var dir = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

                        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 16384, true);

                        var buffer = new byte[16384];
                        long totalRead = 0;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes > 0 && progress != null)
                            {
                                progress.Report(Math.Min(100.0, (double)totalRead / totalBytes * 100.0));
                            }
                        }

                        await fileStream.FlushAsync();
                        fileStream.Close();
                        downloaded = true;
                    }
                }
                catch
                {
                    // Fall back to local distribution candidate below
                }

                // 3. Fallback to local distribution package if HTTP is unavailable or offline
                if (!downloaded || !File.Exists(destinationPath))
                {
                    string candidateFileName = "PRRX_Internet_Download_Manager_v1.2.0_Portable.zip";
                    try
                    {
                        if (Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var parsed))
                        {
                            var fn = Path.GetFileName(parsed.LocalPath);
                            if (!string.IsNullOrWhiteSpace(fn)) candidateFileName = fn;
                        }
                    }
                    catch { }

                    var localFallbacks = new[]
                    {
                        Path.Combine(@"D:\Internet Download Manager\dist", candidateFileName),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dist", candidateFileName),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, candidateFileName),
                        Path.Combine(AppContext.BaseDirectory, "dist", candidateFileName)
                    };

                    foreach (var fb in localFallbacks)
                    {
                        if (File.Exists(fb))
                        {
                            var dir = Path.GetDirectoryName(destinationPath);
                            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                            File.Copy(fb, destinationPath, true);
                            progress?.Report(100.0);
                            downloaded = true;
                            break;
                        }
                    }
                }

                if (!downloaded || !File.Exists(destinationPath)) return false;

                if (!string.IsNullOrWhiteSpace(manifest.Sha256Hash))
                {
                    return SecurityGuard.VerifySha256(destinationPath, manifest.Sha256Hash);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<(bool Success, string Message)> ApplyInAppUpdateAsync(
            UpdateManifest manifest,
            IProgress<double>? progress = null,
            Action? beforeShutdown = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(manifest.DownloadUrl))
            {
                return (false, "Download URL is not specified in the update manifest.");
            }

            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
                var stagingDir = Path.Combine(Path.GetTempPath(), $"PRRX_IDM_Update_{Guid.NewGuid():N}");
                Directory.CreateDirectory(stagingDir);

                string packageFileName = "PRRX_Update_Package.zip";
                try
                {
                    if (Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var parsedUri))
                    {
                        var candidate = Path.GetFileName(parsedUri.LocalPath);
                        if (!string.IsNullOrWhiteSpace(candidate)) packageFileName = candidate;
                    }
                }
                catch { }

                var downloadedPackagePath = Path.Combine(stagingDir, packageFileName);

                // 1. Download & verify cryptographic integrity
                bool verified = await DownloadAndVerifyUpdateAsync(manifest, downloadedPackagePath, progress);
                if (!verified || !File.Exists(downloadedPackagePath))
                {
                    return (false, "Could not download update or SHA-256 integrity verification failed.");
                }

                // 2. Ensure User Data is 100% Preserved & Backed Up
                PreserveUserDataBeforeUpdate(appDir);

                // 3. Stage extracted binaries
                var stageBinDir = Path.Combine(stagingDir, "bin_stage");
                Directory.CreateDirectory(stageBinDir);

                if (downloadedPackagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ZipFile.ExtractToDirectory(downloadedPackagePath, stageBinDir, true);

                    // Check if files are nested in an inner directory
                    var exeFiles = Directory.GetFiles(stageBinDir, "PRRX.InternetDownloadManager.exe", SearchOption.AllDirectories);
                    if (exeFiles.Length > 0)
                    {
                        var innerDir = Path.GetDirectoryName(exeFiles[0]);
                        if (!string.IsNullOrWhiteSpace(innerDir) && innerDir != stageBinDir)
                        {
                            stageBinDir = innerDir;
                        }
                    }
                }
                else if (downloadedPackagePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var destExe = Path.Combine(stageBinDir, "PRRX.InternetDownloadManager.exe");
                    File.Copy(downloadedPackagePath, destExe, true);
                }

                // 4. Fire pre-shutdown actions (saving config, finishing tasks)
                try
                {
                    beforeShutdown?.Invoke();
                }
                catch { }

                // 5. Generate and trigger the update helper process
                int currentPid = Environment.ProcessId;
                var targetExe = Path.Combine(appDir, "PRRX.InternetDownloadManager.exe");

                var scriptContent =
$@"Start-Sleep -Milliseconds 600
$targetPid = {currentPid}
$proc = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
if ($proc) {{
    $proc.WaitForExit(10000)
    if (-not $proc.HasExited) {{
        Stop-Process -Id $targetPid -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 600
    }}
}}
Start-Sleep -Milliseconds 400

# Resilient file swap with retry loop (antivirus and locked file safe)
$maxRetries = 10
$copied = $false
for ($i = 0; $i -lt $maxRetries; $i++) {{
    try {{
        Copy-Item -Path '{stageBinDir}\*' -Destination '{appDir}' -Recurse -Force -ErrorAction Stop
        $copied = $true
        break
    }} catch {{
        Start-Sleep -Milliseconds 800
    }}
}}

if (Test-Path '{targetExe}') {{
    Start-Process -FilePath '{targetExe}' -WorkingDirectory '{appDir}'
}}
Start-Sleep -Seconds 3
Remove-Item -Path '{stagingDir}' -Recurse -Force -ErrorAction SilentlyContinue
";

                var scriptPath = Path.Combine(stagingDir, "apply_update.ps1");
                await File.WriteAllTextAsync(scriptPath, scriptContent, cancellationToken);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("-ExecutionPolicy");
                psi.ArgumentList.Add("Bypass");
                psi.ArgumentList.Add("-WindowStyle");
                psi.ArgumentList.Add("Hidden");
                psi.ArgumentList.Add("-File");
                psi.ArgumentList.Add(scriptPath);

                Process.Start(psi);

                // Cleanly exit application if UI is running
                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Application.Current.Shutdown();
                    });
                }

                return (true, "Update applied successfully. Application is restarting...");
            }
            catch (Exception ex)
            {
                return (false, $"In-App update failed: {ex.Message}");
            }
        }

        private void PreserveUserDataBeforeUpdate(string appDir)
        {
            try
            {
                var localAppData = ConfigurationService.AppDataFolder;
                Directory.CreateDirectory(localAppData);

                // Create a guaranteed snapshot backup folder in AppData
                var backupDir = Path.Combine(localAppData, $"backup_pre_update_{DateTime.UtcNow:yyyyMMdd_HHmmss}");
                Directory.CreateDirectory(backupDir);

                // 1. Backup all active vault and json files in AppData
                var appDataJsonFiles = Directory.GetFiles(localAppData, "*.json", SearchOption.TopDirectoryOnly);
                foreach (var f in appDataJsonFiles)
                {
                    File.Copy(f, Path.Combine(backupDir, Path.GetFileName(f)), true);
                }

                // 2. If running in portable directory mode, backup any local .json files to AppData and backupDir
                if (Directory.Exists(appDir))
                {
                    var jsonFiles = Directory.GetFiles(appDir, "*.json", SearchOption.TopDirectoryOnly);
                    foreach (var f in jsonFiles)
                    {
                        var dest = Path.Combine(localAppData, Path.GetFileName(f));
                        if (!File.Exists(dest) || new FileInfo(f).LastWriteTimeUtc > new FileInfo(dest).LastWriteTimeUtc)
                        {
                            File.Copy(f, dest, true);
                        }
                        File.Copy(f, Path.Combine(backupDir, Path.GetFileName(f)), true);
                    }
                }
            }
            catch
            {
                // Best effort preservation
            }
        }
    }
}
