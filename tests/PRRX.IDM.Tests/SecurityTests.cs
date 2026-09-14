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
using PRRX.IDM.Models;
using PRRX.IDM.Security;
using PRRX.IDM.Services;
using Xunit;

namespace PRRX.IDM.Tests
{
    public class SecurityTests
    {
        [Theory]
        [InlineData("--exec 'rm -rf /'")]
        [InlineData("--config-location /etc/passwd")]
        [InlineData("https://example.com --exec calc.exe")]
        [InlineData("https://example.com --config-location secret")]
        [InlineData("-f best")]
        public void UrlSanitizer_RejectsCommandInjectionFlags(string maliciousUrl)
        {
            bool isValid = SecurityGuard.ValidateUrl(maliciousUrl, out _, out string error);
            Assert.False(isValid);
            Assert.NotEmpty(error);
        }

        [Theory]
        [InlineData("file:///C:/Windows/System32/cmd.exe")]
        [InlineData("ftp://anonymous@ftp.example.com/payload.exe")]
        [InlineData("javascript:alert(1)")]
        [InlineData("gopher://evil.com/1")]
        public void UrlSanitizer_RejectsNonHttpProtocols(string nonHttpUrl)
        {
            bool isValid = SecurityGuard.ValidateUrl(nonHttpUrl, out _, out string error);
            Assert.False(isValid);
            Assert.Contains("Only HTTP and HTTPS protocols are allowed", error);
        }

