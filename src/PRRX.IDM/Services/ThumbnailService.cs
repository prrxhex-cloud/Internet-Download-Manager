using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using PRRX.IDM.Models;
using PRRX.IDM.Security;

namespace PRRX.IDM.Services
{
    public interface IThumbnailService
    {
        string ExtractYouTubeVideoId(string url);
        List<ThumbnailItem> GetThumbnailResolutions(string videoId);
        Task<BitmapImage?> LoadThumbnailImageAsync(string imageUrl);
        Task<string> SaveThumbnailAsync(string imageUrl, string videoId, string resolutionTitle, string targetFolder);
        void CopyThumbnailToClipboard(BitmapImage bitmap);
    }

    public class ThumbnailService : IThumbnailService
    {
        private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 32,
            EnableMultipleHttp2Connections = true
        }) { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly Regex YouTubeRegex = new(
            @"(?:youtube\.com\/(?:[^\/]+\/.+\/|(?:v|e(?:mbed)?)\/|.*[?&]v=)|youtu\.be\/|youtube\.com\/shorts\/|youtube\.com\/live\/)([^""&?\/ ]{11})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public string ExtractYouTubeVideoId(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            
            var match = YouTubeRegex.Match(url);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        public List<ThumbnailItem> GetThumbnailResolutions(string videoId)
        {
            if (string.IsNullOrWhiteSpace(videoId)) return new List<ThumbnailItem>();

            return new List<ThumbnailItem>
            {
                new()
                {
                    VideoId = videoId,
                    ResolutionTitle = "Maximum Resolution (1080p)",
                    Dimensions = "1920 x 1080",
                    EstimatedSizeFormatted = "~ 380 KB",
                    QualityBadge = "1080p Ultra HD",
                    Url = $"https://img.youtube.com/vi/{videoId}/maxresdefault.jpg"
                },
                new()
                {
                    VideoId = videoId,
                    ResolutionTitle = "High Definition (720p)",
                    Dimensions = "1280 x 720",
                    EstimatedSizeFormatted = "~ 190 KB",
                    QualityBadge = "720p HD",
                    Url = $"https://img.youtube.com/vi/{videoId}/sddefault.jpg"
                },
                new()
                {
                    VideoId = videoId,
                    ResolutionTitle = "Standard Quality (480p)",
                    Dimensions = "640 x 480",
                    EstimatedSizeFormatted = "~ 85 KB",
                    QualityBadge = "480p SD",
                    Url = $"https://img.youtube.com/vi/{videoId}/hqdefault.jpg"
                },
                new()
                {
                    VideoId = videoId,
                    ResolutionTitle = "Compact Mobile (360p)",
                    Dimensions = "480 x 360",
                    EstimatedSizeFormatted = "~ 45 KB",
                    QualityBadge = "360p Compact",
                    Url = $"https://img.youtube.com/vi/{videoId}/mqdefault.jpg"
                }
            };
        }

        public async Task<BitmapImage?> LoadThumbnailImageAsync(string imageUrl)
        {
            try
            {
                var bytes = await HttpClient.GetByteArrayAsync(imageUrl);
                using var stream = new MemoryStream(bytes);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 420; // 90% RAM reduction: decodes only required UI resolution
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public async Task<string> SaveThumbnailAsync(string imageUrl, string videoId, string resolutionTitle, string targetFolder)
        {
            Directory.CreateDirectory(targetFolder);
            var safeResName = SecurityGuard.SanitizeFileName(resolutionTitle);
            var fileName = $"YouTube_Thumbnail_{videoId}_{safeResName}.jpg";
            var destinationPath = SecurityGuard.EnsureSafePath(targetFolder, fileName);

            var bytes = await HttpClient.GetByteArrayAsync(imageUrl);
            await File.WriteAllBytesAsync(destinationPath, bytes);

            return destinationPath;
        }

        public void CopyThumbnailToClipboard(BitmapImage bitmap)
        {
            if (bitmap != null)
            {
                Clipboard.SetImage(bitmap);
            }
        }
    }
}
