// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Microsoft.Win32;
using PRRX.IDM.Models;

namespace PRRX.IDM.ViewModels
{
    public enum BatchFilterPreset
    {
        All = 0,
        Videos = 1,
        Audio = 2,
        Images = 3,
        Documents = 4,
        Archives = 5,
        Programs = 6,
        Regex = 7,
        Custom = 8
    }

    public class BatchDownloadViewModel : ViewModelBase
    {
        private string _saveDirectory = string.Empty;
        private string _sourcePageTitle = string.Empty;
        private string _filterQuery = string.Empty;
        private BatchFilterPreset _selectedPreset = BatchFilterPreset.All;
        private string _minSizeMbText = string.Empty;
        private string _maxSizeMbText = string.Empty;
        private string _selectionStatusText = string.Empty;

        private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".bmp", ".ico", ".tiff", ".avif"
        };

        private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".webm", ".avi", ".mov", ".flv", ".m4v", ".ts", ".m3u8", ".3gp", ".wmv"
        };

        private static readonly HashSet<string> AudioExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".m4a", ".wav", ".aac", ".flac", ".ogg", ".opus", ".wma", ".aiff"
        };

        private static readonly HashSet<string> DocExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv", ".epub", ".rtf", ".odt"
        };

        private static readonly HashSet<string> ArchiveExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".dmg"
        };

        private static readonly HashSet<string> ProgramExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".msi", ".apk", ".bin", ".jar", ".deb", ".rpm"
        };

        public ObservableCollection<BatchLinkItem> Links { get; } = new();
        public List<BatchLinkItem> SelectedLinks => Links.Where(l => l.IsSelected).ToList();

        public string SaveDirectory
        {
            get => _saveDirectory;
            set => SetProperty(ref _saveDirectory, value);
        }

        public string SourcePageTitle
        {
            get => _sourcePageTitle;
            set => SetProperty(ref _sourcePageTitle, value);
        }

        public BatchFilterPreset SelectedPreset
        {
            get => _selectedPreset;
            set
            {
                if (SetProperty(ref _selectedPreset, value))
                {
                    ApplyFilter();
                }
            }
        }

        public string FilterQuery
        {
            get => _filterQuery;
            set
            {
                if (SetProperty(ref _filterQuery, value))
                {
                    ApplyFilter();
                }
            }
        }

        public string MinSizeMbText
        {
            get => _minSizeMbText;
            set
            {
                if (SetProperty(ref _minSizeMbText, value))
                {
                    ApplyFilter();
                }
            }
        }

        public string MaxSizeMbText
        {
            get => _maxSizeMbText;
            set
            {
                if (SetProperty(ref _maxSizeMbText, value))
                {
                    ApplyFilter();
                }
            }
        }

        public string SelectionStatusText
        {
            get => _selectionStatusText;
            set => SetProperty(ref _selectionStatusText, value);
        }

        public ICommand CheckAllCommand { get; }
        public ICommand UncheckAllCommand { get; }
        public ICommand InvertSelectionCommand { get; }
        public ICommand BrowseDirectoryCommand { get; }
        public ICommand StartBatchDownloadCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action? RequestClose;
        public bool IsConfirmed { get; private set; } = false;

        public BatchDownloadViewModel(BatchDownloadRequest request, string defaultDir)
        {
            _sourcePageTitle = request.PageTitle;
            _saveDirectory = string.IsNullOrWhiteSpace(defaultDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                : defaultDir;

            foreach (var link in request.Links)
            {
                link.PropertyChanged += (_, _) => UpdateSelectionStatus();
                Links.Add(link);
            }

            CheckAllCommand = new RelayCommand(_ =>
            {
                foreach (var l in Links) l.IsSelected = true;
                UpdateSelectionStatus();
            });

            UncheckAllCommand = new RelayCommand(_ =>
            {
                foreach (var l in Links) l.IsSelected = false;
                UpdateSelectionStatus();
            });

            InvertSelectionCommand = new RelayCommand(_ =>
            {
                foreach (var l in Links) l.IsSelected = !l.IsSelected;
                UpdateSelectionStatus();
            });

            BrowseDirectoryCommand = new RelayCommand(_ =>
            {
                var dlg = new OpenFolderDialog { InitialDirectory = SaveDirectory, Title = "Select Download Folder" };
                if (dlg.ShowDialog() == true)
                {
                    SaveDirectory = dlg.FolderName;
                }
            });

            StartBatchDownloadCommand = new RelayCommand(_ =>
            {
                IsConfirmed = true;
                RequestClose?.Invoke();
            });

            CancelCommand = new RelayCommand(_ =>
            {
                IsConfirmed = false;
                RequestClose?.Invoke();
            });

            UpdateSelectionStatus();
        }

        private void UpdateSelectionStatus()
        {
            int selected = Links.Count(l => l.IsSelected);
            int total = Links.Count;
            SelectionStatusText = $"{selected} of {total} items selected for download";
        }

        private void ApplyFilter()
        {
            Regex? regex = null;
            if (SelectedPreset == BatchFilterPreset.Regex && !string.IsNullOrWhiteSpace(FilterQuery))
            {
                try { regex = new Regex(FilterQuery, RegexOptions.IgnoreCase); } catch { }
            }

            double.TryParse(MinSizeMbText, out double minMb);
            double.TryParse(MaxSizeMbText, out double maxMb);
            long minBytes = minMb > 0 ? (long)(minMb * 1024 * 1024) : 0;
            long maxBytes = maxMb > 0 ? (long)(maxMb * 1024 * 1024) : 0;

            var q = FilterQuery.Trim().ToLowerInvariant();
            var customTokens = (SelectedPreset == BatchFilterPreset.Custom && !string.IsNullOrWhiteSpace(FilterQuery))
                ? FilterQuery.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().TrimStart('*').ToLowerInvariant())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToArray()
                : Array.Empty<string>();

            foreach (var l in Links)
            {
                var ext = !string.IsNullOrWhiteSpace(l.Extension) ? l.Extension : Path.GetExtension(l.FileName);
                if (!string.IsNullOrWhiteSpace(ext) && !ext.StartsWith('.')) ext = "." + ext;

                bool matchesCustom = customTokens.Length == 0 || customTokens.Any(token =>
                {
                    var dotToken = token.StartsWith('.') ? token : "." + token;
                    return ext.Equals(dotToken, StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(token, StringComparison.OrdinalIgnoreCase) ||
                           l.FileName.Contains(token, StringComparison.OrdinalIgnoreCase) ||
                           l.Url.Contains(token, StringComparison.OrdinalIgnoreCase);
                });

                bool matchesPreset = SelectedPreset switch
                {
                    BatchFilterPreset.All => true,
                    BatchFilterPreset.Videos => l.Category == FileCategory.Video || VideoExts.Contains(ext),
                    BatchFilterPreset.Audio => l.Category == FileCategory.Music || AudioExts.Contains(ext),
                    BatchFilterPreset.Images => ImageExts.Contains(ext),
                    BatchFilterPreset.Documents => l.Category == FileCategory.Documents || DocExts.Contains(ext),
                    BatchFilterPreset.Archives => l.Category == FileCategory.Compressed || ArchiveExts.Contains(ext),
                    BatchFilterPreset.Programs => l.Category == FileCategory.Programs || ProgramExts.Contains(ext),
                    BatchFilterPreset.Regex => regex?.IsMatch(l.Url) == true || regex?.IsMatch(l.FileName) == true,
                    BatchFilterPreset.Custom => matchesCustom,
                    _ => true
                };

                bool matchesQuery = string.IsNullOrWhiteSpace(q) || SelectedPreset == BatchFilterPreset.Custom || SelectedPreset == BatchFilterPreset.Regex ||
                                    l.FileName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                    l.Extension.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                    l.LinkText.Contains(q, StringComparison.OrdinalIgnoreCase);

                bool matchesSize = true;
                if (l.FileSizeBytes > 0)
                {
                    if (minBytes > 0 && l.FileSizeBytes < minBytes) matchesSize = false;
                    if (maxBytes > 0 && l.FileSizeBytes > maxBytes) matchesSize = false;
                }

                l.IsSelected = matchesPreset && matchesQuery && matchesSize;
            }

            UpdateSelectionStatus();
        }
    }
}
