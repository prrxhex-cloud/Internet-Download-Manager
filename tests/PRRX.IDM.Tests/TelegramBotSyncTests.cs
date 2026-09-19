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
    }
}
