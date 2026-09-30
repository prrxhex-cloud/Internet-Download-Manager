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
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PRRX.IDM.Models;

namespace PRRX.IDM.Services
{
    public interface ISiteSpiderGrabberService
    {
        Task CrawlWebsiteAsync(
            GrabberProject project,
            Action<GrabberFoundFile> onFileDiscovered,
            IProgress<string> statusProgress,
            CancellationToken cancellationToken);

        Task DownloadFilesAsync(
            GrabberProject project,
            IEnumerable<GrabberFoundFile> filesToDownload,
            Action<GrabberFoundFile> onFileProgress,
            CancellationToken cancellationToken);
    }

    public class SiteSpiderGrabberService : ISiteSpiderGrabberService
    {
        private readonly IConfigurationService? _configService;

        private static readonly Regex LinkRegex = new(
            """<a\s+(?:[^>]*?\s+)?href=["']?([^"'\s>#]+)["']?""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex ImgRegex = new(
            """<img\s+(?:[^>]*?\s+)?src=["']?([^"'\s>#]+)["']?""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex VideoRegex = new(
            """<video\s+(?:[^>]*?\s+)?src=["']?([^"'\s>#]+)["']?""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AudioRegex = new(
            """<audio\s+(?:[^>]*?\s+)?src=["']?([^"'\s>#]+)["']?""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SourceRegex = new(
            """<source\s+(?:[^>]*?\s+)?src=["']?([^"'\s>#]+)["']?""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex StylesheetRegex = new(
            """<link\s+(?:[^>]*?\s+)?href=["']?([^"'\s>#]+)["']?""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            "jpg", "jpeg", "png", "gif", "webp", "bmp", "svg", "ico", "tiff", "avif", "heic"
        };

        private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "rtf", "odt", "csv", "epub", "pages", "numbers"
        };

        private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            "mp4", "mkv", "avi", "mov", "wmv", "webm", "flv", "mp3", "m4a", "aac", "flac", "wav", "ogg", "m4v", "ts", "m3u8"
        };

        private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            "zip", "rar", "7z", "tar", "gz", "bz2", "xz", "iso", "dmg", "exe", "msi", "bin", "apk"
        };

        public SiteSpiderGrabberService(IConfigurationService? configService = null)
        {
            _configService = configService;
        }

