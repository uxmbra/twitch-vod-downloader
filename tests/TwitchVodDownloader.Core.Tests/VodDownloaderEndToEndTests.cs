using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using TwitchVodDownloader.Core.Downloads;
using TwitchVodDownloader.Core.Media;
using TwitchVodDownloader.Core.Twitch;

namespace TwitchVodDownloader.Core.Tests;

/// <summary>
/// Runs the whole VOD pipeline against a fake Twitch: real HLS segments made by ffmpeg, served
/// through a stub HTTP handler. Needs ffmpeg on PATH; does nothing without it.
/// </summary>
public sealed class VodDownloaderEndToEndTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("tvd-test-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Downloads_trims_and_handles_muted_segments()
    {
        if (!TryLocateFfmpeg(out var ffmpeg))
        {
            return;
        }

        var hlsDir = Path.Combine(_root, "hls");
        Directory.CreateDirectory(hlsDir);
        await ffmpeg.RunAsync(
        [
            "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", "testsrc=size=320x240:rate=30",
            "-f", "lavfi", "-i", "sine=frequency=440",
            "-t", "30", "-c:v", "libx264", "-g", "30", "-c:a", "aac",
            "-f", "hls", "-hls_time", "10", "-hls_list_size", "0",
            "-hls_segment_filename", Path.Combine(hlsDir, "%d.ts"),
            Path.Combine(hlsDir, "index.m3u8"),
        ]);

        // Pretend segment 1 was muted: Twitch lists it as "-unmuted", which 403s.
        File.Move(Path.Combine(hlsDir, "1.ts"), Path.Combine(hlsDir, "1-muted.ts"));
        var media = await File.ReadAllTextAsync(Path.Combine(hlsDir, "index.m3u8"));
        await File.WriteAllTextAsync(Path.Combine(hlsDir, "index.m3u8"), media.Replace("\n1.ts", "\n1-unmuted.ts", StringComparison.Ordinal));

        using var http = new HttpClient(new FakeTwitch(hlsDir));
        var downloader = new VodDownloader(new TwitchApiClient(http), http, ffmpeg);
        var output = Path.Combine(_root, "out", "vod.mp4");
        var reports = new List<DownloadProgress>();

        var result = await downloader.DownloadAsync(
            new VodDownloadRequest("123", output) { Start = TimeSpan.FromSeconds(5), End = TimeSpan.FromSeconds(20), TempDirectory = _root },
            new SyncProgress(reports));

        Assert.Equal("1080p60 (source)", result.Quality);
        Assert.True(File.Exists(output));
        Assert.Equal(DownloadStage.Done, reports[^1].Stage);
        Assert.Contains(reports, r => r.Stage == DownloadStage.Downloading && r.PartsDone == 2 && r.PartsTotal == 2);

        var duration = await ProbeDurationAsync(ffmpeg, output);
        Assert.InRange(duration, 14.0, 16.5);
        Assert.Empty(Directory.GetDirectories(_root, "tvd-123-*"));
    }

    private static bool TryLocateFfmpeg(out Ffmpeg ffmpeg)
    {
        try
        {
            ffmpeg = Ffmpeg.Locate();
            return true;
        }
        catch (FfmpegException)
        {
            ffmpeg = null!;
            return false;
        }
    }

    private static async Task<double> ProbeDurationAsync(Ffmpeg ffmpeg, string file)
    {
        var probe = Path.Combine(Path.GetDirectoryName(ffmpeg.ExecutablePath)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        var info = new ProcessStartInfo(probe) { RedirectStandardOutput = true };
        foreach (var arg in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", file })
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        var text = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return double.Parse(text.Trim(), CultureInfo.InvariantCulture);
    }

    private sealed class SyncProgress(List<DownloadProgress> reports) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value)
        {
            lock (reports)
            {
                reports.Add(value);
            }
        }
    }

    private sealed class FakeTwitch(string hlsDir) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            if (uri.Host == "gql.twitch.tv")
            {
                var body = await request.Content!.ReadAsStringAsync(ct);
                Assert.Contains("videoPlaybackAccessToken", body, StringComparison.Ordinal);
                Assert.Equal(TwitchApiClient.WebClientId, request.Headers.GetValues("Client-ID").Single());
                return Text("""{"data":{"videoPlaybackAccessToken":{"value":"{\"vod_id\":123}","signature":"sig"}}}""");
            }

            if (uri.Host == "usher.ttvnw.net")
            {
                Assert.Contains("sig=sig", uri.Query, StringComparison.Ordinal);
                Assert.Contains("allow_source=true", uri.Query, StringComparison.Ordinal);
                return Text(M3u8ParserTests.Master.Replace(
                    "https://d1m7jfoe9zdc1j.cloudfront.net/abc_channel_123/chunked/index-dvr.m3u8",
                    "https://cdn.test/chunked/index.m3u8",
                    StringComparison.Ordinal));
            }

            if (uri.Host == "cdn.test")
            {
                var file = Path.Combine(hlsDir, Path.GetFileName(uri.AbsolutePath));
                if (!File.Exists(file))
                {
                    return new HttpResponseMessage(HttpStatusCode.Forbidden);
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(await File.ReadAllBytesAsync(file, ct)) };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Text(string text) =>
            new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }
}
