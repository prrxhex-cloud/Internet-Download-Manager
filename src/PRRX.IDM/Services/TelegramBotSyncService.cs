// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PRRX.IDM.Models;

namespace PRRX.IDM.Services
{
    public class TelegramRemoteTask
    {
        public string Id { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; } = 0;
        public string FormattedSize { get; set; } = string.Empty;
        public string MediaType { get; set; } = "document";
        public string Source { get; set; } = "Telegram @PRRX_IDM_Bot";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public interface ITelegramBotSyncService
    {
        bool IsEnabled { get; set; }
        bool IsConnected { get; }
        string ClientId { get; }
        string PairingCode { get; }
        string BotUsername { get; }
        string BotDeepLink { get; }
        event EventHandler<TelegramRemoteTask>? TaskReceived;
        event EventHandler<string>? PairingCodeChanged;
        Task InitializeAsync();
        void Start();
        void Stop();
    }

    public class TelegramBotSyncService : ITelegramBotSyncService
    {
        private const string DefaultBaseUrl = "https://prrx-api.sayurusenavirathna70.workers.dev";
        private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = true
        })
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        public static TelegramBotSyncService? Current { get; internal set; }

        private readonly IConfigurationService? _configService;
        private readonly string _baseUrl;
        private CancellationTokenSource? _syncCts;
        private bool _isStarted;

        public bool IsEnabled { get; set; } = true;
        public bool IsConnected { get; private set; }
        public string ClientId { get; private set; } = string.Empty;
        public string PairingCode { get; private set; } = "PRRX-INIT";
        public string BotUsername => "PRRX_IDM_Bot";
        public string BotDeepLink => $"https://t.me/{BotUsername}?start={PairingCode}";

        public event EventHandler<TelegramRemoteTask>? TaskReceived;
        public event EventHandler<string>? PairingCodeChanged;

        static TelegramBotSyncService()
        {
            if (!SharedHttpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                SharedHttpClient.DefaultRequestHeaders.Add("User-Agent", "PRRX-IDM-TelegramSync/1.5.0");
            }
        }

        public TelegramBotSyncService(IConfigurationService? configService = null, string? baseUrl = null)
        {
            _configService = configService;
            _baseUrl = !string.IsNullOrWhiteSpace(baseUrl) ? baseUrl.TrimEnd('/') : DefaultBaseUrl;

            // Generate or load unique ClientId
            ClientId = Guid.NewGuid().ToString("N");
            Current = this;
        }

        public async Task InitializeAsync()
        {
            try
            {
                var pairUri = $"{_baseUrl}/api/telegram/pair";
                var payload = JsonSerializer.Serialize(new { clientId = ClientId });
                using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

                using var resp = await SharedHttpClient.PostAsync(pairUri, content);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("pairCode", out var codeElem))
                    {
                        PairingCode = codeElem.GetString() ?? "PRRX-FREE";
                        IsConnected = true;
                        PairingCodeChanged?.Invoke(this, PairingCode);
                    }
                }
            }
            catch
            {
                // Offline fallback pairing code
                PairingCode = "PRRX-" + ClientId.Substring(0, 4).ToUpperInvariant();
                PairingCodeChanged?.Invoke(this, PairingCode);
            }
        }

        public void Start()
        {
            if (_isStarted) return;
            _isStarted = true;
            _syncCts = new CancellationTokenSource();
            _ = Task.Run(() => PollTasksLoopAsync(_syncCts.Token));
        }

        public void Stop()
        {
            _isStarted = false;
            _syncCts?.Cancel();
            _syncCts?.Dispose();
            _syncCts = null;
        }

        private async Task PollTasksLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (IsEnabled)
                    {
                        await PollRemoteTasksAsync(ct);
                    }
                }
                catch
                {
                    // Ignore transient network errors
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(4), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task PollRemoteTasksAsync(CancellationToken ct)
        {
            var taskUri = $"{_baseUrl}/api/telegram/tasks?client_id={ClientId}";
            using var resp = await SharedHttpClient.GetAsync(taskUri, ct);
            if (!resp.IsSuccessStatusCode) return;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("tasks", out var tasksArray) && tasksArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var taskElem in tasksArray.EnumerateArray())
                {
                    var task = new TelegramRemoteTask
                    {
                        Id = taskElem.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "",
                        Url = taskElem.TryGetProperty("url", out var urlElem) ? urlElem.GetString() ?? "" : "",
                        FileName = taskElem.TryGetProperty("fileName", out var fnElem) ? fnElem.GetString() ?? "" : "telegram_file.bin",
                        FileSize = taskElem.TryGetProperty("fileSize", out var fsElem) ? fsElem.GetInt64() : 0,
                        FormattedSize = taskElem.TryGetProperty("formattedSize", out var fszElem) ? fszElem.GetString() ?? "" : "",
                        MediaType = taskElem.TryGetProperty("mediaType", out var mtElem) ? mtElem.GetString() ?? "document" : "document",
                        Source = taskElem.TryGetProperty("source", out var srcElem) ? srcElem.GetString() ?? "@PRRX_IDM_Bot" : "@PRRX_IDM_Bot"
                    };

                    if (!string.IsNullOrWhiteSpace(task.Url))
                    {
                        IsConnected = true;
                        TaskReceived?.Invoke(this, task);
                    }
                }
            }
        }
    }
}
