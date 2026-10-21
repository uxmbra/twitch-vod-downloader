using System.Globalization;
using TwitchVodDownloader.Cli;
using TwitchVodDownloader.Core.Downloads;
using TwitchVodDownloader.Core.Hls;
using TwitchVodDownloader.Core.Media;
using TwitchVodDownloader.Core.Twitch;

const string Usage = """
    tvd: download Twitch VODs, highlights and clips at the best available quality.

    Usage:
      tvd info <link>
      tvd download <link> [<link> ...] [options]
      tvd download --batch links.txt [options]

    Options:
      -o, --output <path>     Output file (one link) or folder (several). Default: current folder.
      -q, --quality <q>       best (default), 1080p60, 720p, audio ... Falls back to the next best.
      -s, --start <time>      Start of the range to keep, e.g. 1:02:03 or 1h2m3s.
      -e, --end <time>        End of the range to keep.
      -b, --batch <file>      Text file with one link per line (# for comments).
      -t, --threads <n>       Parallel segment downloads for VODs (default 8).
          --auth-token <tok>  twitch.tv auth-token cookie, for subscriber-only VODs.
                              Can also be set with the TVD_AUTH_TOKEN environment variable.
          --ffmpeg <path>     Path to ffmpeg if it is not on PATH.
          --h264-only         Skip HEVC/AV1 renditions (for players without those codecs).
          --temp <dir>        Folder for temporary segment files.
    """;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    var options = CliOptions.Parse(args);
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("twitch-vod-downloader/0.1 (+https://github.com/uxmbra/twitch-vod-downloader)");
    var api = new TwitchApiClient(http, options.AuthToken);

    return options.Command switch
    {
        "info" => await InfoAsync(api, options, cts.Token),
        "download" or "dl" => await DownloadAsync(api, http, options, cts.Token),
        _ => PrintUsage(),
    };
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("\nCancelled.");
    return 130;
}
catch (Exception ex) when (ex is CliException or TwitchApiException or FfmpegException or ArgumentException or FormatException or HttpRequestException)
{
    Console.Error.WriteLine($"\nError: {ex.Message}");
    return 1;
}

static int PrintUsage()
{
    Console.WriteLine(Usage);
    return 0;
}

static async Task<int> InfoAsync(TwitchApiClient api, CliOptions options, CancellationToken ct)
{
    if (options.Links is not [var link])
    {
        throw new CliException("info takes exactly one link.");
    }

    var content = TwitchUrlParser.Parse(link);
    if (content.Kind == ContentKind.Clip)
    {
        var clip = await api.GetClipAsync(content.Id, ct);
        Console.WriteLine($"Clip:     {clip.Title}");
        Console.WriteLine($"Channel:  {clip.ChannelName}");
        Console.WriteLine($"Created:  {clip.CreatedAt:yyyy-MM-dd HH:mm} UTC");
        Console.WriteLine($"Length:   {clip.Length:hh\\:mm\\:ss}");
        Console.WriteLine("Qualities:");
        foreach (var q in clip.Qualities.OrderByDescending(q => q.Height).ThenByDescending(q => q.FrameRate))
        {
            Console.WriteLine($"  {q.Height}p{(q.FrameRate > 31 ? Math.Round(q.FrameRate).ToString(CultureInfo.InvariantCulture) : "")}");
        }

        return 0;
    }

    var video = await api.GetVideoAsync(content.Id, ct);
    Console.WriteLine($"Video:    {video.Title}");
    Console.WriteLine($"Type:     {video.BroadcastType.ToLowerInvariant()}");
    Console.WriteLine($"Channel:  {video.ChannelName}");
    Console.WriteLine($"Game:     {video.Game ?? "-"}");
    Console.WriteLine($"Created:  {video.CreatedAt:yyyy-MM-dd HH:mm} UTC");
    Console.WriteLine($"Length:   {video.Length:hh\\:mm\\:ss}");
    Console.WriteLine("Qualities (best first):");
    foreach (var v in QualitySelector.Rank(await api.GetVideoVariantsAsync(content.Id, !options.H264Only, ct)))
    {
        Console.WriteLine($"  {v}");
    }

    return 0;
}

