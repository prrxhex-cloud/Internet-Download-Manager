using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using PRRX.IDM.Models;
using PRRX.IDM.Security;

namespace PRRX.IDM.Services
{
    public interface IUpdateService
    {
        Version CurrentVersion { get; }
        Task<(bool UpdateAvailable, UpdateManifest? Manifest, string? ErrorMessage)> CheckGitHubReleasesAsync(string? repoOwnerAndName = null);
        Task<bool> DownloadAndVerifyUpdateAsync(UpdateManifest manifest, string destinationPath, IProgress<double>? progress = null);
    }

    public class UpdateService : IUpdateService
    {
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private const string DefaultGitHubRepo = "prrxhex-cloud/Internet-Download-Manager";

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PRRX-IDM", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            return client;
        }

        public Version CurrentVersion
        {
            get
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                return ver ?? new Version(1, 0, 0, 0);
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
                    return (false, null, "No GitHub releases published yet. You are on the primary build.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    return (false, null, $"GitHub API responded with: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var rawTag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "1.0.0" : "1.0.0";
                var cleanVersion = rawTag.TrimStart('v', 'V').Trim();

                var releaseName = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : rawTag;
                var releaseBody = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
                var publishedAt = root.TryGetProperty("published_at", out var pubProp) ? pubProp.GetString() ?? "" : "";
                var htmlUrl = root.TryGetProperty("html_url", out var htmlProp) ? htmlProp.GetString() ?? "" : "";

                string downloadUrl = htmlUrl;

                // Look for .exe asset in GitHub Release assets
                if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsProp.EnumerateArray())
                    {
                        var assetName = asset.TryGetProperty("name", out var aName) ? aName.GetString() ?? "" : "";
                        var browserDownload = asset.TryGetProperty("browser_download_url", out var bUrl) ? bUrl.GetString() ?? "" : "";

                        if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || 
                            assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = browserDownload;
                            break;
                        }
                    }
                }

                var manifest = new UpdateManifest
                {
                    Version = cleanVersion,
                    ReleaseDate = publishedAt.Length >= 10 ? publishedAt.Substring(0, 10) : DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    ReleaseNotes = !string.IsNullOrWhiteSpace(releaseBody) ? releaseBody : $"Release {releaseName}",
                    DownloadUrl = downloadUrl
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
                return (false, null, $"Could not check GitHub releases: {ex.Message}");
            }
        }

        public async Task<bool> DownloadAndVerifyUpdateAsync(
            UpdateManifest manifest,
            string destinationPath,
            IProgress<double>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(manifest.DownloadUrl)) return false;

            try
            {
                using var response = await HttpClient.GetAsync(manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    if (totalBytes > 0 && progress != null)
                    {
                        progress.Report((double)totalRead / totalBytes * 100.0);
                    }
                }

                fileStream.Close();

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
    }
}
