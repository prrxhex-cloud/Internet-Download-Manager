using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PRRX.IDM.Models;
using PRRX.IDM.ViewModels;

namespace PRRX.IDM.ViewModels
{
    public enum DownloadDialogResult
    {
        Cancel,
        StartNow,
        DownloadLater
    }

    public class DownloadFileInfoViewModel : ViewModelBase
    {
        private static readonly HttpClient ProbeClient = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.None
        }) { Timeout = TimeSpan.FromSeconds(10) };

        private string _url = string.Empty;
        private string _fileName = string.Empty;
        private FileCategory _selectedCategory = FileCategory.General;
        private string _saveDirectory = string.Empty;
        private string _description = string.Empty;
        private string _fileSizeFormatted = "Estimating size...";
        private CancellationTokenSource? _probeCts;

        public DownloadDialogResult DialogResult { get; private set; } = DownloadDialogResult.Cancel;

        public string Url
        {
            get => _url;
            set
            {
                if (SetProperty(ref _url, value))
                {
                    _ = ProbeFileSizeAsync(value);
                }
            }
        }

        public string FileName
        {
            get => _fileName;
            set
            {
                var sanitized = SanitizeFileName(value);
                if (SetProperty(ref _fileName, sanitized))
                {
                    OnPropertyChanged(nameof(SaveAsFullPath));
                    if (!_isDescriptionUserEdited)
                    {
                        AutoPopulateDescription();
                    }
                }
            }
        }

        public FileCategory SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    if (!_isDescriptionUserEdited)
                    {
                        AutoPopulateDescription();
                    }
                }
            }
        }

        public List<FileCategory> AvailableCategories { get; } = new()
        {
            FileCategory.General,
            FileCategory.Programs,
            FileCategory.Compressed,
            FileCategory.Video,
            FileCategory.Music,
            FileCategory.Documents
        };

        public string SaveDirectory
        {
            get => _saveDirectory;
            set
            {
                if (SetProperty(ref _saveDirectory, value))
                {
                    OnPropertyChanged(nameof(SaveAsFullPath));
                }
            }
        }

        public string SaveAsFullPath => Path.Combine(string.IsNullOrWhiteSpace(SaveDirectory) ? "." : SaveDirectory, FileName);

        private bool _isDescriptionUserEdited = false;
        private string? _cachedPageTitle = null;
        private string? _detectedMime = null;

        public string Description
        {
            get => _description;
            set
            {
                if (SetProperty(ref _description, value))
                {
                    _isDescriptionUserEdited = true;
                }
            }
        }

        public string FileSizeFormatted
        {
            get => _fileSizeFormatted;
            set => SetProperty(ref _fileSizeFormatted, value);
        }

        public ICommand StartDownloadCommand { get; }
        public ICommand DownloadLaterCommand { get; }
        public ICommand BrowseDirectoryCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action? RequestClose;

        public DownloadFileInfoViewModel(string initialUrl, string defaultDownloadDir, string? pageTitle = null)
        {
            _url = initialUrl;
            _fileName = ExtractFileNameFromUrl(initialUrl);
            _selectedCategory = FileCategoryHelper.DetectCategory(_fileName);
            _cachedPageTitle = pageTitle;

            var baseDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            _saveDirectory = !string.IsNullOrWhiteSpace(defaultDownloadDir) && Directory.Exists(defaultDownloadDir)
                ? defaultDownloadDir
                : baseDownloads;

            AutoPopulateDescription(pageTitle);

            StartDownloadCommand = new RelayCommand(_ =>
            {
                DialogResult = DownloadDialogResult.StartNow;
                RequestClose?.Invoke();
            });

            DownloadLaterCommand = new RelayCommand(_ =>
            {
                DialogResult = DownloadDialogResult.DownloadLater;
                RequestClose?.Invoke();
            });

            BrowseDirectoryCommand = new RelayCommand(_ =>
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "Select Destination Folder for PRRX IDM",
                    InitialDirectory = Directory.Exists(SaveDirectory) ? SaveDirectory : baseDownloads
                };

                if (dialog.ShowDialog() == true)
                {
                    SaveDirectory = dialog.FolderName;
                }
            });

            CancelCommand = new RelayCommand(_ =>
            {
                DialogResult = DownloadDialogResult.Cancel;
                RequestClose?.Invoke();
            });

            _ = ProbeFileSizeAsync(initialUrl);
        }

        private void AutoPopulateDescription(string? pageTitle = null, string? mime = null)
        {
            if (_isDescriptionUserEdited && !string.IsNullOrWhiteSpace(_description)) return;

            var parts = new List<string>();
            var title = !string.IsNullOrWhiteSpace(pageTitle) ? pageTitle : _cachedPageTitle;
            if (!string.IsNullOrWhiteSpace(title))
            {
                parts.Add($"Page: {title.Trim()}");
            }

            try
            {
                if (Uri.TryCreate(_url, UriKind.Absolute, out var uri))
                {
                    parts.Add($"Host: {uri.Host}");
                }
                else if (!string.IsNullOrWhiteSpace(_url))
                {
                    parts.Add($"Source: {_url}");
                }
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(FileName))
            {
                parts.Add($"File: {FileName}");
            }

            var ext = Path.GetExtension(FileName);
            var mimeStr = !string.IsNullOrWhiteSpace(mime) ? mime : _detectedMime;
            var mimeInfo = !string.IsNullOrWhiteSpace(mimeStr) ? $" [{mimeStr}]" : "";
            parts.Add($"Format: {SelectedCategory}{(string.IsNullOrEmpty(ext) ? "" : $" ({ext})")}{mimeInfo}");
            parts.Add($"Captured: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            _description = string.Join(Environment.NewLine, parts);
            OnPropertyChanged(nameof(Description));
        }

        public async Task ProbeFileSizeAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                FileSizeFormatted = "Unknown size";
                return;
            }

            _probeCts?.Cancel();
            var cts = new CancellationTokenSource();
            _probeCts = cts;

            FileSizeFormatted = "Estimating size...";

            long? detectedBytes = null;
            string? detectedName = null;
            string? detectedMime = null;

            // 1. Try HEAD request with 1.5-second fast timeout
            try
            {
                using var headCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                headCts.CancelAfter(TimeSpan.FromSeconds(1.5));

                using var headReq = new HttpRequestMessage(HttpMethod.Head, url);
                headReq.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                headReq.Headers.Add("Accept", "*/*");

                using var headResp = await ProbeClient.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, headCts.Token);
                if (headResp.IsSuccessStatusCode)
                {
                    if (headResp.Content.Headers.ContentLength.HasValue && headResp.Content.Headers.ContentLength.Value > 0)
                    {
                        detectedBytes = headResp.Content.Headers.ContentLength.Value;
                    }

                    if (headResp.Content.Headers.ContentType?.MediaType != null)
                    {
                        detectedMime = headResp.Content.Headers.ContentType.MediaType;
                    }

                    if (headResp.Content.Headers.ContentDisposition?.FileName != null)
                    {
                        detectedName = headResp.Content.Headers.ContentDisposition.FileName.Trim('"', '\'');
                    }
                    else if (headResp.Content.Headers.ContentDisposition?.FileNameStar != null)
                    {
                        detectedName = headResp.Content.Headers.ContentDisposition.FileNameStar;
                    }
                }
            }
            catch
            {
                // Fallback to GET immediately
            }

            // 2. If HEAD didn't yield size or failed/blocked, probe with GET Range: bytes=0-0
            if (detectedBytes == null && !cts.IsCancellationRequested)
            {
                try
                {
                    using var getCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                    getCts.CancelAfter(TimeSpan.FromSeconds(2.0));

                    using var getReq = new HttpRequestMessage(HttpMethod.Get, url);
                    getReq.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                    getReq.Headers.Add("Accept", "*/*");
                    getReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);

                    using var getResp = await ProbeClient.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead, getCts.Token);
                    if (getResp.Content.Headers.ContentType?.MediaType != null)
                    {
                        detectedMime ??= getResp.Content.Headers.ContentType.MediaType;
                    }

                    if (getResp.Content.Headers.ContentDisposition?.FileName != null)
                    {
                        detectedName ??= getResp.Content.Headers.ContentDisposition.FileName.Trim('"', '\'');
                    }
                    else if (getResp.Content.Headers.ContentDisposition?.FileNameStar != null)
                    {
                        detectedName ??= getResp.Content.Headers.ContentDisposition.FileNameStar;
                    }

                    if (getResp.Content.Headers.ContentRange?.Length.HasValue == true)
                    {
                        detectedBytes = getResp.Content.Headers.ContentRange.Length.Value;
                    }
                    else if (getResp.Content.Headers.TryGetValues("Content-Range", out var crVals))
                    {
                        foreach (var val in crVals)
                        {
                            var slashIdx = val.LastIndexOf('/');
                            if (slashIdx >= 0 && long.TryParse(val.AsSpan(slashIdx + 1).Trim(), out var total))
                            {
                                detectedBytes = total;
                                break;
                            }
                        }
                    }
                    else if (getResp.StatusCode == System.Net.HttpStatusCode.OK && getResp.Content.Headers.ContentLength.HasValue)
                    {
                        detectedBytes = getResp.Content.Headers.ContentLength.Value;
                    }
                }
                catch
                {
                    // Network or DNS error
                }
            }

            if (cts.IsCancellationRequested && _probeCts != cts) return;

            // Apply results to UI safely using DispatchToUi
            DispatchToUi(() =>
            {
                if (!string.IsNullOrWhiteSpace(detectedName) &&
                    (FileName.StartsWith("download_") || FileName == "download.bin"))
                {
                    FileName = detectedName;
                    SelectedCategory = FileCategoryHelper.DetectCategory(FileName);
                }

                if (!string.IsNullOrWhiteSpace(detectedMime))
                {
                    _detectedMime = detectedMime;
                }

                AutoPopulateDescription(_cachedPageTitle, detectedMime);

                if (detectedBytes.HasValue && detectedBytes.Value > 0)
                {
                    FileSizeFormatted = FormatBytes(detectedBytes.Value);
                }
                else
                {
                    FileSizeFormatted = "Unknown size";
                }
            });
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "Unknown size";
            if (bytes >= 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
            if (bytes >= 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
            if (bytes >= 1024) return $"{(bytes / 1024.0):F1} KB";
            return $"{bytes} B";
        }

        public static string ExtractFileNameFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var lastSeg = uri.Segments[^1].TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(lastSeg) && lastSeg.Contains('.'))
                {
                    return SanitizeFileName(Uri.UnescapeDataString(lastSeg));
                }

                // Check query string parameters for filename
                if (!string.IsNullOrWhiteSpace(uri.Query))
                {
                    var query = uri.Query.TrimStart('?');
                    var pairs = query.Split('&');
                    foreach (var pair in pairs)
                    {
                        var kv = pair.Split('=');
                        if (kv.Length == 2)
                        {
                            var k = kv[0].ToLowerInvariant();
                            if (k is "filename" or "file" or "name" or "fn" or "attachment")
                            {
                                var val = Uri.UnescapeDataString(kv[1]);
                                if (!string.IsNullOrWhiteSpace(val) && val.Contains('.'))
                                {
                                    return SanitizeFileName(Path.GetFileName(val));
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return "download_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bin";
        }

        public static string SanitizeFileName(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return "download.bin";
            var clean = fileName.Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                clean = clean.Replace(c, '_');
            }
            return string.IsNullOrWhiteSpace(clean) ? "download.bin" : clean;
        }
    }
}
