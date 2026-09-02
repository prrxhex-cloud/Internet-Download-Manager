using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PRRX.IDM.Models;

namespace PRRX.IDM.Services
{
    public interface IHistoryService
    {
        ObservableCollection<DownloadItem> HistoryItems { get; }
        void AddItem(DownloadItem item);
        void RemoveItem(DownloadItem item);
        void ClearHistory();
        void OpenFile(DownloadItem item);
        void OpenFolder(DownloadItem item);
        void SaveHistory();
    }

    public class HistoryService : IHistoryService
    {
        private readonly string _historyFilePath;
        public ObservableCollection<DownloadItem> HistoryItems { get; } = new();

        public HistoryService()
        {
            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PRRX_IDM");
            Directory.CreateDirectory(appDataDir);
            _historyFilePath = Path.Combine(appDataDir, "download_history.json");

            LoadHistory();
        }

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    var json = File.ReadAllText(_historyFilePath);
                    var items = JsonSerializer.Deserialize<List<DownloadItem>>(json);
                    if (items != null)
                    {
                        HistoryItems.Clear();
                        foreach (var item in items)
                        {
                            HistoryItems.Add(item);
                        }
                    }
                }
            }
            catch
            {
                // Fallback to empty history
            }
        }

        public void SaveHistory()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(new List<DownloadItem>(HistoryItems), options);
                File.WriteAllText(_historyFilePath, json);
            }
            catch
            {
                // Ignore background save errors
            }
        }

        public void AddItem(DownloadItem item)
        {
            HistoryItems.Insert(0, item);
            SaveHistory();
        }

        public void RemoveItem(DownloadItem item)
        {
            if (HistoryItems.Contains(item))
            {
                HistoryItems.Remove(item);
                SaveHistory();
            }
        }

        public void ClearHistory()
        {
            HistoryItems.Clear();
            SaveHistory();
        }

        public void OpenFile(DownloadItem item)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(item.TargetFilePath) && File.Exists(item.TargetFilePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = item.TargetFilePath,
                        UseShellExecute = true
                    });
                }
            }
            catch
            {
                // Ignore launch errors
            }
        }

        public void OpenFolder(DownloadItem item)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(item.TargetFilePath) && File.Exists(item.TargetFilePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{item.TargetFilePath}\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    var defaultDir = Path.GetDirectoryName(item.TargetFilePath);
                    if (!string.IsNullOrWhiteSpace(defaultDir) && Directory.Exists(defaultDir))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = defaultDir,
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch
            {
                // Ignore folder launch errors
            }
        }
    }
}