static async Task<int> DownloadAsync(TwitchApiClient api, HttpClient http, CliOptions options, CancellationToken ct)
{
    if (options.Links.Count == 0)
    {
        throw new CliException("Give at least one link, or --batch <file>.");
    }

    var contents = options.Links.Select(TwitchUrlParser.Parse).ToList();
    var ffmpeg = Ffmpeg.Locate(options.FfmpegPath);
    var vods = new VodDownloader(api, http, ffmpeg);
    var clips = new ClipDownloader(api, http, ffmpeg);

    // With several links, --output is a folder; with one it may be a file or a folder.
    var outputIsFolder = contents.Count > 1
        || options.Output is null
        || Directory.Exists(options.Output)
        || options.Output.EndsWith(Path.DirectorySeparatorChar)
        || options.Output.EndsWith(Path.AltDirectorySeparatorChar);
    var folder = outputIsFolder ? options.Output ?? Directory.GetCurrentDirectory() : null;

    var failures = 0;
    for (var i = 0; i < contents.Count; i++)
    {
        var content = contents[i];
        var prefix = contents.Count > 1 ? $"[{i + 1}/{contents.Count}] " : "";
        try
        {
            DownloadResult result;
            if (content.Kind == ContentKind.Clip)
            {
                var info = await api.GetClipAsync(content.Id, ct);
                var path = folder is null ? options.Output! : Path.Combine(folder, OutputNames.For(info));
                Console.WriteLine($"{prefix}Clip: {info.ChannelName} - {info.Title}");
                result = await clips.DownloadAsync(
                    new ClipDownloadRequest(content.Id, path)
                    {
                        Quality = options.Quality,
                        Start = options.Start,
                        End = options.End,
                        TempDirectory = options.TempDirectory,
                    },
                    new ConsoleProgress(),
                    ct);
            }
            else
            {
                var info = await api.GetVideoAsync(content.Id, ct);
                var path = folder is null ? options.Output! : Path.Combine(folder, OutputNames.For(info));
                Console.WriteLine($"{prefix}{info.BroadcastType.ToLowerInvariant()}: {info.ChannelName} - {info.Title} ({info.Length:hh\\:mm\\:ss})");
                result = await vods.DownloadAsync(
                    new VodDownloadRequest(content.Id, path)
                    {
                        Quality = options.Quality,
                        Start = options.Start,
                        End = options.End,
                        MaxParallelDownloads = options.Threads,
                        IncludeAdvancedCodecs = !options.H264Only,
                        TempDirectory = options.TempDirectory,
                    },
                    new ConsoleProgress(),
                    ct);
            }

            Console.WriteLine($"\n{prefix}Saved {result.Quality}, {FormatBytes(result.Bytes)}: {result.OutputPath}");
        }
        catch (Exception ex) when (contents.Count > 1 && ex is TwitchApiException or FfmpegException or HttpRequestException or ArgumentException)
        {
            // In a batch, one bad link should not stop the rest.
            failures++;
            Console.Error.WriteLine($"\n{prefix}Failed: {ex.Message}");
        }
    }

    if (contents.Count > 1)
    {
        Console.WriteLine($"\nFinished: {contents.Count - failures} succeeded, {failures} failed.");
    }

    return failures == 0 ? 0 : 1;
}

static string FormatBytes(long bytes) => bytes switch
{
    >= 1L << 30 => $"{bytes / (double)(1L << 30):0.00} GB",
    >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
    _ => $"{bytes / 1024.0:0} KB",
};

/// <summary>Draws a single updating progress line. Reports arrive from many threads.</summary>
internal sealed class ConsoleProgress : IProgress<DownloadProgress>
{
    private readonly object _lock = new();
    private DateTime _lastDraw = DateTime.MinValue;

    public void Report(DownloadProgress value)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var important = value.Stage != DownloadStage.Downloading || value.PartsDone == value.PartsTotal;
            if (!important && now - _lastDraw < TimeSpan.FromMilliseconds(200))
            {
                return;
            }

            _lastDraw = now;
            var percent = value.Fraction is { } f ? $"{f * 100,5:0.0}%" : "";
            var parts = value.PartsTotal > 1 ? $" {value.PartsDone}/{value.PartsTotal} segments" : "";
            var mb = $" {value.BytesDownloaded / (1024.0 * 1024.0):0.0} MB";
            var line = value.Stage switch
            {
                DownloadStage.Preparing => "  Preparing...",
                DownloadStage.Downloading => $"  Downloading {percent}{parts}{mb}",
                DownloadStage.Finalizing => "  Finalizing with ffmpeg...",
                _ => "  Done.",
            };
            Console.Write("\r" + line.PadRight(Math.Max(Console.IsOutputRedirected ? 0 : Console.WindowWidth - 1, line.Length)));
        }
    }
}
