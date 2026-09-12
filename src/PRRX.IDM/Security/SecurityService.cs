// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PRRX.IDM.Security
{
    public interface ISecurityService
    {
        byte[] ProtectData(byte[] plaintext, byte[]? optionalEntropy = null);
        byte[] UnprotectData(byte[] ciphertext, byte[]? optionalEntropy = null);
        string ProtectString(string plaintext);
        string UnprotectString(string ciphertextBase64);

        byte[] EncryptAesGcm(byte[] plaintext, byte[] key);
        byte[] DecryptAesGcm(byte[] ciphertextWithNonceAndTag, byte[] key);
        string EncryptAesGcmString(string plaintext, byte[] key);
        string DecryptAesGcmString(string ciphertextBase64, byte[] key);

        byte[] GetOrCreateMasterKey();
        string GetOrCreateIpcToken();
        bool ValidateIpcToken(string? token);
        bool ValidateOrigin(string? origin);
        void ZeroMemory(byte[] buffer);
        void ZeroMemory(char[] buffer);
    }

    public class SecurityService : ISecurityService
    {
        private const int GcmNonceSize = 12; // 96-bit nonce standard for GCM
        private const int GcmTagSize = 16;   // 128-bit authentication tag
        private const int KeySize = 32;       // 256-bit key

        private static readonly string VaultDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PRRX Cooperation",
            "InternetDownloadManager");

        private static readonly string MasterKeyPath = Path.Combine(VaultDirectory, "vault.key");
        private static readonly string IpcTokenPath = Path.Combine(VaultDirectory, "ipc.token");

        private readonly object _lock = new();
        private byte[]? _cachedMasterKey;
        private string? _cachedIpcToken;

        public SecurityService()
        {
            EnsureVaultDirectory();
        }

        private static void EnsureVaultDirectory()
        {
            if (!Directory.Exists(VaultDirectory))
            {
                Directory.CreateDirectory(VaultDirectory);
            }
        }

        public byte[] ProtectData(byte[] plaintext, byte[]? optionalEntropy = null)
        {
            if (plaintext == null || plaintext.Length == 0) return Array.Empty<byte>();
            return ProtectedData.Protect(plaintext, optionalEntropy, DataProtectionScope.CurrentUser);
        }

        public byte[] UnprotectData(byte[] ciphertext, byte[]? optionalEntropy = null)
        {
            if (ciphertext == null || ciphertext.Length == 0) return Array.Empty<byte>();
            return ProtectedData.Unprotect(ciphertext, optionalEntropy, DataProtectionScope.CurrentUser);
        }

        public string ProtectString(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return string.Empty;
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            try
            {
                var protectedBytes = ProtectData(bytes);
                return Convert.ToBase64String(protectedBytes);
            }
            finally
            {
                ZeroMemory(bytes);
            }
        }

        public string UnprotectString(string ciphertextBase64)
        {
            if (string.IsNullOrWhiteSpace(ciphertextBase64)) return string.Empty;
            try
            {
                var ciphertext = Convert.FromBase64String(ciphertextBase64);
                var decrypted = UnprotectData(ciphertext);
                try
                {
                    return Encoding.UTF8.GetString(decrypted);
                }
                finally
                {
                    ZeroMemory(decrypted);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        public byte[] EncryptAesGcm(byte[] plaintext, byte[] key)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            if (key == null || key.Length != KeySize) throw new ArgumentException($"Key must be exactly {KeySize} bytes for AES-256.", nameof(key));

            var nonce = new byte[GcmNonceSize];
            RandomNumberGenerator.Fill(nonce);

            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[GcmTagSize];

            using (var aesGcm = new AesGcm(key, GcmTagSize))
            {
                aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
            }

            // Output format: [12-byte Nonce][16-byte Tag][Ciphertext]
            var result = new byte[GcmNonceSize + GcmTagSize + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, GcmNonceSize);
            Buffer.BlockCopy(tag, 0, result, GcmNonceSize, GcmTagSize);
            Buffer.BlockCopy(ciphertext, 0, result, GcmNonceSize + GcmTagSize, ciphertext.Length);

            return result;
        }

        public byte[] DecryptAesGcm(byte[] ciphertextWithNonceAndTag, byte[] key)
        {
            if (ciphertextWithNonceAndTag == null) throw new ArgumentNullException(nameof(ciphertextWithNonceAndTag));
            if (key == null || key.Length != KeySize) throw new ArgumentException($"Key must be exactly {KeySize} bytes for AES-256.", nameof(key));

            if (ciphertextWithNonceAndTag.Length < GcmNonceSize + GcmTagSize)
            {
                throw new CryptographicException("Ciphertext is too short to contain valid AES-GCM nonce and authentication tag.");
            }

            var nonce = new byte[GcmNonceSize];
            var tag = new byte[GcmTagSize];
            int cipherTextLength = ciphertextWithNonceAndTag.Length - GcmNonceSize - GcmTagSize;
            var ciphertext = new byte[cipherTextLength];

            Buffer.BlockCopy(ciphertextWithNonceAndTag, 0, nonce, 0, GcmNonceSize);
            Buffer.BlockCopy(ciphertextWithNonceAndTag, GcmNonceSize, tag, 0, GcmTagSize);
            Buffer.BlockCopy(ciphertextWithNonceAndTag, GcmNonceSize + GcmTagSize, ciphertext, 0, cipherTextLength);

            var plaintext = new byte[cipherTextLength];
            using (var aesGcm = new AesGcm(key, GcmTagSize))
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
            }

            return plaintext;
        }

        public string EncryptAesGcmString(string plaintext, byte[] key)
        {
            if (string.IsNullOrEmpty(plaintext)) return string.Empty;
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            try
            {
                var encrypted = EncryptAesGcm(bytes, key);
                return Convert.ToBase64String(encrypted);
            }
            finally
            {
                ZeroMemory(bytes);
            }
        }

        public string DecryptAesGcmString(string ciphertextBase64, byte[] key)
        {
            if (string.IsNullOrWhiteSpace(ciphertextBase64)) return string.Empty;
            var cipherBytes = Convert.FromBase64String(ciphertextBase64);
            var decrypted = DecryptAesGcm(cipherBytes, key);
            try
            {
                return Encoding.UTF8.GetString(decrypted);
            }
            finally
            {
                ZeroMemory(decrypted);
            }
        }

        public byte[] GetOrCreateMasterKey()
        {
            lock (_lock)
            {
                if (_cachedMasterKey != null && _cachedMasterKey.Length == KeySize)
                {
                    return (byte[])_cachedMasterKey.Clone();
                }

                EnsureVaultDirectory();

                if (File.Exists(MasterKeyPath))
                {
                    try
                    {
                        var encryptedKey = File.ReadAllBytes(MasterKeyPath);
                        var key = UnprotectData(encryptedKey);
                        if (key.Length == KeySize)
                        {
                            _cachedMasterKey = key;
                            return (byte[])_cachedMasterKey.Clone();
                        }
                    }
                    catch
                    {
                        // Key corrupted, regenerate below
                    }
                }

                // Generate a fresh cryptographically secure 256-bit key
                var newKey = new byte[KeySize];
                RandomNumberGenerator.Fill(newKey);

                try
                {
                    var protectedKey = ProtectData(newKey);
                    File.WriteAllBytes(MasterKeyPath, protectedKey);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to write master key to vault: {ex.Message}");
                }

                _cachedMasterKey = newKey;
                return (byte[])_cachedMasterKey.Clone();
            }
        }

        public string GetOrCreateIpcToken()
        {
            lock (_lock)
            {
                if (!string.IsNullOrWhiteSpace(_cachedIpcToken))
                {
                    return _cachedIpcToken;
                }

                EnsureVaultDirectory();

                if (File.Exists(IpcTokenPath))
                {
                    try
                    {
                        var enc = File.ReadAllText(IpcTokenPath);
                        var token = UnprotectString(enc);
                        if (!string.IsNullOrWhiteSpace(token) && token.Length >= 32)
                        {
                            _cachedIpcToken = token;
                            return _cachedIpcToken;
                        }
                    }
                    catch
                    {
                        // Regenerate below
                    }
                }

                var randomBytes = new byte[32];
                RandomNumberGenerator.Fill(randomBytes);
                var newToken = Convert.ToHexString(randomBytes).ToLowerInvariant();

                try
                {
                    var protectedToken = ProtectString(newToken);
                    File.WriteAllText(IpcTokenPath, protectedToken);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to persist IPC token: {ex.Message}");
                }

                _cachedIpcToken = newToken;
                return _cachedIpcToken;
            }
        }

        public bool ValidateIpcToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            var expected = GetOrCreateIpcToken();

            var tokenBytes = Encoding.UTF8.GetBytes(token.Trim());
            var expectedBytes = Encoding.UTF8.GetBytes(expected);

            if (tokenBytes.Length != expectedBytes.Length) return false;
            return CryptographicOperations.FixedTimeEquals(tokenBytes, expectedBytes);
        }

        public bool ValidateOrigin(string? origin)
        {
            if (string.IsNullOrWhiteSpace(origin)) return false;
            var trimmed = origin.Trim();

            // Strictly reject public/internet web origins (e.g. http://, https://, null, file://)
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Only permit recognized Chrome/Edge/Firefox extension origins
            if (trimmed.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("edge-extension://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("extension://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public void ZeroMemory(byte[] buffer)
        {
            if (buffer != null)
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }

        public void ZeroMemory(char[] buffer)
        {
            if (buffer != null)
            {
                Array.Clear(buffer, 0, buffer.Length);
            }
        }
    }
}
