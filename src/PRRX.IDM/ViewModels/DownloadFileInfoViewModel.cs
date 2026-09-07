using System;
using System.Collections.Generic;
using System.IO;
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
        private string _url = string.Empty;
        private string _fileName = string.Empty;
        private FileCategory _selectedCategory = FileCategory.General;
        private string _saveDirectory = string.Empty;
        private string _description = string.Empty;
        private string _fileSizeFormatted = "Estimating size...";

        public DownloadDialogResult DialogResult { get; private set; } = DownloadDialogResult.Cancel;

        public string Url
        {
            get => _url;
            set => SetProperty(ref _url, value);
        }

        public string FileName
        {
            get => _fileName;
            set
            {
                if (SetProperty(ref _fileName, value))
                {
                    OnPropertyChanged(nameof(SaveAsFullPath));
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
                    AutoUpdateSaveDirectory();
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

        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
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

        public DownloadFileInfoViewModel(string initialUrl, string defaultDownloadDir)
        {
            _url = initialUrl;
            _fileName = ExtractFileNameFromUrl(initialUrl);
            _selectedCategory = FileCategoryHelper.DetectCategory(_fileName);
            _saveDirectory = string.IsNullOrWhiteSpace(defaultDownloadDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                : defaultDownloadDir;

            AutoUpdateSaveDirectory();

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
                    InitialDirectory = SaveDirectory
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
        }

        private void AutoUpdateSaveDirectory()
        {
            var baseDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var subFolder = SelectedCategory switch
            {
                FileCategory.Programs => "Programs",
                FileCategory.Compressed => "Compressed",
                FileCategory.Video => "Video",
                FileCategory.Music => "Music",
                FileCategory.Documents => "Documents",
                _ => ""
            };

            var target = string.IsNullOrWhiteSpace(subFolder) ? baseDownloads : Path.Combine(baseDownloads, subFolder);
            try
            {
                Directory.CreateDirectory(target);
                SaveDirectory = target;
            }
            catch
            {
                SaveDirectory = baseDownloads;
            }
        }

        private static string ExtractFileNameFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var lastSeg = uri.Segments[^1].TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(lastSeg) && lastSeg.Contains('.'))
                {
                    return Uri.UnescapeDataString(lastSeg);
                }
            }
            catch { }

            return "download_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bin";
        }
    }
}