        public async Task CrawlWebsiteAsync(
            GrabberProject project,
            Action<GrabberFoundFile> onFileDiscovered,
            IProgress<string> statusProgress,
            CancellationToken cancellationToken)
        {
            if (project == null || string.IsNullOrWhiteSpace(project.StartUrl)) return;

            if (!Uri.TryCreate(project.StartUrl, UriKind.Absolute, out var startUri) ||
                (startUri.Scheme != Uri.UriSchemeHttp && startUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("Start URL must be a valid HTTP or HTTPS address.");
            }

            project.Status = GrabberStatus.Crawling;
            project.PagesCrawled = 0;
            project.FilesFound = 0;
            project.CurrentActivity = "Initializing crawler...";

            var visitedPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var discoveredUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<(Uri Uri, int Depth)>();

            queue.Enqueue((startUri, 0));

            // Set up custom extensions set if applicable
            HashSet<string>? customExtSet = null;
            if (project.FilterPreset == GrabberFilterPreset.Custom && !string.IsNullOrWhiteSpace(project.CustomExtensions))
            {
                customExtSet = new HashSet<string>(
                    project.CustomExtensions.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(e => e.Trim().TrimStart('.').ToLowerInvariant()),
                    StringComparer.OrdinalIgnoreCase);
            }

            Regex? customRegex = null;
            if (!string.IsNullOrWhiteSpace(project.RegexFilter))
            {
                try { customRegex = new Regex(project.RegexFilter, RegexOptions.IgnoreCase | RegexOptions.Compiled); } catch { }
            }

            using var client = SegmentedDownloadEngine.CreateConfiguredClient(_configService?.CurrentConfig, targetUri: startUri);

            while (queue.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                var (currentUri, depth) = queue.Dequeue();
                var currentUrlStr = currentUri.AbsoluteUri;

                if (!visitedPages.Add(currentUrlStr))
                {
                    continue;
                }

                project.PagesCrawled++;
                var depthLabel = project.ExplorationDepth == 0 ? "Unlimited" : $"{depth}/{project.ExplorationDepth}";
                project.CurrentActivity = $"Crawling [Lvl {depthLabel}]: {currentUri.PathAndQuery}";
                statusProgress?.Report(project.CurrentActivity);

                string? htmlContent = null;
                string? contentType = null;

                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, currentUri);
                    SegmentedDownloadEngine.ApplyStandardHeaders(req, currentUrlStr, referer: startUri.AbsoluteUri);

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(15));

                    using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    contentType = resp.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();

                    if (resp.IsSuccessStatusCode)
                    {
                        if (contentType != null && (contentType.Contains("html") || contentType.Contains("xml") || contentType.Contains("text")))
                        {
                            htmlContent = await resp.Content.ReadAsStringAsync(cts.Token);
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SiteSpiderGrabber] Error crawling {currentUrlStr}: {ex.Message}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(htmlContent))
                {
                    continue;
                }

                // Extract all candidate links
                var extractedLinks = new List<string>();
                ExtractMatches(LinkRegex, htmlContent, extractedLinks);
                ExtractMatches(ImgRegex, htmlContent, extractedLinks);
                ExtractMatches(VideoRegex, htmlContent, extractedLinks);
                ExtractMatches(AudioRegex, htmlContent, extractedLinks);
                ExtractMatches(SourceRegex, htmlContent, extractedLinks);
                if (project.FilterPreset == GrabberFilterPreset.CompleteWebsite)
                {
                    ExtractMatches(StylesheetRegex, htmlContent, extractedLinks);
                }

                foreach (var rawLink in extractedLinks)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    if (string.IsNullOrWhiteSpace(rawLink) || rawLink.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || rawLink.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || rawLink.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!Uri.TryCreate(currentUri, rawLink, out var resolvedUri) ||
                        (resolvedUri.Scheme != Uri.UriSchemeHttp && resolvedUri.Scheme != Uri.UriSchemeHttps))
                    {
                        continue;
                    }

                    // Check domain constraint
                    if (project.StayOnDomain)
                    {
                        if (!resolvedUri.Host.Equals(startUri.Host, StringComparison.OrdinalIgnoreCase) &&
                            !resolvedUri.Host.EndsWith("." + startUri.Host, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    // Check regex filter
                    if (customRegex != null && !customRegex.IsMatch(resolvedUri.AbsoluteUri))
                    {
                        continue;
                    }

                    var ext = Path.GetExtension(resolvedUri.AbsolutePath).TrimStart('.').ToLowerInvariant();

                    // Check if file matches project filter preset
                    if (IsFileMatchingPreset(ext, resolvedUri.AbsolutePath, project.FilterPreset, customExtSet))
                    {
                        if (discoveredUrls.Add(resolvedUri.AbsoluteUri))
                        {
                            var fileName = Path.GetFileName(resolvedUri.LocalPath);
                            if (string.IsNullOrWhiteSpace(fileName))
                            {
                                fileName = $"resource_{discoveredUrls.Count}.{(!string.IsNullOrWhiteSpace(ext) ? ext : "html")}";
                            }

                            string localPath;
                            var saveDir = !string.IsNullOrWhiteSpace(project.SaveDirectory) ? project.SaveDirectory : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "PRRX_Grabber");

                            if (project.SaveStructure)
                            {
                                var safeSubPath = resolvedUri.AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                                localPath = Path.Combine(saveDir, resolvedUri.Host, safeSubPath);
                            }
                            else
                            {
                                localPath = Path.Combine(saveDir, fileName);
                            }

                            var foundFile = new GrabberFoundFile
                            {
                                Url = resolvedUri.AbsoluteUri,
                                FileName = fileName,
                                LocalPath = localPath,
                                FormattedSize = "Pending",
                                Status = "Discovered"
                            };

                            project.FilesFound++;
                            onFileDiscovered?.Invoke(foundFile);
                        }
                    }

                    // Check if this URL is an HTML page to traverse deeper
                    bool isNextPageCandidate = string.IsNullOrWhiteSpace(ext) || ext is "html" or "htm" or "php" or "asp" or "aspx" or "jsp";
                    int maxAllowedDepth = project.ExplorationDepth; // 0 = unlimited domain
                    bool depthAllows = maxAllowedDepth == 0 || depth + 1 <= maxAllowedDepth;

                    if (isNextPageCandidate && depthAllows && !visitedPages.Contains(resolvedUri.AbsoluteUri))
                    {
                        queue.Enqueue((resolvedUri, depth + 1));
                    }
                }
            }

            project.Status = cancellationToken.IsCancellationRequested ? GrabberStatus.Stopped : GrabberStatus.Completed;
            project.CurrentActivity = cancellationToken.IsCancellationRequested ? "Crawl stopped" : $"Crawl finished. Found {project.FilesFound} files.";
            statusProgress?.Report(project.CurrentActivity);
        }

