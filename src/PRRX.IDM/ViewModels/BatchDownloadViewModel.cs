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
using System.Windows.Input;
using Microsoft.Win32;
using PRRX.IDM.Models;

namespace PRRX.IDM.ViewModels
{
    public class BatchDownloadViewModel : ViewModelBase
    {
        private string _saveDirectory = string.Empty;
        private string _sourcePageTitle = string.Empty;
        private string _filterQuery = string.Empty;

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

        public ICommand CheckAllCommand { get; }
        public ICommand UncheckAllCommand { get; }
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
                Links.Add(link);
            }

            CheckAllCommand = new RelayCommand(_ =>
            {
                foreach (var l in Links) l.IsSelected = true;
            });

            UncheckAllCommand = new RelayCommand(_ =>
            {
                foreach (var l in Links) l.IsSelected = false;
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
        }

        private void ApplyFilter()
        {
            if (string.IsNullOrWhiteSpace(FilterQuery)) return;
            var q = FilterQuery.ToLowerInvariant().Trim();

            foreach (var l in Links)
            {
                l.IsSelected = l.FileName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               l.Extension.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               l.Category.ToString().Contains(q, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
