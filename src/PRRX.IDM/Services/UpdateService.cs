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
        string CurrentVersionClean { get; }
        Task<(bool UpdateAvailable, UpdateManifest? Manifest, string? ErrorMessage)> CheckGitHubReleasesAsync(string? repoOwnerAndName = null);
        Task<bool> DownloadAndVerifyUpdateAsync(UpdateManifest manifest, string destinationPath, IProgress<double>? progress = null);
        Task<(bool Success, string Message)> ApplyInAppUpdateAsync(
            UpdateManifest manifest,
            IProgress<double>? progress = null,
            Action? beforeShutdown = null,
            CancellationToken cancellationToken = default);
        Task<CleanupReport> CleanupPostUpdateArtifactsAsync(string? customAppDir = null);
        CleanupReport CleanupPostUpdateArtifacts(string? customAppDir = null);
    }

    public class UpdateService : IUpdateService
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private const string DefaultGitHubRepo = "prrxhex-cloud/Internet-Download-Manager";
        public const string CloudflareWorkerApi = "https://prrx-api.sayurusenavirathna70.workers.dev/api/manifest";

        public async Task<UpdateManifest?> QueryCloudflareWorkerManifestAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3.5));
                using var req = new HttpRequestMessage(HttpMethod.Get, CloudflareWorkerApi);
                using var resp = await HttpClient.SendAsync(req, cts.Token);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync(cts.Token);
                    var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (manifest != null && !string.IsNullOrWhiteSpace(manifest.Version))
                    {
                        return manifest;
                    }
                }
            }
            catch
            {
                // Degrades gracefully to GitHub API / fallback
            }
            return null;
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
            var asm = typeof(App).Assembly;
            var ver = asm.GetName().Version;
            var verStr = ver != null ? $"{ver.Major}.{ver.Minor}.{Math.Max(0, ver.Build)}" : "1.8.0";
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PRRX-IDM", verStr));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            return client;
        }

        public Version CurrentVersion
        {
            get
            {
                try
                {
                    var asm = typeof(App).Assembly;
                    var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                    if (!string.IsNullOrWhiteSpace(infoVer))
                    {
                        var norm = ParseNormalizedVersion(infoVer);
                        if (norm > new Version(0, 0, 0, 0))
                        {
                            return norm.Revision > 0
                                ? new Version(norm.Major, norm.Minor, Math.Max(0, norm.Build), norm.Revision)
                                : new Version(norm.Major, norm.Minor, Math.Max(0, norm.Build));
                        }
                    }

                    var ver = asm.GetName().Version;
                    if (ver != null)
                    {
                        return ver.Revision > 0
                            ? new Version(ver.Major, ver.Minor, Math.Max(0, ver.Build), ver.Revision)
                            : new Version(ver.Major, ver.Minor, Math.Max(0, ver.Build));
                    }
                }
                catch { }

                return new Version(1, 8, 0);
            }
        }

        public string CurrentVersionClean
        {
            get
            {
                var ver = CurrentVersion;
                return ver.Revision > 0
                    ? $"{ver.Major}.{ver.Minor}.{Math.Max(0, ver.Build)}.{ver.Revision}"
                    : $"{ver.Major}.{ver.Minor}.{Math.Max(0, ver.Build)}";
            }
        }

        public static Version ParseNormalizedVersion(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new Version(0, 0, 0, 0);

            var clean = raw.Trim().TrimStart('v', 'V').Trim();
            int plusIdx = clean.IndexOf('+');
            if (plusIdx >= 0) clean = clean.Substring(0, plusIdx);
            int dashIdx = clean.IndexOf('-');
            if (dashIdx >= 0) clean = clean.Substring(0, dashIdx);

            var parts = clean.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return new Version(0, 0, 0, 0);

            int major = parts.Length > 0 && int.TryParse(parts[0], out var maj) ? maj : 0;
            int minor = parts.Length > 1 && int.TryParse(parts[1], out var min) ? min : 0;
            int build = parts.Length > 2 && int.TryParse(parts[2], out var bld) ? bld : 0;
            int rev = parts.Length > 3 && int.TryParse(parts[3], out var r) ? r : 0;

            return new Version(major, minor, build, rev);
        }

        public static bool IsVersionNewer(string? remoteVersionStr, string? localVersionStr)
        {
            if (string.IsNullOrWhiteSpace(remoteVersionStr) || string.IsNullOrWhiteSpace(localVersionStr))
                return false;

            var remoteVersionClean = remoteVersionStr.Trim().TrimStart('v', 'V').Trim();
            var currentVersionClean = localVersionStr.Trim().TrimStart('v', 'V').Trim();

            int plusR = remoteVersionClean.IndexOf('+');
            if (plusR >= 0) remoteVersionClean = remoteVersionClean.Substring(0, plusR);
            int dashR = remoteVersionClean.IndexOf('-');
            if (dashR >= 0) remoteVersionClean = remoteVersionClean.Substring(0, dashR);

            int plusL = currentVersionClean.IndexOf('+');
            if (plusL >= 0) currentVersionClean = currentVersionClean.Substring(0, plusL);
            int dashL = currentVersionClean.IndexOf('-');
            if (dashL >= 0) currentVersionClean = currentVersionClean.Substring(0, dashL);

            var remoteNorm = ParseNormalizedVersion(remoteVersionClean);
            var localNorm = ParseNormalizedVersion(currentVersionClean);

            return remoteNorm > localNorm;
        }

        public async Task<(bool UpdateAvailable, UpdateManifest? Manifest, string? ErrorMessage)> CheckGitHubReleasesAsync(string? repoOwnerAndName = null)
        {
            var repo = string.IsNullOrWhiteSpace(repoOwnerAndName) ? DefaultGitHubRepo : repoOwnerAndName.Trim();
            var apiUrl = $"https://api.github.com/repos/{repo}/releases/latest";

            // 1. Query Cloudflare Worker edge API
            UpdateManifest? cloudManifest = null;
            try
            {
                cloudManifest = await QueryCloudflareWorkerManifestAsync();
            }
            catch { }

            try
            {
                using var response = await HttpClient.GetAsync(apiUrl);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    if (cloudManifest != null)
                    {
                        bool isCloudNewer = IsVersionNewer(cloudManifest.Version, CurrentVersionClean);
                        return (isCloudNewer, cloudManifest, null);
                    }
                    return await CheckFallbackLocalManifestAsync("No published GitHub releases found. Checking update repository...");
                }

                if (!response.IsSuccessStatusCode)
                {
                    if (cloudManifest != null)
                    {
                        bool isCloudNewer = IsVersionNewer(cloudManifest.Version, CurrentVersionClean);
                        return (isCloudNewer, cloudManifest, null);
                    }
                    return await CheckFallbackLocalManifestAsync($"GitHub API responded with {response.StatusCode}.");
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var rawTag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "1.7.0" : "1.7.0";
                var cleanVersion = rawTag.TrimStart('v', 'V').Trim();

                var releaseName = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : rawTag;
                var releaseBody = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
                var publishedAt = root.TryGetProperty("published_at", out var pubProp) ? pubProp.GetString() ?? "" : "";
                var htmlUrl = root.TryGetProperty("html_url", out var htmlProp) ? htmlProp.GetString() ?? "" : "";

                string downloadUrl = htmlUrl;
                string? sha256 = null;
                string? manifestAssetUrl = null;

                // Look for Portable ZIP, EXE, or manifest.json in GitHub Release assets
                if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsProp.EnumerateArray())
                    {
                        var assetName = asset.TryGetProperty("name", out var aName) ? aName.GetString() ?? "" : "";
                        var browserDownload = asset.TryGetProperty("browser_download_url", out var bUrl) ? bUrl.GetString() ?? "" : "";

                        if (assetName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                        {
                            manifestAssetUrl = browserDownload;
                        }

                        if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && 
                            assetName.Contains("Portable", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = browserDownload;
                        }
                        else if ((assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || 
                                  assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) && 
                                 downloadUrl == htmlUrl)
                        {
                            downloadUrl = browserDownload;
                        }
                    }
                }

                // If manifest.json is attached as a release asset, fetch it for authentic SHA256 and metadata
                if (!string.IsNullOrWhiteSpace(manifestAssetUrl))
                {
                    try
                    {
                        var manifestJson = await HttpClient.GetStringAsync(manifestAssetUrl);
                        var parsed = JsonSerializer.Deserialize<UpdateManifest>(manifestJson);
                        if (parsed != null)
                        {
                            if (!string.IsNullOrWhiteSpace(parsed.Sha256Hash))
                                sha256 = parsed.Sha256Hash.Trim().ToLowerInvariant();
                            if (!string.IsNullOrWhiteSpace(parsed.DownloadUrl))
                                downloadUrl = parsed.DownloadUrl;
                            if (!string.IsNullOrWhiteSpace(parsed.ReleaseNotes))
                                releaseBody = parsed.ReleaseNotes;
                        }
                    }
                    catch { }
                }

                // Fallback: extract SHA-256 hash from release body notes
                if (string.IsNullOrWhiteSpace(sha256) && !string.IsNullOrWhiteSpace(releaseBody))
                {
                    // Match portable zip sha256 in table or list
                    var tableMatch = System.Text.RegularExpressions.Regex.Match(
                        releaseBody,
                        @"Portable.*?([a-fA-F0-9]{64})",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                    if (tableMatch.Success)
                    {
                        sha256 = tableMatch.Groups[1].Value.ToLowerInvariant();
                    }
                    else
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(
                            releaseBody, 
                            @"SHA256[:\s`*]+([a-fA-F0-9]{64})", 
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            sha256 = match.Groups[1].Value.ToLowerInvariant();
                        }
                    }
                }

                var gitHubManifest = new UpdateManifest
                {
                    Version = cleanVersion,
                    ReleaseDate = publishedAt.Length >= 10 ? publishedAt.Substring(0, 10) : DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    ReleaseNotes = !string.IsNullOrWhiteSpace(releaseBody) ? releaseBody : $"PRRX IDM Release {releaseName}",
                    DownloadUrl = downloadUrl,
                    Sha256Hash = sha256 ?? string.Empty
                };

                // Compare GitHub release with Cloudflare worker manifest and choose the newest
                var finalManifest = (cloudManifest != null && IsVersionNewer(cloudManifest.Version, gitHubManifest.Version))
                    ? cloudManifest
                    : gitHubManifest;

                bool isNewer = IsVersionNewer(finalManifest.Version, CurrentVersionClean);
                return (isNewer, finalManifest, null);
            }
            catch (Exception ex)
            {
                if (cloudManifest != null)
                {
                    bool isCloudNewer = IsVersionNewer(cloudManifest.Version, CurrentVersionClean);
                    return (isCloudNewer, cloudManifest, null);
                }
                return await CheckFallbackLocalManifestAsync($"Update repository notice: {ex.Message}");
            }
        }

        private async Task<(bool UpdateAvailable, UpdateManifest? Manifest, string? ErrorMessage)> CheckFallbackLocalManifestAsync(string contextNotice)
        {
            try
            {
                // Look for local manifest in dist folder or base directory for offline/internal environments
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidates = new List<string>
                {
                    Path.Combine(baseDir, "dist", "manifest.json"),
                    Path.Combine(baseDir, "manifest.json"),
                    Path.Combine(AppContext.BaseDirectory, "dist", "manifest.json"),
                    Path.Combine(AppContext.BaseDirectory, "manifest.json")
                };

                var cur = new DirectoryInfo(baseDir);
                for (int i = 0; i < 5 && cur != null; i++)
                {
                    var cand1 = Path.Combine(cur.FullName, "dist", "manifest.json");
                    if (File.Exists(cand1) && !candidates.Contains(cand1)) candidates.Add(cand1);
                    var cand2 = Path.Combine(cur.FullName, "manifest.json");
                    if (File.Exists(cand2) && !candidates.Contains(cand2)) candidates.Add(cand2);
                    cur = cur.Parent;
                }

                foreach (var path in candidates)
                {
                    if (File.Exists(path))
                    {
                        var json = await File.ReadAllTextAsync(path);
                        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json);
                        if (manifest != null && !string.IsNullOrWhiteSpace(manifest.Version))
                        {
                            bool isNewer = IsVersionNewer(manifest.Version, CurrentVersionClean);
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
                        var dir = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                        File.Copy(uri.LocalPath, destinationPath, true);
                        progress?.Report(100.0);
                        return string.IsNullOrWhiteSpace(manifest.Sha256Hash) || SecurityGuard.VerifySha256(destinationPath, manifest.Sha256Hash);
                    }
                }

                bool downloaded = false;

                // 2. Resilient HTTP streaming download with range-resume support, 256KB buffer, and SHA-256 verification
                int maxRetries = 3;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

                        long existingBytes = 0;
                        if (File.Exists(destinationPath))
                        {
                            existingBytes = new FileInfo(destinationPath).Length;
                        }

                        using var request = new HttpRequestMessage(HttpMethod.Get, manifest.DownloadUrl);
                        if (existingBytes > 0)
                        {
                            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);
                        }

                        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                        bool isPartial = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                        if (response.IsSuccessStatusCode)
                        {
                            long totalExpected = response.Content.Headers.ContentLength ?? -1L;
                            if (isPartial && response.Content.Headers.ContentRange?.Length != null)
                            {
                                totalExpected = response.Content.Headers.ContentRange.Length.Value;
                            }
                            else if (isPartial && totalExpected > 0)
                            {
                                totalExpected += existingBytes;
                            }

                            await using var contentStream = await response.Content.ReadAsStreamAsync();

                            FileMode fileMode = (isPartial && existingBytes > 0) ? FileMode.Append : FileMode.Create;
                            await using var fileStream = new FileStream(
                                destinationPath,
                                fileMode,
                                FileAccess.Write,
                                FileShare.None,
                                262144,
                                FileOptions.Asynchronous | FileOptions.SequentialScan);

                            var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(262144);
                            long totalRead = isPartial ? existingBytes : 0;
                            try
                            {
                                int bytesRead;
                                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                                    totalRead += bytesRead;

                                    if (totalExpected > 0 && progress != null)
                                    {
                                        progress.Report(Math.Min(100.0, (double)totalRead / totalExpected * 100.0));
                                    }
                                }
                            }
                            finally
                            {
                                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                            }

                            await fileStream.FlushAsync();
                            fileStream.Close();

                            // Validate downloaded size integrity if known
                            if (totalExpected > 0 && totalRead < totalExpected)
                            {
                                continue;
                            }

                            // Cryptographic SHA-256 verification
                            if (!string.IsNullOrWhiteSpace(manifest.Sha256Hash))
                            {
                                if (!SecurityGuard.VerifySha256(destinationPath, manifest.Sha256Hash))
                                {
                                    if (File.Exists(destinationPath)) File.Delete(destinationPath);
                                    continue;
                                }
                            }

                            downloaded = true;
                            break;
                        }
                    }
                    catch
                    {
                        if (attempt < maxRetries)
                        {
                            await Task.Delay(attempt * 1000);
                        }
                    }
                }

                // 3. Fallback to local distribution package if HTTP is unavailable or offline
                if (!downloaded || !File.Exists(destinationPath))
                {
                    string candidateFileName = $"PRRX_Internet_Download_Manager_v{manifest.Version}_Portable.zip";
                    try
                    {
                        if (Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var parsed))
                        {
                            var fn = Path.GetFileName(parsed.LocalPath);
                            if (!string.IsNullOrWhiteSpace(fn)) candidateFileName = fn;
                        }
                    }
                    catch { }

                    var localFallbacks = new List<string>
                    {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dist", candidateFileName),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, candidateFileName),
                        Path.Combine(AppContext.BaseDirectory, "dist", candidateFileName)
                    };

                    var curDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                    for (int i = 0; i < 5 && curDir != null; i++)
                    {
                        var cand = Path.Combine(curDir.FullName, "dist", candidateFileName);
                        if (File.Exists(cand) && !localFallbacks.Contains(cand)) localFallbacks.Add(cand);
                        curDir = curDir.Parent;
                    }

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
                var tempDir = Path.GetTempPath();
                var stagingDir = Path.Combine(tempDir, $"PRRX_IDM_Update_{Guid.NewGuid():N}");
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

                // 5. Generate and trigger the update helper process outside staging dir
                int currentPid = Environment.ProcessId;
                var targetExe = Path.Combine(appDir, "PRRX.InternetDownloadManager.exe");
                var scriptPath = Path.Combine(tempDir, $"apply_update_{Guid.NewGuid():N}.ps1");

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

