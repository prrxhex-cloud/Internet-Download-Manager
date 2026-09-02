using System;
using System.IO;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Videos.Streams;

class Program
{
    static async Task Main(string[] args)
    {
        var url = "https://youtu.be/CKadA20afFI";
        var youtube = new YoutubeClient();
        Console.WriteLine($"Probing video: {url} ...");
        
        try
        {
            var video = await youtube.Videos.GetAsync(url);
            Console.WriteLine($"Title: {video.Title}");
            Console.WriteLine($"Author: {video.Author.ChannelTitle}");
            Console.WriteLine($"Duration: {video.Duration}");

            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);
            var muxed = streamManifest.GetMuxedStreams();
            foreach (var s in muxed)
            {
                Console.WriteLine($"Muxed: {s.VideoQuality.Label} ({s.Container.Name}) - {s.Size.MegaBytes:F2} MB");
            }

            var videoOnly = streamManifest.GetVideoOnlyStreams();
            foreach (var s in videoOnly)
            {
                Console.WriteLine($"VideoOnly: {s.VideoQuality.Label} ({s.Container.Name}) - {s.Size.MegaBytes:F2} MB");
            }

            var audioOnly = streamManifest.GetAudioOnlyStreams();
            foreach (var s in audioOnly)
            {
                Console.WriteLine($"AudioOnly: {s.Bitrate.KiloBitsPerSecond:F0} kbps ({s.Container.Name})");
            }

            Console.WriteLine("SUCCESS: Direct Zero-Cookie Stream Manifest Retrieved!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }
    }
}
