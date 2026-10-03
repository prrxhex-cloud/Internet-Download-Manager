// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using PRRX.IDM.Models;
using PRRX.IDM.Services;
using PRRX.IDM.ViewModels;

namespace PRRX.IDM.Tests
{
    public class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    public class CloudResolverServiceTests
    {
        [Theory]
        [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", true)]
        [InlineData("https://youtu.be/dQw4w9WgXcQ", true)]
        [InlineData("https://www.tiktok.com/@user/video/1234567890", true)]
        [InlineData("https://www.facebook.com/watch/?v=123456", true)]
        [InlineData("https://fb.watch/abcdef/", true)]
        [InlineData("https://www.instagram.com/reel/C123456/", true)]
        [InlineData("https://www.pinterest.com/pin/123456/", true)]
        [InlineData("https://twitter.com/user/status/123456", true)]
        [InlineData("https://x.com/user/status/123456", true)]
        [InlineData("https://drive.google.com/file/d/12345abc/view", true)]
        [InlineData("https://www.mediafire.com/file/sample/file.zip", true)]
        [InlineData("https://pixeldrain.com/u/abc12345", true)]
        [InlineData("https://usersdrive.com/abcdef.html", true)]
        [InlineData("https://open.spotify.com/track/4cOdK2wGLETKBW3PvgPWqT", true)]
        [InlineData("https://sinhanada.net/download/song123", true)]
        [InlineData("https://slmix.lk/download/track", true)]
        [InlineData("https://paperhub.lk/document", true)]
        [InlineData("https://unknown-random-site-999.org/file.iso", false)]
        public void CanResolve_RecognizesAll22SupportedDomains(string url, bool expectedCanResolve)
        {
            var resolver = new CloudResolverService();
            var canResolve = resolver.CanResolve(url);
            Assert.Equal(expectedCanResolve, canResolve);
        }

        [Fact]
        public async Task ResolveMediaAsync_ParsesSuccessfulResponse()
        {
            var fakeJson = @"
            {
                ""status"": true,
                ""creator"": ""Sasa Dev"",
                ""result"": {
                    ""title"": ""Sample Video Track"",
                    ""download"": ""https://cdn.example.com/video.mp4"",
                    ""thumbnail"": ""https://cdn.example.com/thumb.jpg"",
                    ""size"": ""45.2 MB""
                }
            }";

            var handler = new FakeHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fakeJson, Encoding.UTF8, "application/json")
            });

            var client = new HttpClient(handler);
            var resolver = new CloudResolverService("https://test-worker.local", client);

            var resolved = await resolver.ResolveMediaAsync("https://www.youtube.com/watch?v=sample");

            Assert.NotNull(resolved);
            Assert.True(resolved.Success);
            Assert.Equal("Sample Video Track", resolved.Title);
            Assert.Equal("https://cdn.example.com/video.mp4", resolved.DirectStreamUrl);
            Assert.Equal("https://cdn.example.com/thumb.jpg", resolved.ThumbnailUrl);
            Assert.Equal("45.2 MB", resolved.FormattedSize);
            Assert.Equal("mp4", resolved.Extension);
            Assert.False(resolved.FallbackRequired);
        }

        [Fact]
        public async Task ResolveMediaAsync_HandlesRateLimitGracefullyWithFallback()
        {
            var fakeJson = @"
            {
                ""status"": false,
                ""success"": false,
                ""error"": ""Rate limit exceeded. Please wait 60 seconds."",
                ""rateLimitExceeded"": true,
                ""retryAfter"": 60,
                ""fallbackRequired"": true
            }";

            var handler = new FakeHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(fakeJson, Encoding.UTF8, "application/json")
            });

            var client = new HttpClient(handler);
            var resolver = new CloudResolverService("https://test-worker.local", client);

            var resolved = await resolver.ResolveMediaAsync("https://www.youtube.com/watch?v=sample");

            Assert.NotNull(resolved);
            Assert.False(resolved.Success);
            Assert.True(resolved.FallbackRequired);
        }

        [Fact]
        public async Task ResolveMediaProbeAsync_GeneratesValidMediaFormat()
        {
            var fakeJson = @"
            {
                ""status"": true,
                ""result"": {
                    ""title"": ""Spotify Hit Song"",
                    ""download"": ""https://rr2---sn-playback.googlevideo.com/videoplayback?itag=140&mime=audio%2Fmp4"",
                    ""thumbnail"": ""https://spotifycdn.com/cover.jpg"",
                    ""size"": ""3.4 MB""
                }
            }";

            var handler = new FakeHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fakeJson, Encoding.UTF8, "application/json")
            });

            var client = new HttpClient(handler);
            var resolver = new CloudResolverService("https://test-worker.local", client);

            var probe = await resolver.ResolveMediaProbeAsync("https://open.spotify.com/track/sample");

            Assert.NotNull(probe);
            Assert.Equal("Spotify Hit Song", probe.Title);
            Assert.NotEmpty(probe.Formats);
            var fmt = probe.Formats.First();
            Assert.Equal("cloud_cdn_stream", fmt.FormatId);
            Assert.Contains("googlevideo.com", fmt.DirectDownloadUrl);
        }

        [Fact]
        public async Task SearchApkAsync_ParsesApkItemsProperly()
        {
            var fakeJson = @"
            {
                ""status"": true,
                ""result"": [
                    {
                        ""title"": ""WhatsApp Messenger (Mirror)"",
                        ""image"": ""https://img.aptoide.com/icon.png"",
                        ""size"": ""141.77 MB"",
                        ""version"": ""2.26.39.71"",
                        ""link"": ""https://com.whatsapp.en.aptoide.com/app""
                    },
                    {
                        ""title"": ""WhatsApp Business (Mirror)"",
                        ""image"": ""https://img.aptoide.com/business.png"",
                        ""size"": ""51.02 MB"",
                        ""version"": ""2.23.8.76"",
                        ""link"": ""https://com.whatsapp.w4b.en.aptoide.com/app""
                    }
                ]
            }";

            var handler = new FakeHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(fakeJson, Encoding.UTF8, "application/json")
            });

            var client = new HttpClient(handler);
            var resolver = new CloudResolverService("https://test-worker.local", client);

            var result = await resolver.SearchApkAsync("whatsapp", "ultra");

            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.Equal(2, result.Items.Count);
            Assert.Equal("WhatsApp Messenger (Mirror)", result.Items[0].Title);
            Assert.Equal("141.77 MB", result.Items[0].Size);
            Assert.Equal("2.26.39.71", result.Items[0].Version);
        }

        [Fact]
        public void ZeroSecretsHardcodedInClientCode()
        {
            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "PRRX.InternetDownloadManager.sln")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            Assert.NotNull(dir);
            var srcDir = Path.Combine(dir!, "src", "PRRX.IDM");
            Assert.True(Directory.Exists(srcDir));

            var files = Directory.GetFiles(srcDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".cs") || f.EndsWith(".xaml") || f.EndsWith(".json") || f.EndsWith(".config"));

            var forbiddenSecret = "Sasa_Dev_Api_";

            foreach (var f in files)
            {
                var content = File.ReadAllText(f);
                Assert.False(content.Contains(forbiddenSecret), $"Security Violation: Secret pattern found in client file '{Path.GetFileName(f)}'!");
            }
        }
    }
}
