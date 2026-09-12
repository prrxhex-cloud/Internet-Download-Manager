// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PRRX.IDM.Models;
using PRRX.IDM.Security;

namespace PRRX.IDM.Services
{
    public interface IConfigurationService
    {
        AppConfig CurrentConfig { get; }
        void SaveConfig();
        void LoadConfig();
    }

    public class EncryptedVaultContainer
    {
        [JsonPropertyName("vaultVersion")]
        public string VaultVersion { get; set; } = "2.0";

        [JsonPropertyName("isEncrypted")]
        public bool IsEncrypted { get; set; } = true;

        [JsonPropertyName("cipherPayload")]
        public string CipherPayload { get; set; } = string.Empty;
    }

    public class ConfigurationService : IConfigurationService
    {
        public static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PRRX Cooperation",
            "InternetDownloadManager");

        private static readonly string ConfigFilePath = Path.Combine(AppDataFolder, "config.json");

        // Legacy storage path for backward-compatibility auto-migration
        private static readonly string LegacyAppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PRRX Cooperation",
            "Internet Download Manager");
        private static readonly string LegacyConfigFilePath = Path.Combine(LegacyAppDataFolder, "config.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private readonly ISecurityService _securityService;

        public AppConfig CurrentConfig { get; private set; } = new();

        public ConfigurationService(ISecurityService? securityService = null)
        {
            _securityService = securityService ?? new SecurityService();
            EnsureDirectoryExists();
            LoadConfig();
        }

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(AppDataFolder))
            {
                Directory.CreateDirectory(AppDataFolder);
            }
        }

        public void LoadConfig()
        {
            try
            {
                // Auto-migrate legacy configuration if new path does not exist
                if (!File.Exists(ConfigFilePath) && File.Exists(LegacyConfigFilePath))
                {
                    try
                    {
                        EnsureDirectoryExists();
                        File.Copy(LegacyConfigFilePath, ConfigFilePath, true);
                    }
                    catch
                    {
                        // Best effort migration
                    }
                }

                if (File.Exists(ConfigFilePath))
                {
                    var fileContent = File.ReadAllText(ConfigFilePath);
                    string plainJson = fileContent;

                    // Check if file is stored in encrypted vault container
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

                    var config = JsonSerializer.Deserialize<AppConfig>(plainJson);
                    if (config != null)
                    {
                        CurrentConfig = config;
                    }
                }
            }
            catch (Exception)
            {
                CurrentConfig = new AppConfig();
            }

            if (string.IsNullOrWhiteSpace(CurrentConfig.DownloadDirectory))
            {
                CurrentConfig.DownloadDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads");
            }

            if (!Directory.Exists(CurrentConfig.DownloadDirectory))
            {
                try
                {
                    Directory.CreateDirectory(CurrentConfig.DownloadDirectory);
                }
                catch
                {
                    CurrentConfig.DownloadDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }
            }
        }

        public void SaveConfig()
        {
            try
            {
                EnsureDirectoryExists();
                var plainJson = JsonSerializer.Serialize(CurrentConfig, JsonOptions);

                // Encrypt config payload with AES-256-GCM using DPAPI-backed Master Key
                var key = _securityService.GetOrCreateMasterKey();
                var cipherPayload = _securityService.EncryptAesGcmString(plainJson, key);

                var vault = new EncryptedVaultContainer
                {
                    VaultVersion = "2.0",
                    IsEncrypted = true,
                    CipherPayload = cipherPayload
                };

                var vaultJson = JsonSerializer.Serialize(vault, JsonOptions);
                File.WriteAllText(ConfigFilePath, vaultJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save encrypted config: {ex.Message}");
            }
        }
    }
}
