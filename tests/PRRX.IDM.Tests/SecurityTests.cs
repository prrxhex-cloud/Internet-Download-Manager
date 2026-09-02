using System;
using System.IO;
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
                
                using var sha = System.Security.Cryptography.SHA256.Create();
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
    }
}