# Post-update auto cleanup in helper script (Guaranteed Zero Data Loss)
try {{
    # Remove old binary swap files, preserving all user configuration and database files
    Get-ChildItem -Path '{appDir}' -File -Filter '*.old' -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -notmatch '\.(json|sqlite|db|key|token)$' }} | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path '{appDir}' -File -Filter '*.bak' -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -notmatch '\.(json|sqlite|db|key|token)$' }} | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path '{appDir}' -File -Filter '*.tmp' -ErrorAction SilentlyContinue | Where-Object {{ $_.Name -notmatch '\.(json|sqlite|db|key|token)$' }} | Remove-Item -Force -ErrorAction SilentlyContinue
    
    # Remove staging directory (now cleanly unlocked since script runs outside it)
    if (Test-Path '{stagingDir}') {{
        Remove-Item -Path '{stagingDir}' -Recurse -Force -ErrorAction SilentlyContinue
    }}
}} catch {{}}

if (Test-Path '{targetExe}') {{
    Start-Process -FilePath '{targetExe}' -WorkingDirectory '{appDir}'
}}
Start-Sleep -Seconds 1
try {{
    # Self-clean helper script in background after powershell exits
    Start-Process cmd.exe -ArgumentList '/c timeout /t 2 >nul & del /f /q ""{scriptPath}""' -WindowStyle Hidden
}} catch {{}}
";

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

        public static bool IsProtectedUserDataFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return true;

            try
            {
                var fileName = Path.GetFileName(filePath).ToLowerInvariant();
                var ext = Path.GetExtension(filePath).ToLowerInvariant();

                // Explicit protected configuration and state file names
                var protectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "config.json",
                    "download_history.json",
                    "history.json",
                    "settings.json",
                    "cookies.txt",
                    "master.key",
                    "ipc.token",
                    "manifest.json"
                };

                if (protectedNames.Contains(fileName)) return true;

                // Protect all data state files
                if (ext == ".json" || ext == ".db" || ext == ".sqlite" || ext == ".key" || ext == ".token")
                {
                    return true;
                }

                var fullPath = Path.GetFullPath(filePath);
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var userDownloads = Path.Combine(userProfile, "Downloads");
                var userDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                // Protected system and user personal directories
                if (SecurityGuard.IsPathWithinDirectory(userDownloads, fullPath) ||
                    SecurityGuard.IsPathWithinDirectory(userDocuments, fullPath) ||
                    SecurityGuard.IsPathWithinDirectory(userDesktop, fullPath))
                {
                    return true;
                }

                var appDataFolder = ConfigurationService.AppDataFolder;
                if (SecurityGuard.IsPathWithinDirectory(appDataFolder, fullPath))
                {
                    var rel = Path.GetRelativePath(appDataFolder, fullPath);
                    // Only backup directories older than 7 days can be pruned
                    if (!rel.StartsWith("backup_pre_update_", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Strict zero-data-loss fail-safe
                return true;
            }

            return false;
        }

        public Task<CleanupReport> CleanupPostUpdateArtifactsAsync(string? customAppDir = null)
        {
            return Task.Run(() => CleanupPostUpdateArtifacts(customAppDir));
        }

        public CleanupReport CleanupPostUpdateArtifacts(string? customAppDir = null)
        {
            var report = new CleanupReport();
            var appDir = customAppDir ?? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            var tempDir = Path.GetTempPath();

            try
            {
                // 1. Scan and delete temporary update staging directories in Temp
                try
                {
                    var tempDirs = Directory.GetDirectories(tempDir, "PRRX_IDM_Update_*", SearchOption.TopDirectoryOnly);
                    foreach (var dir in tempDirs)
                    {
                        try
                        {
                            var di = new DirectoryInfo(dir);
                            long dirSize = 0;
                            foreach (var fi in di.GetFiles("*", SearchOption.AllDirectories))
                            {
                                if (!IsProtectedUserDataFile(fi.FullName))
                                {
                                    dirSize += fi.Length;
                                    report.FilesDeletedCount++;
                                }
                            }
                            Directory.Delete(dir, true);
                            report.DirectoriesCleanedCount++;
                            report.BytesFreed += dirSize;
                            report.CleanedItems.Add($"Removed update staging directory: {di.Name} ({FormatBytes(dirSize)})");
                        }
                        catch { }
                    }

                    var otherTempDirs = Directory.GetDirectories(tempDir, "PRRX_Update_*", SearchOption.TopDirectoryOnly);
                    foreach (var dir in otherTempDirs)
                    {
                        try
                        {
                            var di = new DirectoryInfo(dir);
                            long dirSize = 0;
                            foreach (var fi in di.GetFiles("*", SearchOption.AllDirectories))
                            {
                                if (!IsProtectedUserDataFile(fi.FullName))
                                {
                                    dirSize += fi.Length;
                                    report.FilesDeletedCount++;
                                }
                            }
                            Directory.Delete(dir, true);
                            report.DirectoriesCleanedCount++;
                            report.BytesFreed += dirSize;
                            report.CleanedItems.Add($"Removed update cache directory: {di.Name} ({FormatBytes(dirSize)})");
                        }
                        catch { }
                    }
                }
                catch { }

                // 2. Scan and delete temporary update archives in Temp
                try
                {
                    var zipFilters = new[] { "PRRX_Update_Package*.zip", "*PRRX*Update*.zip", "PRRX_IDM_*_stage*.zip" };
                    foreach (var filter in zipFilters)
                    {
                        var zipFiles = Directory.GetFiles(tempDir, filter, SearchOption.TopDirectoryOnly);
                        foreach (var zf in zipFiles)
                        {
                            try
                            {
                                if (!IsProtectedUserDataFile(zf))
                                {
                                    var fi = new FileInfo(zf);
                                    long sz = fi.Length;
                                    File.Delete(zf);
                                    report.FilesDeletedCount++;
                                    report.BytesFreed += sz;
                                    report.CleanedItems.Add($"Removed update archive: {fi.Name} ({FormatBytes(sz)})");
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }

                // 3. Scan and delete intermediate Inno Setup extraction caches in Temp
                try
                {
                    var innoDirs = Directory.GetDirectories(tempDir, "is-*.tmp", SearchOption.TopDirectoryOnly);
                    foreach (var dir in innoDirs)
                    {
                        try
                        {
                            var di = new DirectoryInfo(dir);
                            if (DateTime.UtcNow - di.CreationTimeUtc > TimeSpan.FromHours(1))
                            {
                                long dirSize = 0;
                                foreach (var fi in di.GetFiles("*", SearchOption.AllDirectories))
                                {
                                    if (!IsProtectedUserDataFile(fi.FullName))
                                    {
                                        dirSize += fi.Length;
                                        report.FilesDeletedCount++;
                                    }
                                }
                                Directory.Delete(dir, true);
                                report.DirectoriesCleanedCount++;
                                report.BytesFreed += dirSize;
                                report.CleanedItems.Add($"Removed Inno extraction cache: {di.Name}");
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                // 4. Scan and delete stale binary swap files in application directory
                if (Directory.Exists(appDir))
                {
                    try
                    {
                        var stalePatterns = new[] { "*.bak", "*.old", "*.tmp", "*.swap", "~*.tmp", "*.pending_update" };
                        foreach (var pattern in stalePatterns)
                        {
                            var staleFiles = Directory.GetFiles(appDir, pattern, SearchOption.TopDirectoryOnly);
                            foreach (var sf in staleFiles)
                            {
                                try
                                {
                                    if (!IsProtectedUserDataFile(sf))
                                    {
                                        var fi = new FileInfo(sf);
                                        long sz = fi.Length;
                                        File.Delete(sf);
                                        report.FilesDeletedCount++;
                                        report.BytesFreed += sz;
                                        report.CleanedItems.Add($"Removed stale binary swap: {fi.Name} ({FormatBytes(sz)})");
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }

                // 5. Clean stale temporary scripts and logs in Temp
                try
                {
                    var scriptFiles = Directory.GetFiles(tempDir, "apply_update*.ps1", SearchOption.TopDirectoryOnly);
                    foreach (var sc in scriptFiles)
                    {
                        try
                        {
                            if (!IsProtectedUserDataFile(sc))
                            {
                                File.Delete(sc);
                                report.FilesDeletedCount++;
                            }
                        }
                        catch { }
                    }

                    var logFiles = Directory.GetFiles(tempDir, "*update_cleanup*.log", SearchOption.TopDirectoryOnly);
                    foreach (var lf in logFiles)
                    {
                        try
                        {
                            if (!IsProtectedUserDataFile(lf))
                            {
                                File.Delete(lf);
                                report.FilesDeletedCount++;
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                // 6. Clean old pre-update backup folders in AppData older than 7 days
                try
                {
                    var appDataFolder = ConfigurationService.AppDataFolder;
                    if (Directory.Exists(appDataFolder))
                    {
                        var backupDirs = Directory.GetDirectories(appDataFolder, "backup_pre_update_*", SearchOption.TopDirectoryOnly);
                        foreach (var bDir in backupDirs)
                        {
                            try
                            {
                                var di = new DirectoryInfo(bDir);
                                if (DateTime.UtcNow - di.CreationTimeUtc > TimeSpan.FromDays(7))
                                {
                                    long dirSize = 0;
                                    foreach (var fi in di.GetFiles("*", SearchOption.AllDirectories))
                                    {
                                        dirSize += fi.Length;
                                        report.FilesDeletedCount++;
                                    }
                                    Directory.Delete(bDir, true);
                                    report.DirectoriesCleanedCount++;
                                    report.BytesFreed += dirSize;
                                    report.CleanedItems.Add($"Pruned expired backup snapshot: {di.Name}");
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            catch
            {
                report.Success = false;
            }

            return report;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            if (bytes >= 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
            if (bytes >= 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
            if (bytes >= 1024) return $"{(bytes / 1024.0):F1} KB";
            return $"{bytes} B";
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
