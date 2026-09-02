using System;
using System.IO;
using System.Text.Json;
using PRRX.IDM.Models;

namespace PRRX.IDM.Services
{
    public interface IConfigurationService
    {
        AppConfig CurrentConfig { get; }
        void SaveConfig();
        void LoadConfig();
    }

    public class ConfigurationService : IConfigurationService
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PRRX Cooperation",
            "Internet Download Manager");

        private static readonly string ConfigFilePath = Path.Combine(AppDataFolder, "config.json");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public AppConfig CurrentConfig { get; private set; } = new();

        public ConfigurationService()
        {
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
                if (File.Exists(ConfigFilePath))
                {
                    var json = File.ReadAllText(ConfigFilePath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
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
                    "Downloads",
                    "PRRX Downloads");
            }

            if (!Directory.Exists(CurrentConfig.DownloadDirectory))
            {
                try
                {
                    Directory.CreateDirectory(CurrentConfig.DownloadDirectory);
                }
                catch
                {
                    // Fallback to Desktop or standard Downloads
                    CurrentConfig.DownloadDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }
            }
        }

        public void SaveConfig()
        {
            try
            {
                EnsureDirectoryExists();
                var json = JsonSerializer.Serialize(CurrentConfig, JsonOptions);
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
            }
        }
    }
}
