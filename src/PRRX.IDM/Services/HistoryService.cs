// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PRRX.IDM.Models;
using PRRX.IDM.Security;

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
        public static string DefaultHistoryFilePath => Path.Combine(ConfigurationService.AppDataFolder, "download_history.json");

        private readonly string _historyFilePath;
        private readonly ISecurityService _securityService;
        public ObservableCollection<DownloadItem> HistoryItems { get; } = new();

        public HistoryService(ISecurityService? securityService = null, string? customHistoryPath = null)
        {
            _securityService = securityService ?? new SecurityService();

            _historyFilePath = !string.IsNullOrWhiteSpace(customHistoryPath)
                ? customHistoryPath
                : DefaultHistoryFilePath;

            var dir = Path.GetDirectoryName(_historyFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Auto-migrate legacy history location only for default live file
            if (string.IsNullOrWhiteSpace(customHistoryPath))
            {
                var legacyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PRRX_IDM",
                    "download_history.json");

                if (!File.Exists(_historyFilePath) && File.Exists(legacyPath))
                {
                    try
                    {
                        File.Copy(legacyPath, _historyFilePath, true);
                    }
                    catch
                    {
                        // Ignore migration errors
                    }
                }
            }

            LoadHistory();
        }

        public static bool IsTestArtifact(DownloadItem? item)
        {
            if (item == null) return false;
            return string.Equals(item.Title, "SecurityAuditTest.mp4", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(item.Url, "https://example.com/SecurityAuditTest.mp4", StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrEmpty(item.TargetFilePath) && item.TargetFilePath.Contains("SecurityAuditTest.mp4", StringComparison.OrdinalIgnoreCase));
        }

        public static int PurgeTestArtifacts(string? historyFilePath = null, ISecurityService? securityService = null)
        {
            var targetPath = !string.IsNullOrWhiteSpace(historyFilePath)
                ? historyFilePath
                : DefaultHistoryFilePath;

            if (!File.Exists(targetPath)) return 0;

            var sec = securityService ?? new SecurityService();
            var service = new HistoryService(sec, targetPath);
            return service.HistoryItems.Count;
        }

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    var fileContent = File.ReadAllText(_historyFilePath);
                    string plainJson = fileContent;

                    if (fileContent.Contains("\"isEncrypted\"", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var vault = JsonSerializer.Deserialize<EncryptedVaultContainer>(fileContent);
                            if (vault != null && vault.IsEncrypted && !string.IsNullOrWhiteSpace(vault.CipherPayload))
                            {
                                var key = _securityService.GetOrCreateMasterKey();
                                plainJson = _securityService.DecryptAesGcmString(vault.CipherPayload, key);
                            }
                        }
                        catch
                        {
                            plainJson = fileContent;
                        }
                    }

                    var items = JsonSerializer.Deserialize<List<DownloadItem>>(plainJson);
                    if (items != null)
                    {
                        HistoryItems.Clear();
                        bool purgedTestArtifacts = false;
                        foreach (var item in items)
                        {
                            if (IsTestArtifact(item))
                            {
                                purgedTestArtifacts = true;
                                continue;
                            }
                            HistoryItems.Add(item);
                        }

                        if (purgedTestArtifacts)
                        {
                            SaveHistory();
                        }
                    }
                }
            }
            catch
            {
                // Fallback to empty history on error
            }
        }

        public void SaveHistory()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var plainJson = JsonSerializer.Serialize(new List<DownloadItem>(HistoryItems), options);

                // Encrypt download history with AES-256-GCM
                var key = _securityService.GetOrCreateMasterKey();
                var cipherPayload = _securityService.EncryptAesGcmString(plainJson, key);

                var vault = new EncryptedVaultContainer
                {
                    VaultVersion = "2.0",
                    IsEncrypted = true,
                    CipherPayload = cipherPayload
                };

                var vaultJson = JsonSerializer.Serialize(vault, options);
                File.WriteAllText(_historyFilePath, vaultJson);
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
            if (item == null) return;

            DownloadItem? toRemove = null;
            if (HistoryItems.Contains(item))
            {
                toRemove = item;
            }
            else
            {
                toRemove = System.Linq.Enumerable.FirstOrDefault(HistoryItems, i =>
                    (!string.IsNullOrEmpty(item.Id) && i.Id == item.Id) ||
                    (!string.IsNullOrEmpty(item.TargetFilePath) && i.TargetFilePath.Equals(item.TargetFilePath, StringComparison.OrdinalIgnoreCase) && i.Url == item.Url) ||
                    (i.Title == item.Title && i.Url == item.Url));
            }

            if (toRemove != null)
            {
                HistoryItems.Remove(toRemove);
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
                    var fullPath = Path.GetFullPath(item.TargetFilePath);
                    var psi = new ProcessStartInfo
                    {
                        FileName = fullPath,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
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
                    var fullPath = Path.GetFullPath(item.TargetFilePath);
                    var psi = new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        UseShellExecute = false
                    };
                    psi.ArgumentList.Add($"/select,{fullPath}");
                    Process.Start(psi);
                }
                else
                {
                    var defaultDir = Path.GetDirectoryName(item.TargetFilePath);
                    if (!string.IsNullOrWhiteSpace(defaultDir) && Directory.Exists(defaultDir))
                    {
                        var fullDir = Path.GetFullPath(defaultDir);
                        var psi = new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            UseShellExecute = false
                        };
                        psi.ArgumentList.Add(fullDir);
                        Process.Start(psi);
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