        [Theory]
        [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&feature=shared&t=10s")]
        [InlineData("https://youtu.be/CKadA20afFI?si=8guLoZK1oVcuEhQI")]
        [InlineData("https://www.tiktok.com/@sayurugg/video/7404946443442146580?is_from_webapp=1&sender_device=pc")]
        [InlineData("https://vimeo.com/76979871")]
        public void UrlSanitizer_AcceptsLegitimateUrlsWithQueryParameters(string validUrl)
        {
            bool isValid = SecurityGuard.ValidateUrl(validUrl, out string sanitizedUrl, out string error);
            Assert.True(isValid, $"Failed for: {validUrl}, Error: {error}");
            Assert.NotEmpty(sanitizedUrl);
            Assert.Empty(error);
        }

        [Fact]
        public void PathGuard_PreventsDirectoryTraversal()
        {
            var baseDir = Path.Combine(Path.GetTempPath(), "PRRX_Safe_Dir");
            Directory.CreateDirectory(baseDir);

            try
            {
                var safePath = SecurityGuard.EnsureSafePath(baseDir, "../../Windows/System32/cmd.exe");
                Assert.StartsWith(baseDir, safePath, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("..", safePath);
            }
            finally
            {
                if (Directory.Exists(baseDir))
                {
                    Directory.Delete(baseDir, true);
                }
            }
        }

        [Fact]
        public void PathGuard_StrictDirectoryBoundary_PreventsPrefixCollisions()
        {
            var baseDir = Path.Combine(Path.GetTempPath(), "PRRX_Safe_Dir");
            var siblingPrefixDir = Path.Combine(Path.GetTempPath(), "PRRX_Safe_Dir_Malicious");

            // A sibling directory starting with the exact same prefix must NOT be considered within baseDir
            bool isSiblingAllowed = SecurityGuard.IsPathWithinDirectory(baseDir, siblingPrefixDir);
            Assert.False(isSiblingAllowed);

            // A file legitimately inside baseDir is allowed
            var validFile = Path.Combine(baseDir, "test.mp4");
            bool isValidAllowed = SecurityGuard.IsPathWithinDirectory(baseDir, validFile);
        }

        [Fact]
        public void PathGuard_SanitizesIllegalCharacters()
        {
            var dirtyName = "My:Awesome*Video?Name<123>|Test.mp4";
            var cleanName = SecurityGuard.SanitizeFileName(dirtyName);

            Assert.DoesNotContain(":", cleanName);
            Assert.DoesNotContain("*", cleanName);
            Assert.DoesNotContain("?", cleanName);
            Assert.DoesNotContain("<", cleanName);
            Assert.DoesNotContain(">", cleanName);
            Assert.DoesNotContain("|", cleanName);
        }

        [Fact]
        public void ThumbnailService_ExtractsYouTubeIdsAccurately()
        {
            var service = new ThumbnailService();

            Assert.Equal("dQw4w9WgXcQ", service.ExtractYouTubeVideoId("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));
            Assert.Equal("dQw4w9WgXcQ", service.ExtractYouTubeVideoId("https://youtu.be/dQw4w9WgXcQ"));
            Assert.Equal("CKadA20afFI", service.ExtractYouTubeVideoId("https://youtu.be/CKadA20afFI?si=8guLoZK1oVcuEhQI"));
            Assert.Equal("dQw4w9WgXcQ", service.ExtractYouTubeVideoId("https://www.youtube.com/shorts/dQw4w9WgXcQ"));
            Assert.Equal("dQw4w9WgXcQ", service.ExtractYouTubeVideoId("https://www.youtube.com/embed/dQw4w9WgXcQ"));
        }

        [Fact]
        public void Sha256_ValidatesCryptographicIntegrity()
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, "PRRX-SECURE-UPDATE-PAYLOAD-TEST");
                
                using var sha = SHA256.Create();
                using var stream = File.OpenRead(tempFile);
                var hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();

                Assert.True(SecurityGuard.VerifySha256(tempFile, hash));
                Assert.False(SecurityGuard.VerifySha256(tempFile, "0000000000000000000000000000000000000000000000000000000000000000"));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void SecurityService_Dpapi_ProtectsAndUnprotectsDataAccurately()
        {
            var sec = new SecurityService();
            var plaintext = "PRRX-CONFIDENTIAL-CREDENTIAL-VAULT-2026";

            var encryptedBase64 = sec.ProtectString(plaintext);
            Assert.NotEmpty(encryptedBase64);
            Assert.NotEqual(plaintext, encryptedBase64);

            var decrypted = sec.UnprotectString(encryptedBase64);
            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void SecurityService_Aes256Gcm_EncryptsAndDecryptsAuthenticatedPayloads()
        {
            var sec = new SecurityService();
            var masterKey = sec.GetOrCreateMasterKey();
            Assert.Equal(32, masterKey.Length);

            var sensitivePayload = "{\"secretApiKey\":\"PRRX_SECRET_TOKEN_9999\",\"account\":\"Enterprise\"}";

            var cipherBase64 = sec.EncryptAesGcmString(sensitivePayload, masterKey);
            Assert.NotEmpty(cipherBase64);
            Assert.NotEqual(sensitivePayload, cipherBase64);

            var decrypted = sec.DecryptAesGcmString(cipherBase64, masterKey);
            Assert.Equal(sensitivePayload, decrypted);

            sec.ZeroMemory(masterKey);
        }

        [Fact]
        public void SecurityService_Aes256Gcm_TamperedCiphertextThrowsException()
        {
            var sec = new SecurityService();
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);

            var rawData = Encoding.UTF8.GetBytes("IntegrityProtectedData");
            var ciphertext = sec.EncryptAesGcm(rawData, key);

            // Tamper with one bit in ciphertext
            ciphertext[^1] ^= 0x01;

            Assert.ThrowsAny<CryptographicException>(() =>
            {
                sec.DecryptAesGcm(ciphertext, key);
            });
        }

        [Fact]
        public void SecurityService_IpcToken_GeneratesAndValidatesConsistently()
        {
            var sec = new SecurityService();
            var token = sec.GetOrCreateIpcToken();

            Assert.NotNull(token);
            Assert.True(token.Length >= 32);

            Assert.True(sec.ValidateIpcToken(token));
            Assert.False(sec.ValidateIpcToken("wrong-token-value-1234567890"));
            Assert.False(sec.ValidateIpcToken(null));
            Assert.False(sec.ValidateIpcToken(string.Empty));
        }

        [Fact]
        public void SecurityService_OriginValidation_PermitsOnlyAuthorizedExtensions()
        {
            var sec = new SecurityService();

            // Authorized Chrome / Edge / Firefox extensions
            Assert.True(sec.ValidateOrigin("chrome-extension://mjcomdjfgmiphnekplhmgdepbhafbjal"));
            Assert.True(sec.ValidateOrigin("chrome-extension://jpnkdblibibkbnllncikdeijkbdnmpem"));
            Assert.True(sec.ValidateOrigin("edge-extension://mjcomdjfgmiphnekplhmgdepbhafbjal"));
            Assert.True(sec.ValidateOrigin("extension://mjcomdjfgmiphnekplhmgdepbhafbjal"));
            Assert.True(sec.ValidateOrigin("moz-extension://a1b2c3d4-e5f6-7890-abcd-ef0123456789"));

            // Prohibited web origins (malicious websites attacking local IPC)
            Assert.False(sec.ValidateOrigin("https://evil-hacker.com"));
            Assert.False(sec.ValidateOrigin("http://localhost:8080"));
            Assert.False(sec.ValidateOrigin("http://127.0.0.1:3000"));
            Assert.False(sec.ValidateOrigin("*"));
            Assert.False(sec.ValidateOrigin("null"));
            Assert.False(sec.ValidateOrigin("file:///C:/test.html"));
            Assert.False(sec.ValidateOrigin(""));
            Assert.False(sec.ValidateOrigin(null));
        }

        [Fact]
        public void ConfigurationService_StoresAndLoadsEncryptedVault_WithoutDataLoss()
        {
            var tempConfigFile = Path.Combine(Path.GetTempPath(), $"prrx_cfg_test_{Guid.NewGuid():N}.json");
            try
            {
                var sec = new SecurityService();
                var configService = new ConfigurationService(sec, tempConfigFile);

                configService.CurrentConfig.TurboConnectionCount = 32;
                configService.CurrentConfig.EnableTurboAcceleration = true;
                configService.CurrentConfig.CookiesFilePath = @"D:\Internet Download Manager\test_cookies.txt";
                configService.SaveConfig();

                // Load into a new service instance
                var reloadedService = new ConfigurationService(sec, tempConfigFile);
                Assert.Equal(32, reloadedService.CurrentConfig.TurboConnectionCount);
                Assert.True(reloadedService.CurrentConfig.EnableTurboAcceleration);
                Assert.Equal(@"D:\Internet Download Manager\test_cookies.txt", reloadedService.CurrentConfig.CookiesFilePath);
            }
            finally
            {
                if (File.Exists(tempConfigFile)) File.Delete(tempConfigFile);
            }
        }

        [Fact]
        public void HistoryService_StoresAndLoadsEncryptedHistory_WithoutDataLoss()
        {
            var sec = new SecurityService();
            var historyService = new HistoryService(sec);

            var testItem = new DownloadItem
            {
                Title = "SecurityAuditTest.mp4",
                TargetFilePath = @"C:\Downloads\SecurityAuditTest.mp4",
                Url = "https://example.com/SecurityAuditTest.mp4",
                FileSizeFormatted = "1.0 MB",
                Status = DownloadStatus.Completed
            };

            historyService.AddItem(testItem);

            var reloadedHistory = new HistoryService(sec);
            Assert.Contains(reloadedHistory.HistoryItems, i => i.Title == "SecurityAuditTest.mp4");

            // Clean up test item
            reloadedHistory.RemoveItem(testItem);
        }

        [Fact]
        public async System.Threading.Tasks.Task UpdateService_DownloadAndVerifyUpdate_EnforcesSha256()
        {
            var updateService = new UpdateService();
            var tempPackage = Path.GetTempFileName();
            var testContent = "PRRX-UPDATE-PACKAGE-V1.2.0-STAGED-BINARY";
            await File.WriteAllTextAsync(tempPackage, testContent);

            string realHash;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(tempPackage))
            {
                realHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }

            var destFile = Path.Combine(Path.GetTempPath(), $"PRRX_Test_Dest_{Guid.NewGuid():N}.zip");
            var destInvalid = Path.Combine(Path.GetTempPath(), $"PRRX_Test_Dest_Inv_{Guid.NewGuid():N}.zip");

            try
            {
                var validManifest = new UpdateManifest
                {
                    Version = "1.2.0",
                    DownloadUrl = new Uri(tempPackage).AbsoluteUri,
                    Sha256Hash = realHash
                };

                bool success = await updateService.DownloadAndVerifyUpdateAsync(validManifest, destFile);
                Assert.True(success);
                Assert.True(File.Exists(destFile));

                // Test with invalid hash
                var invalidManifest = new UpdateManifest
                {
                    Version = "1.2.0",
                    DownloadUrl = new Uri(tempPackage).AbsoluteUri,
                    Sha256Hash = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef"
                };

                bool failSuccess = await updateService.DownloadAndVerifyUpdateAsync(invalidManifest, destInvalid);
                Assert.False(failSuccess);
            }
            finally
            {
                if (File.Exists(tempPackage)) File.Delete(tempPackage);
                if (File.Exists(destFile)) File.Delete(destFile);
                if (File.Exists(destInvalid)) File.Delete(destInvalid);
            }
        }
    }
}
