// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PRRX.IDM.Security
{
    public static class SecurityGuard
    {
        private static readonly Regex SafeUrlRegex = new(
            @"^(https?):\/\/[^\s]+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string[] DangerousFlagPrefixes = new[]
        {
            "--exec",
            "--config-location",
            "--external-downloader-args",
            "--batch-file",
            "--load-info-json"
        };

        public static bool ValidateUrl(string? inputUrl, out string sanitizedUrl, out string errorMessage)
        {
            sanitizedUrl = string.Empty;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(inputUrl))
            {
                errorMessage = "URL cannot be empty.";
                return false;
            }

            var trimmed = inputUrl.Trim();

            // Guard against command-line flag injection
            if (trimmed.StartsWith("-", StringComparison.Ordinal))
            {
                errorMessage = "Input cannot begin with command argument flags.";
                return false;
            }

            foreach (var flag in DangerousFlagPrefixes)
            {
                if (trimmed.Contains(flag, StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = $"Input contains prohibited execution flags: '{flag}'";
                    return false;
                }
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uriResult) ||
                (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
            {
                errorMessage = "Invalid URL structure. Only HTTP and HTTPS protocols are allowed.";
                return false;
            }

            if (!SafeUrlRegex.IsMatch(trimmed))
            {
                errorMessage = "The provided URL contains invalid whitespace or unsupported characters.";
                return false;
            }

            sanitizedUrl = uriResult.ToString();
            return true;
        }

        public static string SanitizeFileName(string rawFileName)
        {
            if (string.IsNullOrWhiteSpace(rawFileName))
            {
                return $"PRRX_Download_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", rawFileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();

            // Prevent path traversal sequences
            sanitized = sanitized.Replace("..", "").Replace("/", "").Replace("\\", "");

            if (sanitized.Length > 180)
            {
                sanitized = sanitized.Substring(0, 180);
            }

            return string.IsNullOrWhiteSpace(sanitized) ? $"PRRX_Download_{DateTime.UtcNow:yyyyMMdd_HHmmss}" : sanitized;
        }

        public static bool IsPathWithinDirectory(string baseDirectory, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory) || string.IsNullOrWhiteSpace(targetPath)) return false;
            var safeDir = Path.GetFullPath(baseDirectory);
            var normalizedSafeDir = safeDir.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? safeDir
                : safeDir + Path.DirectorySeparatorChar;

            var fullTarget = Path.GetFullPath(targetPath);
            return fullTarget.StartsWith(normalizedSafeDir, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fullTarget, safeDir, StringComparison.OrdinalIgnoreCase);
        }

        public static string EnsureSafePath(string baseDirectory, string fileName)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                throw new ArgumentException("Base directory cannot be empty.", nameof(baseDirectory));
            }

            var safeDir = Path.GetFullPath(baseDirectory);
            var normalizedSafeDir = safeDir.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? safeDir
                : safeDir + Path.DirectorySeparatorChar;

            var cleanName = SanitizeFileName(fileName);
            var combined = Path.GetFullPath(Path.Combine(safeDir, cleanName));

            // Verify canonical path does not escape base directory using strict boundary checking
            if (!combined.StartsWith(normalizedSafeDir, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(combined, safeDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Path traversal detected! Attempted path escapes base directory.");
            }

            return combined;
        }

        public static bool VerifySha256(string filePath, string expectedHexHash)
        {
            if (!File.Exists(filePath) || string.IsNullOrWhiteSpace(expectedHexHash)) return false;

            try
            {
                using var sha256 = SHA256.Create();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var hashBytes = sha256.ComputeHash(stream);
                var computedHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                return string.Equals(computedHex, expectedHexHash.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
