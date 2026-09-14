// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PRRX.IDM.Models;
using PRRX.IDM.Services;
using PRRX.IDM.ViewModels;
using Xunit;

namespace PRRX.IDM.Tests
{
    public class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            if (ResponseFactory != null)
            {
                return ResponseFactory(request);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }

    public class CloudIntelligenceTests
    {
        [Fact]
        public void ComputeSha256_KnownInput_ComputesAccurateDigest()
        {
            var service = new CloudIntelligenceService();
            // SHA256 of empty string is e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
            var emptyHash = service.ComputeSha256(string.Empty);
            Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", emptyHash);

            // SHA256 of "hello world" is b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9
            var textHash = service.ComputeSha256("hello world");
            Assert.Equal("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9", textHash);
        }

        [Fact]
        public async Task ComputeFileSha256Async_ValidFile_ReturnsExpectedHash()
        {
            var service = new CloudIntelligenceService();
            var tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, "PRRX IDM Test File Content 2026");
                var expected = service.ComputeSha256("PRRX IDM Test File Content 2026");
                var actual = await service.ComputeFileSha256Async(tempFile);
                Assert.Equal(expected, actual);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ComputeFileSha256Async_NonExistentFile_ReturnsEmptyString()
        {
            var service = new CloudIntelligenceService();
            var nonExistent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dat");
            var actual = await service.ComputeFileSha256Async(nonExistent);
            Assert.Empty(actual);
        }

        [Fact]
        public void ExtractOrComputeSha256_ExplicitHexInQuery_ExtractsDirectly()
        {
            var service = new CloudIntelligenceService();
            const string explicitHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
            var url = $"https://example.com/download.zip?sha256={explicitHash}&token=xyz";
            var extracted = service.ExtractOrComputeSha256(url);
            Assert.Equal(explicitHash, extracted);
        }

        [Fact]
        public void ExtractOrComputeSha256_NoHashInQuery_ComputesDeterministicUrlFingerprint()
        {
            var service = new CloudIntelligenceService();
            var url = "https://example.com/file.zip";
            var hash1 = service.ExtractOrComputeSha256(url, "file.zip");
            var hash2 = service.ExtractOrComputeSha256(url, "file.zip");
            Assert.Equal(hash1, hash2);
            Assert.Equal(64, hash1.Length);
        }

        [Fact]
        public void GetLocalLanIp_ReturnsValidIpFormat()
        {
            var service = new CloudIntelligenceService();
            var ip = service.GetLocalLanIp();
            Assert.NotEmpty(ip);
            Assert.True(IPAddress.TryParse(ip, out _));
            Assert.NotEqual("192.168.56.1", ip); // Confirms VirtualBox host-only adapter is filtered out in favor of active gateway adapter
        }

