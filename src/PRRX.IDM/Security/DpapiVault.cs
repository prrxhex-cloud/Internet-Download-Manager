// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

using System;
using System.Security.Cryptography;
using System.Text;

namespace PRRX.IDM.Security
{
    /// <summary>
    /// Provides Windows Data Protection API (DPAPI) hardware-tied encryption for credentials,
    /// tokens, and unique client identifiers stored on disk.
    /// </summary>
    public static class DpapiVault
    {
        private const string DpapiPrefix = "dpapi:";

        /// <summary>
        /// Encrypts plaintext using Windows DPAPI tied to the current Windows user profile.
        /// </summary>
        public static string ProtectString(string? plaintext)
        {
            if (string.IsNullOrWhiteSpace(plaintext)) return string.Empty;

            try
            {
                if (!OperatingSystem.IsWindows()) return plaintext;

                var bytes = Encoding.UTF8.GetBytes(plaintext);
                var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return DpapiPrefix + Convert.ToBase64String(protectedBytes);
            }
            catch
            {
                return plaintext;
            }
        }

        /// <summary>
        /// Decrypts a DPAPI-protected string, with graceful fallback for legacy plaintext.
        /// </summary>
        public static string UnprotectString(string? ciphertext)
        {
            if (string.IsNullOrWhiteSpace(ciphertext)) return string.Empty;

            try
            {
                if (!OperatingSystem.IsWindows()) return ciphertext;

                if (ciphertext.StartsWith(DpapiPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var base64 = ciphertext.Substring(DpapiPrefix.Length);
                    var protectedBytes = Convert.FromBase64String(base64);
                    var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(bytes);
                }

                // If not prefixed, attempt standard DPAPI base64 unprotect
                var rawBytes = Convert.FromBase64String(ciphertext);
                var decBytes = ProtectedData.Unprotect(rawBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decBytes);
            }
            catch
            {
                // Fallback: value is unencrypted legacy plaintext
                return ciphertext;
            }
        }
    }
}
