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
        private const string ApiHash = "b18441a1ff607e10a989891a5462e627";
        private const string BotToken = "8728261333:AAHuFJ7bhIZ_jElnnIo6h_BgGQzpE1niEr4";

        private Client? _client;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private bool _isDisposed;

        private TelegramMtprotoService() { }

        public async Task<Client> GetClientAsync(CancellationToken cancellationToken = default)
        {
            if (_client != null) return _client;

            await _initLock.WaitAsync(cancellationToken);
            try
            {
                if (_client != null) return _client;

                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var sessionDir = Path.Combine(appData, "PRRX_IDM");
                Directory.CreateDirectory(sessionDir);
                var sessionPath = Path.Combine(sessionDir, "tg_bot_session.dat");

                var client = new Client(ApiId, ApiHash, sessionPath);
                await client.LoginBotIfNeeded(BotToken);

                _client = client;
                return _client;
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
