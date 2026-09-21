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

            var clean = rawFileName.Trim();

            // Strip URL-encoded sequences (%2e%2e, %2f, %5c)
            try
            {
                clean = Uri.UnescapeDataString(clean);
            }
            catch { }

            // Strip leading drive letter or rooted directory structure if supplied
            try
            {
                clean = Path.GetFileName(clean);
            }
            catch { }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", clean.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();

            // Prevent path traversal sequences (../, ..\, etc.)
            sanitized = sanitized.Replace("..", "").Replace("/", "").Replace("\\", "").Trim();

            // Strip hidden dotfile prefixes (.bashrc, .env)
            sanitized = sanitized.TrimStart('.');

            // Ensure filename respects reasonable Windows path bounds
            if (sanitized.Length > 180)
            {
                var ext = Path.GetExtension(sanitized);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(sanitized);
                int maxNameLen = Math.Max(10, 180 - ext.Length);
                sanitized = (nameWithoutExt.Length > maxNameLen ? nameWithoutExt.Substring(0, maxNameLen) : nameWithoutExt) + ext;
            }

            // Guard against legacy Windows reserved DOS device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9)
            var baseWithoutExt = Path.GetFileNameWithoutExtension(sanitized);
            var reservedNames = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
            foreach (var reserved in reservedNames)
            {
                if (string.Equals(baseWithoutExt, reserved, StringComparison.OrdinalIgnoreCase))
                {
                    sanitized = "_" + sanitized;
                    break;
                }
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

        /// <summary>
        /// Applies the Windows NTFS Mark-of-the-Web (Zone.Identifier) alternate data stream to downloaded files.
        /// ZoneId=3 marks the file as originating from the Internet, prompting Windows Defender / SmartScreen
        /// security scrutiny and preventing silent execution of malicious payloads.
        /// </summary>
        public static bool ApplyMarkOfTheWeb(string filePath, string? sourceUrl = null, string? referrerUrl = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;

            try
            {
                if (!OperatingSystem.IsWindows()) return false;

                var zoneStreamPath = filePath + ":Zone.Identifier";
                var sb = new StringBuilder();
                sb.AppendLine("[ZoneTransfer]");
                sb.AppendLine("ZoneId=3"); // 3 = URLZONE_INTERNET
                if (!string.IsNullOrWhiteSpace(referrerUrl) && Uri.TryCreate(referrerUrl, UriKind.Absolute, out _))
                {
                    sb.AppendLine($"ReferrerUrl={referrerUrl}");
                }
                if (!string.IsNullOrWhiteSpace(sourceUrl) && Uri.TryCreate(sourceUrl, UriKind.Absolute, out _))
                {
                    sb.AppendLine($"HostUrl={sourceUrl}");
                }

                File.WriteAllText(zoneStreamPath, sb.ToString(), Encoding.ASCII);
                return true;
            }
            catch
            {
                // Alternate data streams may not be supported on non-NTFS volumes (FAT32, exFAT, network shares)
                return false;
            }
        }

        /// <summary>
        /// Provides an isolated temporary staging directory for segmented download chunk assembly,
        /// avoiding collision with user download directories and preventing partial downloads from being exposed.
        /// </summary>
        public static string GetIsolatedTempDownloadDirectory()
        {
            var appData = Services.ConfigurationService.AppDataFolder;
            var tempDir = Path.Combine(appData, "IsolatedTempDownloads");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }
            return tempDir;
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
