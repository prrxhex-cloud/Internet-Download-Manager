// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TL;
using WTelegram;

namespace PRRX.IDM.Services
{
    public class TelegramMtprotoService : IDisposable
    {
        private static readonly Lazy<TelegramMtprotoService> _instance = new(() => new TelegramMtprotoService());
        public static TelegramMtprotoService Current => _instance.Value;

        private const int ApiId = 2040;

        private Client? _client;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private bool _isDisposed;

        private TelegramMtprotoService() { }

        public async Task<Client?> GetClientAsync(CancellationToken cancellationToken = default)
        {
            if (_client != null) return _client;

            await _initLock.WaitAsync(cancellationToken);
            try
            {
                if (_client != null) return _client;

                var botToken = await TelegramDownloadProvider.GetBotTokenAsync(cancellationToken);
                var apiHash = await TelegramDownloadProvider.GetApiHashAsync(cancellationToken);

                if (string.IsNullOrWhiteSpace(botToken))
                {
                    System.Diagnostics.Debug.WriteLine("[TelegramMtproto] Bot token is not configured on server.");
                    return null;
                }

                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var sessionDir = Path.Combine(appData, "PRRX Cooperation");
                Directory.CreateDirectory(sessionDir);
                var sessionPath = Path.Combine(sessionDir, "tg_bot_session.dat");

                var client = new Client(ApiId, apiHash ?? "", sessionPath)
                {
                    TcpHandler = async (host, port) =>
                    {
                        var tcp = new System.Net.Sockets.TcpClient();
                        tcp.NoDelay = true;
                        tcp.SendBufferSize = 1024 * 1024;
                        tcp.ReceiveBufferSize = 1024 * 1024;
                        await tcp.ConnectAsync(host, port);
                        return tcp;
                    },
                    FilePartSize = 512 * 1024
                };

                await client.LoginBotIfNeeded(botToken);

                try
                {
                    client.ParallelTransfers = 16;
                }
                catch { }

                _client = client;
                return _client;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TelegramMtproto] Init client failed: {ex.Message}");
                return null;
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <summary>
        /// Resolves a channel username and retrieves the Document object for the specified message ID.
        /// </summary>
        public async Task<Document?> GetChannelDocumentAsync(string channelUsername, long messageId, CancellationToken cancellationToken = default)
        {
            try
            {
                var cleanChannel = channelUsername.TrimStart('@').Trim();
                if (string.IsNullOrWhiteSpace(cleanChannel)) return null;

                var client = await GetClientAsync(cancellationToken);
                if (client == null) return null;
                var resolved = await client.Contacts_ResolveUsername(cleanChannel);
                if (resolved.Chat is Channel channel)
                {
                    var peer = new InputPeerChannel(channel.id, channel.access_hash);
                    var messages = await client.Channels_GetMessages(peer, new[] { (InputMessage)(int)messageId });

                    if (messages is Messages_ChannelMessages cm && cm.messages.Length > 0)
                    {
                        foreach (var m in cm.messages)
                        {
                            if (m is Message msg && msg.media is MessageMediaDocument mmd)
                            {
                                return mmd.document as Document;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TelegramMtproto] Resolve channel document failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Retrieves the Document object from a message sent directly or forwarded to the bot in its chat.
        /// </summary>
        public async Task<Document?> GetChatMessageDocumentAsync(long messageId, CancellationToken cancellationToken = default)
        {
            try
            {
                if (messageId <= 0) return null;
                var client = await GetClientAsync(cancellationToken);
                if (client == null) return null;
                var messages = await client.Messages_GetMessages(new InputMessage[] { (int)messageId });
                if (messages is Messages_MessagesBase mmb && mmb.Messages.Length > 0)
                {
                    foreach (var m in mmb.Messages)
                    {
                        if (m is Message msg && msg.media is MessageMediaDocument mmd && mmd.document is Document doc)
                        {
                            return doc;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TelegramMtproto] Get chat message document failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Downloads a Telegram Document directly from Telegram Data Centers into the provided stream.
        /// Bypasses all Bot API 20 MB limits up to 2 GB (Free) / 4 GB (Premium).
        /// </summary>
        public async Task<bool> DownloadDocumentAsync(
            Document document,
            Stream outputStream,
            Action<long, long>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var client = await GetClientAsync(cancellationToken);
                if (client == null) return false;

                // Download directly to stream with progress tracking
                await client.DownloadFileAsync(document, outputStream, (PhotoSizeBase?)null, (downloaded, total) =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }
                    progress?.Invoke(downloaded, total);
                });

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TelegramMtproto] Download failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Downloads a Telegram file location directly from the specified Data Center.
        /// </summary>
        public async Task<bool> DownloadFileLocationAsync(
            InputFileLocationBase fileLocation,
            Stream outputStream,
            int dcId,
            long fileSize,
            Action<long, long>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var client = await GetClientAsync(cancellationToken);
                if (client == null) return false;

                await client.DownloadFileAsync(fileLocation, outputStream, dcId, fileSize, (downloaded, total) =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }
                    progress?.Invoke(downloaded, total);
                });

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TelegramMtproto] Location download failed: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _client?.Dispose();
            _initLock.Dispose();
        }
    }
}