        [Fact]
        public async Task CheckHealthAsync_OnlineServer_ParsesPropertiesCorrectly()
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = req => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"status\":\"online\",\"service\":\"PRRX IDM Gateway\",\"version\":\"1.3.0\",\"edge_node\":\"CMB\",\"client_ip\":\"1.2.3.4\",\"timestamp\":\"2026-09-14T00:00:00Z\"}",
                        Encoding.UTF8, "application/json")
                }
            };
            var httpClient = new HttpClient(handler);
            var service = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var health = await service.CheckHealthAsync();
            Assert.NotNull(health);
            Assert.True(health.IsOnline);
            Assert.Equal("online", health.Status);
            Assert.Equal("PRRX IDM Gateway", health.Service);
            Assert.Equal("1.3.0", health.Version);
            Assert.Equal("CMB", health.EdgeNode);
            Assert.Equal("1.2.3.4", health.ClientIp);
        }

        [Fact]
        public async Task CheckHealthAsync_NetworkFailure_DegradesGracefully()
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = _ => throw new HttpRequestException("Network offline")
            };
            var httpClient = new HttpClient(handler);
            var service = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var health = await service.CheckHealthAsync();
            Assert.NotNull(health);
            Assert.False(health.IsOnline);
            Assert.Equal("offline", health.Status);
        }

        [Fact]
        public async Task GetFileReputationAsync_SafeFile_ParsesReputationAccurately()
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = req =>
                {
                    Assert.Contains("/api/reputation?hash=", req.RequestUri?.ToString());
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "{\"found\":true,\"sha256\":\"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\",\"file_name\":\"release.zip\",\"file_size\":2048,\"safe_votes\":15,\"malware_reports\":0,\"download_count\":42,\"safety_score\":100,\"verdict\":\"safe\"}",
                            Encoding.UTF8, "application/json")
                    };
                }
            };
            var httpClient = new HttpClient(handler);
            var service = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var rep = await service.GetFileReputationAsync("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
            Assert.NotNull(rep);
            Assert.True(rep.Found);
            Assert.True(rep.IsSafe);
            Assert.False(rep.IsSuspicious);
            Assert.False(rep.IsUnrated);
            Assert.Equal(15, rep.SafeVotes);
            Assert.Equal(0, rep.MalwareReports);
            Assert.Equal(100, rep.SafetyScore);
            Assert.Equal("safe", rep.Verdict);
            Assert.Equal(ReputationBadgeStatus.Safe, rep.BadgeStatus);
            Assert.Contains("Community Verified: Safe", rep.BadgeText);
        }

        [Fact]
        public async Task GetFileReputationAsync_SuspiciousFile_IdentifiesThreatBadge()
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = req => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"found\":true,\"sha256\":\"abcdef123456\",\"safe_votes\":1,\"malware_reports\":5,\"safety_score\":15,\"verdict\":\"suspicious\"}",
                        Encoding.UTF8, "application/json")
                }
            };
            var httpClient = new HttpClient(handler);
            var service = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var rep = await service.GetFileReputationAsync("abcdef123456");
            Assert.NotNull(rep);
            Assert.True(rep.IsSuspicious);
            Assert.False(rep.IsSafe);
            Assert.Equal(ReputationBadgeStatus.Suspicious, rep.BadgeStatus);
            Assert.Contains("Suspicious / Malware Reported", rep.BadgeText);
        }

        [Fact]
        public async Task GetFileReputationAsync_UnratedFile_ReturnsUnratedBadge()
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = req => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"found\":false,\"sha256\":\"000000\",\"safety_score\":100,\"verdict\":\"unrated\",\"message\":\"New file\"}",
                        Encoding.UTF8, "application/json")
                }
            };
            var httpClient = new HttpClient(handler);
            var service = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var rep = await service.GetFileReputationAsync("000000");
            Assert.NotNull(rep);
            Assert.True(rep.IsUnrated);
            Assert.False(rep.IsSafe);
            Assert.False(rep.IsSuspicious);
            Assert.Equal(ReputationBadgeStatus.Unrated, rep.BadgeStatus);
            Assert.Equal("🛡️ Unrated File (New)", rep.BadgeText);
        }

        [Fact]
        public async Task GetFileReputationAsync_NetworkTimeout_ReturnsSafeFallbackWithoutThrowing()
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = _ => throw new TaskCanceledException("Operation timed out")
            };
            var httpClient = new HttpClient(handler);
            var service = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var rep = await service.GetFileReputationAsync("test-hash");
            Assert.NotNull(rep);
            Assert.False(rep.Found);
            Assert.True(rep.IsUnrated);
            Assert.Equal("🛡️ Unrated File (New)", rep.BadgeText);
        }

        [Fact]
        public async Task ReportReputationAsync_ValidSafeVote_SendsPayloadAndReturnsTrue()
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = req =>
                {
                    Assert.Equal(HttpMethod.Post, req.Method);
                    Assert.Contains("/api/reputation/report", req.RequestUri?.ToString());
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"success\":true,\"message\":\"Reputation recorded\"}", Encoding.UTF8, "application/json")
                    };
                }
            };
            var httpClient = new HttpClient(handler);
            var service = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var result = await service.ReportReputationAsync("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "archive.zip", 4096, "safe");
            Assert.True(result);
            Assert.NotNull(handler.LastRequestBody);
            Assert.Contains("archive.zip", handler.LastRequestBody);
            Assert.Contains("safe", handler.LastRequestBody);
        }

        [Fact]
        public async Task ReportReputationAsync_ServerError_ReturnsFalseWithoutThrowing()
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            };
            var httpClient = new HttpClient(handler);
            var service = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var result = await service.ReportReputationAsync("hash123", "bad.exe", 1024, "malware");
            Assert.False(result);
        }

        [Fact]
        public async Task GetDomainHealthAsync_ValidDomain_ParsesSpeedAndStatus()
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = req =>
                {
                    Assert.Contains("/api/domain/health?domain=github.com", req.RequestUri?.ToString());
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"domain\":\"github.com\",\"status\":\"online\",\"avg_speed_mbps\":145.8}", Encoding.UTF8, "application/json")
                    };
                }
            };
            var httpClient = new HttpClient(handler);
            var service = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var result = await service.GetDomainHealthAsync("github.com");
            Assert.NotNull(result);
            Assert.Equal("github.com", result.Domain);
            Assert.Equal("online", result.Status);
            Assert.True(result.IsOnline);
            Assert.Equal(145.8, result.AvgSpeedMbps);
            Assert.Equal("⚡ 145.8 Mbps", result.DisplayText);
        }

        [Fact]
        public async Task GetDomainHealthAsync_OfflineFallback_ReturnsUnknownWithoutThrowing()
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = _ => throw new HttpRequestException("DNS resolution failed")
            };
            var httpClient = new HttpClient(handler);
            var service = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var result = await service.GetDomainHealthAsync("invalid.fake.domain");
            Assert.NotNull(result);
            Assert.Equal("unknown", result.Status);
            Assert.False(result.IsOnline);
        }

        [Fact]
        public async Task GetLanPeersAsync_DiscoveredPeers_ParsesPeersList()
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = req => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"matched_network\":\"212.104.224.27\",\"total_peers\":1,\"peers\":[{\"peer_id\":\"peer-desktop\",\"lan_ip\":\"192.168.1.50\",\"port\":6881,\"completed_chunks\":20,\"total_chunks\":20}]}",
                        Encoding.UTF8, "application/json")
                }
            };
            var httpClient = new HttpClient(handler);
            var service = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var result = await service.GetLanPeersAsync("hash123", "192.168.1.10");
            Assert.NotNull(result);
            Assert.Equal("212.104.224.27", result.MatchedNetwork);
            Assert.Equal(1, result.TotalPeers);
            Assert.Single(result.Peers);
            Assert.Equal("peer-desktop", result.Peers[0].PeerId);
            Assert.Equal("192.168.1.50", result.Peers[0].LanIp);
            Assert.Equal(6881, result.Peers[0].Port);
            Assert.Equal(20, result.Peers[0].CompletedChunks);
            Assert.Equal(20, result.Peers[0].TotalChunks);
        }

        [Fact]
        public async Task AnnounceLanPeerAsync_ValidAnnounce_ReturnsTrue()
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = req =>
                {
                    Assert.Equal(HttpMethod.Post, req.Method);
                    Assert.Contains("/api/p2p/announce", req.RequestUri?.ToString());
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"success\":true,\"registered\":true}", Encoding.UTF8, "application/json")
                    };
                }
            };
            var httpClient = new HttpClient(handler);
            var service = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var result = await service.AnnounceLanPeerAsync("my-node", "hash456", "192.168.1.20", 6881, 10, 10);
            Assert.True(result);
            Assert.NotNull(handler.LastRequestBody);
            Assert.Contains("my-node", handler.LastRequestBody);
            Assert.Contains("192.168.1.20", handler.LastRequestBody);
        }

        [Fact]
        public async Task GetMirrorsAsync_ReturnsMirrorsList()
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = req => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"sha256\":\"hash789\",\"mirrors\":[\"https://mirror1.prrx.io/file.zip\",\"https://mirror2.prrx.io/file.zip\"]}",
                        Encoding.UTF8, "application/json")
                }
            };
            var httpClient = new HttpClient(handler);
            var service = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var result = await service.GetMirrorsAsync("hash789");
            Assert.NotNull(result);
            Assert.Equal(2, result.Mirrors.Count);
            Assert.Contains("https://mirror1.prrx.io/file.zip", result.Mirrors);
        }

        [Fact]
        public async Task DownloadFileInfoViewModel_WithCloudIntelligence_InitializesAndUpdatesBadges()
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = req =>
                {
                    var url = req.RequestUri?.ToString() ?? "";
                    if (url.Contains("/api/reputation"))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"found\":true,\"sha256\":\"test\",\"safe_votes\":8,\"malware_reports\":0,\"safety_score\":100,\"verdict\":\"safe\"}", Encoding.UTF8, "application/json")
                        };
                    }
                    if (url.Contains("/api/domain/health"))
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"domain\":\"example.com\",\"status\":\"online\",\"avg_speed_mbps\":85.5}", Encoding.UTF8, "application/json")
                        };
                    }
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };
                }
            };
            var httpClient = new HttpClient(handler);
            var mockCloudService = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var vm = new DownloadFileInfoViewModel(
                "https://example.com/software.exe",
                Path.GetTempPath(),
                "Download Site",
                precalculatedSize: 1048576,
                cloudService: mockCloudService);

            // Wait briefly for non-blocking task to complete
            await vm.LoadCloudIntelligenceAsync("https://example.com/software.exe");

            Assert.Equal("🛡️ Community Verified: Safe (100%)", vm.SecurityBadgeText);
            Assert.Equal("#4ADE80", vm.SecurityBadgeFgColor);
            Assert.Equal(ReputationBadgeStatus.Safe, vm.ReputationStatus);
            Assert.True(vm.HasDomainSpeedInfo);
            Assert.Equal("⚡ 85.5 Mbps", vm.DomainSpeedFormatted);
            Assert.True(vm.CanVote);

            // Cast safe vote
            await vm.VoteReputationAsync("safe");
            Assert.True(vm.HasVoted);
            Assert.False(vm.CanVote);
            Assert.True(vm.HasVoteFeedback);
        }

        [Fact]
        public async Task DownloadFileInfoViewModel_SuspiciousVote_UpdatesBadgeToWarning()
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"success\":true}", Encoding.UTF8, "application/json")
                }
            };
            var httpClient = new HttpClient(handler);
            var mockCloudService = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var vm = new DownloadFileInfoViewModel(
                "https://malicious-site.com/virus.exe",
                Path.GetTempPath(),
                "Shady Site",
                precalculatedSize: 512,
                cloudService: mockCloudService);

            await vm.VoteReputationAsync("malware");
            Assert.True(vm.HasVoted);
            Assert.Equal("⚠️ Suspicious / Malware Reported", vm.SecurityBadgeText);
            Assert.Equal("#F87171", vm.SecurityBadgeFgColor);
            Assert.Equal(ReputationBadgeStatus.Suspicious, vm.ReputationStatus);
            Assert.Equal("⚠ Malware report submitted!", vm.VoteFeedbackText);
            Assert.Equal("#F87171", vm.VoteFeedbackFgColor);
        }

        [Fact]
        public void ExtractOrComputeSha256_HashInPathWithoutQuery_ExtractsCorrectly()
        {
            var service = new CloudIntelligenceService();
            const string explicitHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            var url = $"https://cdn.example.com/builds/{explicitHash}/app.zip";
            var extracted = service.ExtractOrComputeSha256(url);
            Assert.Equal(explicitHash, extracted);
        }

        [Fact]
        public void ExtractOrComputeSha256_IgnoresTimestampedPlaceholderFilenames()
        {
            var service = new CloudIntelligenceService();
            var url = "https://example.com/download?id=99";
            var hash1 = service.ExtractOrComputeSha256(url, "download_20260914_140001.bin");
            var hash2 = service.ExtractOrComputeSha256(url, "download_20260914_140002.bin");
            var hash3 = service.ExtractOrComputeSha256(url, "download.bin");
            var hashBare = service.ExtractOrComputeSha256(url);
            Assert.Equal(hash1, hash2);
            Assert.Equal(hash1, hash3);
            Assert.Equal(hash1, hashBare);
        }

        [Theory]
        [InlineData("https://github.com/path", "github.com")]
        [InlineData("http://api.site.com:8080/v1", "api.site.com")]
        [InlineData("ftp://ftp.is.co.za/file.iso", "ftp.is.co.za")]
        [InlineData("sftp://files.corp.net/data.tar", "files.corp.net")]
        [InlineData("example.com/downloads", "example.com")]
        [InlineData("sub.domain.org:9000", "sub.domain.org")]
        public async Task GetDomainHealthAsync_CleansVariedDomainFormats(string rawDomain, string expectedCleanDomain)
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = req =>
                {
                    Assert.Contains($"domain={expectedCleanDomain}", req.RequestUri?.ToString());
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"domain\":\"" + expectedCleanDomain + "\",\"status\":\"online\",\"avg_speed_mbps\":50.0}", Encoding.UTF8, "application/json")
                    };
                }
            };
            var httpClient = new HttpClient(handler);
            var service = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var res = await service.GetDomainHealthAsync(rawDomain);
            Assert.Equal(expectedCleanDomain, res.Domain);
            Assert.Equal("online", res.Status);
        }

        [Fact]
        public async Task FastExit_WhenHashIsEmpty_DoesNotSendNetworkRequests()
        {
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = _ => throw new InvalidOperationException("Should not be called")
            };
            var httpClient = new HttpClient(handler);
            var service = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var peers = await service.GetLanPeersAsync("");
            Assert.Equal(0, peers.TotalPeers);

            var announce = await service.AnnounceLanPeerAsync("peer", "");
            Assert.False(announce);

            var mirrors = await service.GetMirrorsAsync("");
            Assert.Empty(mirrors.Mirrors);
        }

        [Fact]
        public void FileReputationResult_SafeVerdictWithMinorReport_PreservesSafeBadge()
        {
            var rep = new FileReputationResult
            {
                Found = true,
                Sha256 = "hash123",
                SafeVotes = 100,
                MalwareReports = 1,
                SafetyScore = 95,
                Verdict = "safe"
            };

            Assert.True(rep.IsSafe);
            Assert.False(rep.IsSuspicious);
            Assert.Equal(ReputationBadgeStatus.Safe, rep.BadgeStatus);
            Assert.Contains("Community Verified: Safe (95%)", rep.BadgeText);
        }

        [Fact]
        public void FileReputationResult_NeutralVerdict_RendersNeutralBadge()
        {
            var rep = new FileReputationResult
            {
                Found = true,
                Sha256 = "hash456",
                SafeVotes = 2,
                MalwareReports = 1,
                SafetyScore = 60,
                Verdict = "neutral"
            };

            Assert.False(rep.IsSafe);
            Assert.False(rep.IsSuspicious);
            Assert.Equal(ReputationBadgeStatus.Neutral, rep.BadgeStatus);
            Assert.Contains("Neutral (60%)", rep.BadgeText);
        }

        [Fact]
        public void DownloadFileInfoViewModel_WithInitialFileName_InitializesAccurately()
        {
            var handler = new MockHttpMessageHandler();
            var httpClient = new HttpClient(handler);
            var mockCloudService = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var vm = new DownloadFileInfoViewModel(
                "https://example.com/get?id=123",
                Path.GetTempPath(),
                "Download Site",
                precalculatedSize: 1024,
                initialFileName: "custom_app.zip",
                cloudService: mockCloudService);

            Assert.Equal("custom_app.zip", vm.FileName);
            Assert.False(string.IsNullOrWhiteSpace(vm.CurrentFileHash));
        }

        [Fact]
        public void DownloadFileInfoViewModel_ChangingFileName_UpdatesHash()
        {
            var handler = new MockHttpMessageHandler();
            var httpClient = new HttpClient(handler);
            var mockCloudService = new CloudIntelligenceService("https://mock.prrx.io", httpClient);

            var vm = new DownloadFileInfoViewModel(
                "https://example.com/file",
                Path.GetTempPath(),
                initialFileName: "alpha.zip",
                cloudService: mockCloudService);

            var hash1 = vm.CurrentFileHash;
            vm.FileName = "beta.zip";
            var hash2 = vm.CurrentFileHash;

            Assert.NotEqual(hash1, hash2);
        }

        [Fact]
        public async Task LiveCloudflareWorker_EndToEndHealthCheck_Succeeds()
        {
            var service = new CloudIntelligenceService();
            var health = await service.CheckHealthAsync();
            Assert.NotNull(health);
            // Verify online or graceful offline (network independent)
            if (health.IsOnline)
            {
                Assert.Equal("PRRX IDM Cloud Intelligence Gateway", health.Service);
                Assert.NotEmpty(health.Version);
            }
            else
            {
                Assert.Equal("offline", health.Status);
            }
        }

        [Fact]
        public async Task LiveCloudflareWorker_EndToEndReputationQuery_ReturnsVerdict()
        {
            var service = new CloudIntelligenceService();
            // Test with standard known test sha256
            var rep = await service.GetFileReputationAsync("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
            Assert.NotNull(rep);
            Assert.NotEmpty(rep.Verdict);
            Assert.True(rep.SafetyScore >= 0 && rep.SafetyScore <= 100);
            Assert.NotEmpty(rep.BadgeText);
        }

        [Fact]
        public async Task LiveCloudflareWorker_EndToEndDomainHealth_ReturnsDomainResult()
        {
            var service = new CloudIntelligenceService();
            var domainHealth = await service.GetDomainHealthAsync("github.com");
            Assert.NotNull(domainHealth);
            Assert.Equal("github.com", domainHealth.Domain);
            Assert.NotEmpty(domainHealth.Status);
        }

        [Fact]
        public async Task LiveCloudflareWorker_EndToEndLanPeers_ReturnsPeersResult()
        {
            var service = new CloudIntelligenceService();
            var peers = await service.GetLanPeersAsync("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "192.168.1.55");
            Assert.NotNull(peers);
            Assert.NotNull(peers.Peers);
        }
    }
}
