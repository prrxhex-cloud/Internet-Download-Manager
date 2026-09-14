// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
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
using PRRX.IDM.Services;
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
            UseProxy = false, // Critical: bypass Windows WPAD auto-proxy delay
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(2)
        }) { Timeout = TimeSpan.FromSeconds(5) };

        private readonly ICloudIntelligenceService _cloudService;

        private string _url = string.Empty;
        private string _fileName = string.Empty;
        private FileCategory _selectedCategory = FileCategory.General;
        private string _saveDirectory = string.Empty;
        private string _description = string.Empty;
        private string _fileSizeFormatted = "Probing size...";
        private bool _isProbing = false;
        private CancellationTokenSource? _probeCts;

        // Cloud Intelligence & Security properties
        private string _securityBadgeText = "🛡️ Unrated File (New)";
        private string _securityBadgeFgColor = "#60A5FA";
        private string _securityBadgeBgColor = "#200078D4";
        private string _securityBadgeBorderColor = "#400078D4";
        private string _securityBadgeTooltip = "New file - No community reports yet. PRRX IDM Cloud Intelligence active.";
        private ReputationBadgeStatus _reputationStatus = ReputationBadgeStatus.Unrated;
        private string _domainSpeedFormatted = string.Empty;
        private bool _hasDomainSpeedInfo = false;
        private string _domainHealthSummary = string.Empty;
        private bool _hasDomainHealthSummary = false;
        private string _currentFileHash = string.Empty;
        private bool _hasVoted = false;
        private string _voteFeedbackText = string.Empty;
        private string _voteFeedbackFgColor = "#4ADE80";
        private long? _detectedBytes = null;

        public DownloadDialogResult DialogResult { get; private set; } = DownloadDialogResult.Cancel;

        public bool IsProbing
        {
            get => _isProbing;
            set => SetProperty(ref _isProbing, value);
        }

        public string Url
        {
            get => _url;
            set
            {
                if (SetProperty(ref _url, value))
                {
                    _ = ProbeFileSizeAsync(value);
                    _ = LoadCloudIntelligenceAsync(value);
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

                    if (!_hasVoted && !string.IsNullOrWhiteSpace(_url))
                    {
                        var newHash = _cloudService.ExtractOrComputeSha256(_url, sanitized);
                        if (!string.Equals(newHash, CurrentFileHash, StringComparison.OrdinalIgnoreCase))
                        {
                            CurrentFileHash = newHash;
                            _ = Task.Run(() => RefreshReputationAsync(newHash));
                        }
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

        public string SecurityBadgeText
        {
            get => _securityBadgeText;
            set => SetProperty(ref _securityBadgeText, value);
        }

        public string SecurityBadgeFgColor
        {
            get => _securityBadgeFgColor;
            set => SetProperty(ref _securityBadgeFgColor, value);
        }

        public string SecurityBadgeBgColor
        {
            get => _securityBadgeBgColor;
            set => SetProperty(ref _securityBadgeBgColor, value);
        }

        public string SecurityBadgeBorderColor
        {
            get => _securityBadgeBorderColor;
            set => SetProperty(ref _securityBadgeBorderColor, value);
        }

        public string SecurityBadgeTooltip
        {
            get => _securityBadgeTooltip;
            set => SetProperty(ref _securityBadgeTooltip, value);
        }

        public ReputationBadgeStatus ReputationStatus
        {
            get => _reputationStatus;
            set => SetProperty(ref _reputationStatus, value);
        }

        public string DomainSpeedFormatted
        {
            get => _domainSpeedFormatted;
            set => SetProperty(ref _domainSpeedFormatted, value);
        }

        public bool HasDomainSpeedInfo
        {
            get => _hasDomainSpeedInfo;
            set => SetProperty(ref _hasDomainSpeedInfo, value);
        }

        public string DomainHealthSummary
        {
            get => _domainHealthSummary;
            set => SetProperty(ref _domainHealthSummary, value);
        }

        public bool HasDomainHealthSummary
        {
            get => _hasDomainHealthSummary;
            set => SetProperty(ref _hasDomainHealthSummary, value);
        }

        public string CurrentFileHash
        {
            get => _currentFileHash;
            set => SetProperty(ref _currentFileHash, value);
        }

        public bool HasVoted
        {
            get => _hasVoted;
            set
            {
                if (SetProperty(ref _hasVoted, value))
                {
                    OnPropertyChanged(nameof(CanVote));
                }
            }
        }

        public bool CanVote => !_hasVoted;

        public string VoteFeedbackText
        {
            get => _voteFeedbackText;
            set
            {
                if (SetProperty(ref _voteFeedbackText, value))
                {
                    OnPropertyChanged(nameof(HasVoteFeedback));
                }
            }
        }

        public string VoteFeedbackFgColor
        {
            get => _voteFeedbackFgColor;
            set => SetProperty(ref _voteFeedbackFgColor, value);
        }

        public bool HasVoteFeedback => !string.IsNullOrWhiteSpace(_voteFeedbackText);

        public ICommand StartDownloadCommand { get; }
        public ICommand DownloadLaterCommand { get; }
        public ICommand BrowseDirectoryCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand VoteSafeCommand { get; }
        public ICommand VoteSuspiciousCommand { get; }

        public event Action? RequestClose;

        public DownloadFileInfoViewModel(
            string initialUrl, 
            string defaultDownloadDir, 
            string? pageTitle = null, 
            long? precalculatedSize = null,
            string? initialFileName = null,
            ICloudIntelligenceService? cloudService = null)
        {
            _cloudService = cloudService ?? new CloudIntelligenceService();
            _url = initialUrl;
            _fileName = !string.IsNullOrWhiteSpace(initialFileName)
                ? SanitizeFileName(initialFileName)
                : ExtractFileNameFromUrl(initialUrl);
            _selectedCategory = FileCategoryHelper.DetectCategory(_fileName);
            _cachedPageTitle = pageTitle;
            _currentFileHash = _cloudService.ExtractOrComputeSha256(initialUrl, _fileName);

            var baseDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            _saveDirectory = !string.IsNullOrWhiteSpace(defaultDownloadDir)
                ? defaultDownloadDir
                : baseDownloads;

            AutoPopulateDescription(pageTitle);

            StartDownloadCommand = new RelayCommand(_ =>
            {
                CancelProbe();
                DialogResult = DownloadDialogResult.StartNow;
                RequestClose?.Invoke();
            });

            DownloadLaterCommand = new RelayCommand(_ =>
            {
                CancelProbe();
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
                CancelProbe();
                DialogResult = DownloadDialogResult.Cancel;
                RequestClose?.Invoke();
            });

            VoteSafeCommand = new AsyncRelayCommand(() => VoteReputationAsync("safe"), () => CanVote);
            VoteSuspiciousCommand = new AsyncRelayCommand(() => VoteReputationAsync("malware"), () => CanVote);

            if (precalculatedSize.HasValue && precalculatedSize.Value > 0)
            {
                _detectedBytes = precalculatedSize.Value;
                _fileSizeFormatted = FormatBytes(precalculatedSize.Value);
                _isProbing = false;
            }
            else
            {
                _fileSizeFormatted = "Probing size...";
                _isProbing = true;
            }

            // Launch size & filename probing strictly in background (<100ms instant dialog launch)
            _ = Task.Run(() => ProbeFileSizeAsync(initialUrl));

            // Launch non-blocking background lookup for domain health & community reputation
            _ = Task.Run(() => LoadCloudIntelligenceAsync(initialUrl));
        }

        public async Task VoteReputationAsync(string vote)
        {
            if (HasVoted) return;
            HasVoted = true;

            var isMalware = string.Equals(vote, "malware", StringComparison.OrdinalIgnoreCase);
            if (isMalware)
            {
                SecurityBadgeText = "⚠️ Suspicious / Malware Reported";
                SecurityBadgeFgColor = "#F87171";
                SecurityBadgeBgColor = "#25E81123";
                SecurityBadgeBorderColor = "#50E81123";
                SecurityBadgeTooltip = "Reported suspicious by user. Flagged in community threat database.";
                VoteFeedbackText = "⚠ Malware report submitted!";
                VoteFeedbackFgColor = "#F87171";
                ReputationStatus = ReputationBadgeStatus.Suspicious;
            }
            else
            {
                SecurityBadgeText = "🛡️ Community Verified: Safe (100%)";
                SecurityBadgeFgColor = "#4ADE80";
                SecurityBadgeBgColor = "#20107C41";
                SecurityBadgeBorderColor = "#40107C41";
                SecurityBadgeTooltip = "Marked safe by user vote. Contributed to global community database.";
                VoteFeedbackText = "✓ Safe vote submitted!";
                VoteFeedbackFgColor = "#4ADE80";
                ReputationStatus = ReputationBadgeStatus.Safe;
            }

            try
            {
                var size = _detectedBytes ?? 0;
                await _cloudService.ReportReputationAsync(CurrentFileHash, FileName, size, vote);
            }
            catch { }
        }

        public async Task LoadCloudIntelligenceAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            CurrentFileHash = _cloudService.ExtractOrComputeSha256(url, FileName);

            // 1. Non-blocking domain speed & health lookup
            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var domainHealth = await _cloudService.GetDomainHealthAsync(uri.Host);
                    if (domainHealth != null)
                    {
                        DispatchToUi(() =>
                        {
                            if (domainHealth.AvgSpeedMbps > 0)
                            {
                                DomainSpeedFormatted = $"⚡ {domainHealth.AvgSpeedMbps:F1} Mbps";
                                HasDomainSpeedInfo = true;
                                DomainHealthSummary = $"Cloud Acceleration: ~{domainHealth.AvgSpeedMbps:F1} Mbps ({domainHealth.Status.ToUpperInvariant()})";
                                HasDomainHealthSummary = true;
                            }
                            else if (domainHealth.IsOnline)
                            {
                                DomainSpeedFormatted = "⚡ Online";
                                HasDomainSpeedInfo = true;
                                DomainHealthSummary = $"Server Health: {domainHealth.Status}";
                                HasDomainHealthSummary = true;
                            }
                            else if (!string.IsNullOrWhiteSpace(domainHealth.DisplayText))
                            {
                                DomainSpeedFormatted = domainHealth.DisplayText;
                                HasDomainSpeedInfo = true;
                                DomainHealthSummary = $"Server Health: {domainHealth.Status}";
                                HasDomainHealthSummary = true;
                            }
                        });
                    }
                }
            }
            catch { }

            // 2. Non-blocking mirrors check
            try
            {
                var mirrors = await _cloudService.GetMirrorsAsync(CurrentFileHash);
                if (mirrors?.Mirrors.Count > 0)
                {
                    DispatchToUi(() =>
                    {
                        if (HasDomainHealthSummary)
                        {
                            DomainHealthSummary += $" | {mirrors.Mirrors.Count} cloud mirror(s)";
                        }
                        else
                        {
                            DomainHealthSummary = $"Cloud Mirrors: {mirrors.Mirrors.Count} available";
                            HasDomainHealthSummary = true;
                        }
                    });
                }
            }
            catch { }

            // 3. Non-blocking global community reputation lookup
            await RefreshReputationAsync(CurrentFileHash);
        }

        public async Task RefreshReputationAsync(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash) || _hasVoted) return;

            try
            {
                var rep = await _cloudService.GetFileReputationAsync(hash);
                DispatchToUi(() =>
                {
                    if (!_hasVoted && rep != null)
                    {
                        ApplyReputationToBadge(rep);
                    }
                });
            }
            catch { }
        }

        private void ApplyReputationToBadge(FileReputationResult rep)
        {
            if (rep.IsSuspicious)
            {
                SecurityBadgeText = "⚠️ Suspicious / Malware Reported";
                SecurityBadgeFgColor = "#F87171";
                SecurityBadgeBgColor = "#25E81123";
                SecurityBadgeBorderColor = "#50E81123";
                SecurityBadgeTooltip = rep.BadgeTooltip;
                ReputationStatus = ReputationBadgeStatus.Suspicious;
            }
            else if (rep.IsSafe)
            {
                SecurityBadgeText = $"🛡️ Community Verified: Safe ({rep.SafetyScore}%)";
                SecurityBadgeFgColor = "#4ADE80";
                SecurityBadgeBgColor = "#20107C41";
                SecurityBadgeBorderColor = "#40107C41";
                SecurityBadgeTooltip = rep.BadgeTooltip;
                ReputationStatus = ReputationBadgeStatus.Safe;
            }
            else if (rep.BadgeStatus == ReputationBadgeStatus.Neutral)
            {
                SecurityBadgeText = $"🛡️ Community Verified: Neutral ({rep.SafetyScore}%)";
                SecurityBadgeFgColor = "#60A5FA";
                SecurityBadgeBgColor = "#200078D4";
                SecurityBadgeBorderColor = "#400078D4";
                SecurityBadgeTooltip = rep.BadgeTooltip;
                ReputationStatus = ReputationBadgeStatus.Neutral;
            }
            else
            {
                SecurityBadgeText = "🛡️ Unrated File (New)";
                SecurityBadgeFgColor = "#60A5FA";
                SecurityBadgeBgColor = "#200078D4";
                SecurityBadgeBorderColor = "#400078D4";
                SecurityBadgeTooltip = rep.BadgeTooltip;
                ReputationStatus = ReputationBadgeStatus.Unrated;
            }
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

        public void CancelProbe()
        {
            try
            {
                _probeCts?.Cancel();
            }
            catch { }
        }

        private Task? _activeProbeTask;
        private string? _activeProbeUrl;
        private readonly object _probeLock = new();

        public Task ProbeFileSizeAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                DispatchToUi(() =>
                {
                    FileSizeFormatted = "Unknown size";
                    IsProbing = false;
                });
                return Task.CompletedTask;
            }

            lock (_probeLock)
            {
                if (_activeProbeTask != null && !_activeProbeTask.IsCompleted && _activeProbeUrl == url)
                {
                    return _activeProbeTask;
                }

                _probeCts?.Cancel();
                var cts = new CancellationTokenSource();
                _probeCts = cts;
                _activeProbeUrl = url;

                _activeProbeTask = DoProbeFileSizeAsync(url, cts);
                return _activeProbeTask;
            }
        }

        private async Task DoProbeFileSizeAsync(string url, CancellationTokenSource cts)
        {
            await Task.Yield();

            DispatchToUi(() =>
            {
                if (FileSizeFormatted == "Unknown size" || string.IsNullOrWhiteSpace(FileSizeFormatted) || FileSizeFormatted == "Probing size...")
                {
                    FileSizeFormatted = "Probing size...";
                    IsProbing = true;
                }
            });

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

                using var headResp = await ProbeClient.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, headCts.Token).ConfigureAwait(false);
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

                    using var getResp = await ProbeClient.SendAsync(getReq, HttpCompletionOption.ResponseHeadersRead, getCts.Token).ConfigureAwait(false);
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
                IsProbing = false;

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
                    _detectedBytes = detectedBytes.Value;
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
