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
    }
}