        public async Task DownloadFilesAsync(
            GrabberProject project,
            IEnumerable<GrabberFoundFile> filesToDownload,
            Action<GrabberFoundFile> onFileProgress,
            CancellationToken cancellationToken)
        {
            if (project == null || filesToDownload == null) return;

            project.Status = GrabberStatus.Downloading;
            project.CurrentActivity = "Downloading grabbed files...";

            using var client = SegmentedDownloadEngine.CreateConfiguredClient(_configService?.CurrentConfig);
            using var throttle = new SemaphoreSlim(4, 4); // 4 concurrent download connections

            var tasks = filesToDownload.Select(async file =>
            {
                await throttle.WaitAsync(cancellationToken);
                try
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    file.Status = "Downloading";
                    onFileProgress?.Invoke(file);

                    var targetDir = Path.GetDirectoryName(file.LocalPath);
                    if (!string.IsNullOrWhiteSpace(targetDir) && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    using var req = new HttpRequestMessage(HttpMethod.Get, file.Url);
                    SegmentedDownloadEngine.ApplyStandardHeaders(req, file.Url, referer: project.StartUrl);

                    using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (!resp.IsSuccessStatusCode)
                    {
                        file.Status = $"Error ({resp.StatusCode})";
                        onFileProgress?.Invoke(file);
                        return;
                    }

                    long totalLen = resp.Content.Headers.ContentLength ?? -1;
                    if (totalLen > 0)
                    {
                        file.SizeBytes = totalLen;
                        file.FormattedSize = SegmentedDownloadEngine.FormatBytes(totalLen);
                    }

                    using var contentStream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                    using var fileStream = new FileStream(file.LocalPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);

                    var buffer = new byte[65536];
                    long downloaded = 0;
                    int read;

                    while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        downloaded += read;

                        if (totalLen > 0)
                        {
                            file.ProgressPercentage = Math.Min(100.0, (downloaded / (double)totalLen) * 100.0);
                        }
                        onFileProgress?.Invoke(file);
                    }

                    file.Status = "Completed";
                    file.ProgressPercentage = 100.0;
                    file.SizeBytes = downloaded;
                    file.FormattedSize = SegmentedDownloadEngine.FormatBytes(downloaded);

                    lock (project)
                    {
                        project.FilesDownloaded++;
                        project.TotalBytesDownloaded += downloaded;
                    }

                    onFileProgress?.Invoke(file);
                }
                catch (OperationCanceledException)
                {
                    file.Status = "Cancelled";
                    onFileProgress?.Invoke(file);
                }
                catch (Exception ex)
                {
                    file.Status = "Failed";
                    Debug.WriteLine($"[SiteSpiderGrabber] Error downloading {file.Url}: {ex.Message}");
                    onFileProgress?.Invoke(file);
                }
                finally
                {
                    throttle.Release();
                }
            });

            await Task.WhenAll(tasks);

            project.Status = GrabberStatus.Completed;
            project.CurrentActivity = $"Download finished. {project.FilesDownloaded} files saved.";
        }

        private static void ExtractMatches(Regex regex, string html, List<string> output)
        {
            var matches = regex.Matches(html);
            foreach (Match match in matches)
            {
                if (match.Success && match.Groups.Count > 1)
                {
                    var val = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        output.Add(val);
                    }
                }
            }
        }

        private static bool IsFileMatchingPreset(string ext, string path, GrabberFilterPreset preset, HashSet<string>? customExtSet)
        {
            if (string.IsNullOrWhiteSpace(ext)) return false;

            return preset switch
            {
                GrabberFilterPreset.AllImages => ImageExtensions.Contains(ext),
                GrabberFilterPreset.AllDocuments => DocumentExtensions.Contains(ext),
                GrabberFilterPreset.AllMedia => MediaExtensions.Contains(ext),
                GrabberFilterPreset.CompleteWebsite => true,
                GrabberFilterPreset.Custom => customExtSet?.Contains(ext) ?? false,
                GrabberFilterPreset.AllFiles => ImageExtensions.Contains(ext) || DocumentExtensions.Contains(ext) || MediaExtensions.Contains(ext) || ArchiveExtensions.Contains(ext),
                _ => true
            };
        }
    }
}
