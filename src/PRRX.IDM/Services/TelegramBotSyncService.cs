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
        public string FileId { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public string ChatId { get; set; } = string.Empty;
        public long MessageId { get; set; } = 0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum TelegramBotConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        PairingRequired
    }

    public interface ITelegramBotSyncService
    {
        bool IsEnabled { get; set; }
        bool IsConnected { get; }
        TelegramBotConnectionStatus Status { get; }
        string StatusText { get; }
        string StatusColorHex { get; }
        string ClientId { get; }
        string PairingCode { get; }
        string BotUsername { get; }
        string BotDeepLink { get; }
        string? LinkedChatId { get; }
        string? LinkedUsername { get; }
        DateTime? LastHeartbeat { get; }
        int PendingTaskCount { get; }
        event EventHandler<TelegramRemoteTask>? TaskReceived;
        event EventHandler<string>? PairingCodeChanged;
        event EventHandler? StatusChanged;
        Task InitializeAsync();
        void Start();
        void Stop();
        Task<List<TelegramRemoteTask>> HydratePendingTasksAsync(CancellationToken ct = default);
        Task<bool> AcknowledgeTasksAsync(IEnumerable<string> taskIds, CancellationToken ct = default);
        Task<bool> TestConnectionAsync(CancellationToken ct = default);
        Task ForceHeartbeatAsync(CancellationToken ct = default);
        Task<bool> UnlinkAsync(CancellationToken ct = default);
        Task SendOfflineStatusAsync(CancellationToken ct = default);
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
            Timeout = TimeSpan.FromSeconds(6)
        };

        public static TelegramBotSyncService? Current { get; internal set; }

        private readonly IConfigurationService? _configService;
        private readonly string _baseUrl;
        private CancellationTokenSource? _syncCts;
        private bool _isStarted;

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                if (_configService != null && _configService.CurrentConfig.IsTelegramBotSyncEnabled != value)
                {
                    _configService.CurrentConfig.IsTelegramBotSyncEnabled = value;
                    _configService.SaveConfig();
                }
            }
        }

        private TelegramBotConnectionStatus _status = TelegramBotConnectionStatus.Connecting;
        public TelegramBotConnectionStatus Status
        {
            get => _status;
            private set => UpdateStatus(value);
        }

        public bool IsConnected => Status == TelegramBotConnectionStatus.Connected;

        public string StatusText => Status switch
        {
            TelegramBotConnectionStatus.Connected => "Connected & Active",
            TelegramBotConnectionStatus.Connecting => "Connecting / Polling...",
            TelegramBotConnectionStatus.PairingRequired => "Pairing Required",
            TelegramBotConnectionStatus.Disconnected => "Disconnected / Offline",
            _ => "Unknown"
        };

        public string StatusColorHex => Status switch
        {
            TelegramBotConnectionStatus.Connected => "#107C41",
            TelegramBotConnectionStatus.Connecting => "#CA5010",
            TelegramBotConnectionStatus.PairingRequired => "#F7630C",
            TelegramBotConnectionStatus.Disconnected => "#8A8886",
            _ => "#8A8886"
        };

        public string ClientId { get; private set; } = string.Empty;
        public string PairingCode { get; private set; } = "PRRX-INIT";
        public string BotUsername => "PRRX_IDM_Bot";
        public string BotDeepLink => $"https://t.me/{BotUsername}?start={PairingCode}";
        public string? LinkedChatId { get; private set; }
        public string? LinkedUsername { get; private set; }
        public DateTime? LastHeartbeat { get; private set; }
        public int PendingTaskCount { get; private set; }

        public event EventHandler<TelegramRemoteTask>? TaskReceived;
        public event EventHandler<string>? PairingCodeChanged;
        public event EventHandler? StatusChanged;

        static TelegramBotSyncService()
        {
            if (!SharedHttpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                SharedHttpClient.DefaultRequestHeaders.Add("User-Agent", "PRRX-IDM-TelegramSync/1.8.0");
            }
        }

        private DateTime _lastPairRegistered = DateTime.MinValue;

        public TelegramBotSyncService(IConfigurationService? configService = null, string? baseUrl = null)
        {
            _configService = configService;
            _baseUrl = !string.IsNullOrWhiteSpace(baseUrl) ? baseUrl.TrimEnd('/') : DefaultBaseUrl;

            if (_configService != null)
            {
                _isEnabled = _configService.CurrentConfig.IsTelegramBotSyncEnabled;
                if (string.IsNullOrWhiteSpace(_configService.CurrentConfig.TelegramClientId))
                {
                    _configService.CurrentConfig.TelegramClientId = Guid.NewGuid().ToString("N");
                    _configService.SaveConfig();
                }
                ClientId = _configService.CurrentConfig.TelegramClientId;
            }
            else
            {
                ClientId = Guid.NewGuid().ToString("N");
            }

            Current = this;
        }

        private void UpdateStatus(TelegramBotConnectionStatus newStatus)
        {
            if (_status != newStatus)
            {
                _status = newStatus;
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public async Task InitializeAsync()
        {
            try
            {
                UpdateStatus(TelegramBotConnectionStatus.Connecting);
                var pairUri = $"{_baseUrl}/api/telegram/pair";
                var payloadObj = new
                {
                    clientId = ClientId,
                    pairCode = (PairingCode != "PRRX-INIT" && PairingCode.StartsWith("PRRX-")) ? PairingCode : null
                };
                var payload = JsonSerializer.Serialize(payloadObj);
                using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

                using var resp = await SharedHttpClient.PostAsync(pairUri, content);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("pairCode", out var codeElem) &&
                        codeElem.GetString() is { } code && !string.IsNullOrWhiteSpace(code))
                    {
                        PairingCode = code;
                        _lastPairRegistered = DateTime.UtcNow;
                        PairingCodeChanged?.Invoke(this, PairingCode);
                        await SendHeartbeatAsync();
                        return;
                    }
                }

                ApplyFallbackPairingCode();
            }
            catch
            {
                ApplyFallbackPairingCode();
                UpdateStatus(TelegramBotConnectionStatus.Disconnected);
            }
        }

        private void ApplyFallbackPairingCode()
        {
            var prefix = (ClientId.Length >= 4 ? ClientId.Substring(0, 4) : ClientId.PadRight(4, '0')).ToUpperInvariant();
            PairingCode = "PRRX-" + prefix;
            PairingCodeChanged?.Invoke(this, PairingCode);
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
            UpdateStatus(TelegramBotConnectionStatus.Disconnected);
        }

        public async Task<bool> SendHeartbeatAsync(CancellationToken ct = default)
        {
            try
            {
                var heartbeatUri = $"{_baseUrl}/api/telegram/heartbeat";
                var payload = JsonSerializer.Serialize(new
                {
                    clientId = ClientId,
                    status = "online"
                });
                using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                using var resp = await SharedHttpClient.PostAsync(heartbeatUri, content, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    bool linked = root.TryGetProperty("linked", out var linkedElem) && linkedElem.GetBoolean();
                    LastHeartbeat = DateTime.UtcNow;

                    if (linked)
                    {
                        LinkedChatId = root.TryGetProperty("chatId", out var cElem) ? cElem.GetString() : null;
                        LinkedUsername = root.TryGetProperty("username", out var uElem) ? uElem.GetString() : null;
                        PendingTaskCount = root.TryGetProperty("pendingTasks", out var pElem) && pElem.TryGetInt32(out var cnt) ? cnt : 0;
                        UpdateStatus(TelegramBotConnectionStatus.Connected);
                    }
                    else
                    {
                        LinkedChatId = null;
                        LinkedUsername = null;
                        PendingTaskCount = 0;
                        UpdateStatus(TelegramBotConnectionStatus.PairingRequired);
                    }
                    return true;
                }
                else
                {
                    UpdateStatus(TelegramBotConnectionStatus.Disconnected);
                    return false;
                }
            }
            catch
            {
                UpdateStatus(TelegramBotConnectionStatus.Disconnected);
                return false;
            }
        }

        public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
        {
            UpdateStatus(TelegramBotConnectionStatus.Connecting);
            return await SendHeartbeatAsync(ct);
        }

        public async Task ForceHeartbeatAsync(CancellationToken ct = default)
        {
            await SendHeartbeatAsync(ct);
            if (IsEnabled)
            {
                await PollRemoteTasksAsync(ct);
            }
        }

        public async Task<bool> UnlinkAsync(CancellationToken ct = default)
        {
            try
            {
                var unlinkUri = $"{_baseUrl}/api/telegram/unlink";
                var payload = JsonSerializer.Serialize(new { clientId = ClientId });
                using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                using var resp = await SharedHttpClient.PostAsync(unlinkUri, content, ct);

                LinkedChatId = null;
                LinkedUsername = null;
                PendingTaskCount = 0;
                UpdateStatus(TelegramBotConnectionStatus.PairingRequired);
                await InitializeAsync();
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task SendOfflineStatusAsync(CancellationToken ct = default)
        {
            try
            {
                var statusUri = $"{_baseUrl}/api/telegram/status";
                var payload = JsonSerializer.Serialize(new
                {
                    clientId = ClientId,
                    status = "offline"
                });
                using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                using var resp = await SharedHttpClient.PostAsync(statusUri, content, ct);
            }
            catch
            {
                // Best effort on exit
            }
        }

        private async Task PollTasksLoopAsync(CancellationToken ct)
        {
            int cycleCount = 0;
            UpdateStatus(TelegramBotConnectionStatus.Connecting);

            while (!ct.IsCancellationRequested)
            {
                cycleCount++;
                try
                {
                    // Auto-retry registration if disconnected, or renew pair code before 1-hour expiration
                    if (Status == TelegramBotConnectionStatus.Disconnected || (DateTime.UtcNow - _lastPairRegistered).TotalMinutes >= 45)
                    {
                        if (Status == TelegramBotConnectionStatus.Disconnected || cycleCount % 7 == 0)
                        {
                            await InitializeAsync();
                        }
                    }

                    await SendHeartbeatAsync(ct);

                    if (IsEnabled)
                    {
                        await PollRemoteTasksAsync(ct);
                    }
                }
                catch
                {
                    UpdateStatus(TelegramBotConnectionStatus.Disconnected);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }


        public async Task<bool> AcknowledgeTasksAsync(IEnumerable<string> taskIds, CancellationToken ct = default)
        {
            try
            {
                var list = new List<string>(taskIds);
                if (list.Count == 0) return true;

                var ackUri = $"{_baseUrl}/api/telegram/ack";
                var payload = JsonSerializer.Serialize(new
                {
                    clientId = ClientId,
                    taskIds = list
                });

                using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                using var resp = await SharedHttpClient.PostAsync(ackUri, content, ct);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<TelegramRemoteTask>> HydratePendingTasksAsync(CancellationToken ct = default)
        {
            var results = new List<TelegramRemoteTask>();
            if (!IsEnabled) return results;

            try
            {
                var taskUri = $"{_baseUrl}/api/telegram/tasks?client_id={ClientId}";
                using var resp = await SharedHttpClient.GetAsync(taskUri, ct);
                if (!resp.IsSuccessStatusCode) return results;

                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                var ackIds = new List<string>();

                if (doc.RootElement.TryGetProperty("tasks", out var tasksArray) && tasksArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var taskElem in tasksArray.EnumerateArray())
                    {
                        var task = ParseTaskFromJson(taskElem);
                        if (!string.IsNullOrWhiteSpace(task.Url))
                        {
                            results.Add(task);
                            if (!string.IsNullOrWhiteSpace(task.Id))
                            {
                                ackIds.Add(task.Id);
                            }
                            UpdateStatus(TelegramBotConnectionStatus.Connected);
                            TaskReceived?.Invoke(this, task);
                        }
                    }
                }

                if (ackIds.Count > 0)
                {
                    await AcknowledgeTasksAsync(ackIds, ct);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TelegramBotSync] Hydrate error: {ex.Message}");
            }

            return results;
        }

        private async Task PollRemoteTasksAsync(CancellationToken ct)
        {
            var taskUri = $"{_baseUrl}/api/telegram/tasks?client_id={ClientId}";
            using var resp = await SharedHttpClient.GetAsync(taskUri, ct);
            if (!resp.IsSuccessStatusCode) return;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var ackIds = new List<string>();

            if (doc.RootElement.TryGetProperty("tasks", out var tasksArray) && tasksArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var taskElem in tasksArray.EnumerateArray())
                {
                    var task = ParseTaskFromJson(taskElem);
                    if (!string.IsNullOrWhiteSpace(task.Url))
                    {
                        if (!string.IsNullOrWhiteSpace(task.Id))
                        {
                            ackIds.Add(task.Id);
                        }
                        UpdateStatus(TelegramBotConnectionStatus.Connected);
                        TaskReceived?.Invoke(this, task);
                    }
                }
            }

            if (ackIds.Count > 0)
            {
                await AcknowledgeTasksAsync(ackIds, ct);
            }
        }

        internal static TelegramRemoteTask ParseTaskFromJson(JsonElement taskElem)
        {
            var task = new TelegramRemoteTask
            {
                Id = taskElem.TryGetProperty("id", out var idElem) ? idElem.GetString() ?? "" : "",
                Url = taskElem.TryGetProperty("url", out var urlElem) ? urlElem.GetString() ?? "" : "",
                FileName = taskElem.TryGetProperty("fileName", out var fnElem) ? fnElem.GetString() ?? "" : "telegram_file.bin",
                FileSize = taskElem.TryGetProperty("fileSize", out var fsElem) && fsElem.TryGetInt64(out var fsVal) ? fsVal : 0,
                FormattedSize = taskElem.TryGetProperty("formattedSize", out var fszElem) ? fszElem.GetString() ?? "" : "",
                MediaType = taskElem.TryGetProperty("mediaType", out var mtElem) ? mtElem.GetString() ?? "document" : "document",
                Source = taskElem.TryGetProperty("source", out var srcElem) ? srcElem.GetString() ?? "@PRRX_IDM_Bot" : "@PRRX_IDM_Bot",
                FileId = taskElem.TryGetProperty("fileId", out var fidElem) ? fidElem.GetString() ?? "" : "",
                MimeType = taskElem.TryGetProperty("mimeType", out var mtpElem) ? mtpElem.GetString() ?? "" : "",
                ChatId = taskElem.TryGetProperty("chatId", out var cidElem) ? cidElem.GetString() ?? "" : "",
                MessageId = taskElem.TryGetProperty("messageId", out var midElem) && midElem.TryGetInt64(out var midVal) ? midVal : 0
            };

            if (string.IsNullOrWhiteSpace(task.Url) && !string.IsNullOrWhiteSpace(task.FileId))
            {
                task.Url = $"tg://file?file_id={task.FileId}&file_name={Uri.EscapeDataString(task.FileName)}&file_size={task.FileSize}&mime_type={Uri.EscapeDataString(task.MimeType)}&chat_id={task.ChatId}&message_id={task.MessageId}";
            }

            if (task.FileSize > 0 && string.IsNullOrWhiteSpace(task.FormattedSize))
            {
                task.FormattedSize = TelegramDownloadProvider.FormatBytes(task.FileSize);
            }

            return task;
        }
    }
}
