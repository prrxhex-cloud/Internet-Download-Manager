// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-TEST-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System.Threading.Tasks;
using PRRX.IDM.Services;
using Xunit;

namespace PRRX.IDM.Tests
{
    public class TelegramBotSyncTests
    {
        [Fact]
        public void TelegramBotSyncService_InitializesDefaults()
        {
            var syncService = new TelegramBotSyncService();
            Assert.NotNull(syncService.ClientId);
            Assert.NotEmpty(syncService.ClientId);
            Assert.Equal("PRRX_IDM_Bot", syncService.BotUsername);
            Assert.Contains(syncService.PairingCode, syncService.BotDeepLink);
            Assert.True(syncService.IsEnabled);
        }

        [Fact]
        public async Task TelegramBotSyncService_InitializeFallbackGeneratesPairCode()
        {
            // Providing an unresolvable or mock URL to test offline fallback resilience
            var syncService = new TelegramBotSyncService(baseUrl: "https://127.0.0.1:59999");
            await syncService.InitializeAsync();

            Assert.NotNull(syncService.PairingCode);
            Assert.StartsWith("PRRX-", syncService.PairingCode);
            Assert.Contains(syncService.PairingCode, syncService.BotDeepLink);
        }

        [Fact]
        public void TelegramBotSyncService_CanToggleEnabled()
        {
            var syncService = new TelegramBotSyncService();
            syncService.IsEnabled = false;
            Assert.False(syncService.IsEnabled);

            syncService.IsEnabled = true;
            Assert.True(syncService.IsEnabled);
        }

        private class TestConfigService : IConfigurationService
        {
            public Models.AppConfig CurrentConfig { get; set; } = new();
            public bool Saved { get; set; } = false;

            public void SaveConfig()
            {
                Saved = true;
            }

            public void LoadConfig()
            {
            }
        }

        [Fact]
        public void TelegramBotSyncService_GeneratesAndPersistsClientId_WhenConfigEmpty()
        {
            var configService = new TestConfigService();
            configService.CurrentConfig.TelegramClientId = string.Empty;

            var syncService = new TelegramBotSyncService(configService);

            Assert.NotEmpty(syncService.ClientId);
            Assert.Equal(configService.CurrentConfig.TelegramClientId, syncService.ClientId);
            Assert.True(configService.Saved);
        }

        [Fact]
        public void TelegramBotSyncService_LoadsExistingClientId_FromConfig()
        {
            var configService = new TestConfigService();
            var existingId = "client-fixed-guid-12345";
            configService.CurrentConfig.TelegramClientId = existingId;
            configService.CurrentConfig.IsTelegramBotSyncEnabled = false;

            var syncService = new TelegramBotSyncService(configService);

            Assert.Equal(existingId, syncService.ClientId);
            Assert.False(syncService.IsEnabled);
        }

        [Fact]
        public void TelegramBotSyncService_SyncsIsEnabled_WithConfig()
        {
            var configService = new TestConfigService();
            var syncService = new TelegramBotSyncService(configService);

            syncService.IsEnabled = false;
            Assert.False(configService.CurrentConfig.IsTelegramBotSyncEnabled);
            Assert.True(configService.Saved);

            syncService.IsEnabled = true;
            Assert.True(configService.CurrentConfig.IsTelegramBotSyncEnabled);
        }

        [Fact]
        public async Task TelegramBotSyncService_FallbackWithShortClientId_DoesNotThrow()
        {
            var configService = new TestConfigService();
            configService.CurrentConfig.TelegramClientId = "ab";

            var syncService = new TelegramBotSyncService(configService, baseUrl: "https://127.0.0.1:59999");
            await syncService.InitializeAsync();

            Assert.NotNull(syncService.PairingCode);
            Assert.StartsWith("PRRX-AB", syncService.PairingCode);
        }

        [Fact]
        public async Task TelegramBotSyncService_InitializeFiresPairingCodeChangedEvent()
        {
            var syncService = new TelegramBotSyncService(baseUrl: "https://127.0.0.1:59999");
            string? changedCode = null;
            syncService.PairingCodeChanged += (s, code) => changedCode = code;

            await syncService.InitializeAsync();

            Assert.NotNull(changedCode);
            Assert.Equal(syncService.PairingCode, changedCode);
        }

        [Fact]
        public void TelegramRemoteTask_RetainsAllMetadataProperties()
        {
            var task = new TelegramRemoteTask
            {
                Id = "tg_12345",
                Url = "tg://file?file_id=TEST_ID",
                FileName = "4k_video.mp4",
                FileSize = 3500000000,
                FormattedSize = "3.26 GB",
                MediaType = "video",
                Source = "Telegram @PRRX_IDM_Bot",
                FileId = "BAACAgIAAxkBAAIB789",
                MimeType = "video/mp4",
                ChatId = "12345678",
                MessageId = 555
            };

            Assert.Equal("tg_12345", task.Id);
            Assert.Equal("4k_video.mp4", task.FileName);
            Assert.Equal(3500000000, task.FileSize);
            Assert.Equal("3.26 GB", task.FormattedSize);
            Assert.Equal("video", task.MediaType);
            Assert.Equal("Telegram @PRRX_IDM_Bot", task.Source);
            Assert.Equal("BAACAgIAAxkBAAIB789", task.FileId);
            Assert.Equal("video/mp4", task.MimeType);
            Assert.Equal("12345678", task.ChatId);
            Assert.Equal(555, task.MessageId);
        }

        [Fact]
        public void DownloadFileInfoViewModel_RetainsPrecalculatedSize_ForLargeTelegramTasks()
        {
            long largeSize = 2500000000; // 2.5 GB
            var tgUrl = $"tg://file?file_id=LARGE_TG_FILE&file_name=Archive_2026.zip&file_size={largeSize}&mime_type=application%2Fzip&chat_id=123&message_id=456";

            var vm = new PRRX.IDM.ViewModels.DownloadFileInfoViewModel(
                tgUrl,
                @"C:\Downloads",
                "Telegram File (Telegram @PRRX_IDM_Bot)",
                largeSize,
                "Archive_2026.zip",
                null,
                "Telegram @PRRX_IDM_Bot");

            Assert.Equal("Archive_2026.zip", vm.FileName);
            Assert.Contains("2.33 GB", vm.FileSizeFormatted);
            Assert.Contains("Archive_2026.zip", vm.SaveAsFullPath);
            Assert.Equal(largeSize, vm.DetectedBytes);
        }

        [Fact]
        public async Task DownloadFileInfoViewModel_PreservesPrecalculatedSize_WhenProbeReturnsNull()
        {
            long precalculated = 1800000000; // ~1.68 GB
            // Using non-tg URL that cannot be probed (offline/invalid)
            var vm = new PRRX.IDM.ViewModels.DownloadFileInfoViewModel(
                "https://127.0.0.1:59999/large_file.iso",
                @"C:\Downloads",
                "Web Download",
                precalculated,
                "large_file.iso");

            // Wait briefly for the non-blocking background probe to fail/finish
            await Task.Delay(500);

            // Verify that the precalculated size was NOT wiped out or overwritten with "Unknown size"
            Assert.Equal(precalculated, vm.DetectedBytes);
            Assert.Contains("1.68 GB", vm.FileSizeFormatted);
        }
    }
}
