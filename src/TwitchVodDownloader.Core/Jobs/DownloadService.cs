using TwitchVodDownloader.Core.Chat;
using TwitchVodDownloader.Core.Downloads;
using TwitchVodDownloader.Core.Media;
using TwitchVodDownloader.Core.Twitch;

namespace TwitchVodDownloader.Core.Jobs;

/// <summary>Everything known about a link before downloading it.</summary>
public sealed record MediaInfo(
    ContentRef Ref,
    string Title,
    string Channel,
    string KindLabel,
    TimeSpan Length,
    DateTimeOffset CreatedAt,
    string DefaultFileName,
    VideoInfo? Video,
    ClipInfo? Clip);

public sealed record JobOptions
{
    public string? Quality { get; init; }

    public TimeSpan? Start { get; init; }

    public TimeSpan? End { get; init; }

    public bool IncludeVideo { get; init; } = true;

    public bool IncludeChat { get; init; }

    public int MaxParallelDownloads { get; init; } = 8;

    public bool IncludeAdvancedCodecs { get; init; } = true;

    public string? TempDirectory { get; init; }
}

public enum JobPart
{
    Video,
    Chat,
}

/// <param name="Fraction">0 to 1 for the current part, or null when unknown.</param>
public sealed record JobProgress(JobPart Part, double? Fraction, string Detail);

public sealed record JobResult(string? VideoPath, string? Quality, long VideoBytes, string? ChatPath, int ChatMessages, string? Warning);

/// <summary>
/// One place that turns a link plus options into files on disk: video and/or chat, for VODs,
/// highlights and clips. The CLI and the desktop app both go through here.
/// </summary>
public sealed class DownloadService(TwitchApiClient api, HttpClient http, Ffmpeg? ffmpeg)
{
    private readonly ChatDownloader _chat = new(api);

    public async Task<MediaInfo> ResolveAsync(ContentRef content, CancellationToken ct = default)
    {
        if (content.Kind == ContentKind.Clip)
        {
            var clip = await api.GetClipAsync(content.Id, ct);
            return new MediaInfo(content, clip.Title, clip.ChannelName, "Clip", clip.Length, clip.CreatedAt, OutputNames.For(clip), null, clip);
        }

        var video = await api.GetVideoAsync(content.Id, ct);
        var label = video.BroadcastType.ToUpperInvariant() switch
        {
            "HIGHLIGHT" => "Highlight",
            "UPLOAD" => "Upload",
            _ => "Past broadcast",
        };
        return new MediaInfo(content, video.Title, video.ChannelName, label, video.Length, video.CreatedAt, OutputNames.For(video), video, null);
    }

    /// <param name="outputPath">Path of the video file; chat files are saved beside it.</param>
    public async Task<JobResult> RunAsync(
        MediaInfo media,
        string outputPath,
        JobOptions options,
        IProgress<JobProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!options.IncludeVideo && !options.IncludeChat)
        {
            throw new ArgumentException("Nothing to download: choose video, chat or both.");
        }

        string? videoPath = null, quality = null, chatPath = null, warning = null;
        long bytes = 0;
        var chatCount = 0;

        if (options.IncludeVideo)
        {
            var ff = ffmpeg ?? throw new FfmpegException(
                "ffmpeg was not found. Install it (for example `winget install Gyan.FFmpeg`) or set its path in settings.");
            var videoProgress = progress is null ? null : new Relay<DownloadProgress>(p => progress.Report(ToJobProgress(p)));

            DownloadResult result = media.Clip is not null
                ? await new ClipDownloader(api, http, ff).DownloadAsync(
                    new ClipDownloadRequest(media.Ref.Id, outputPath)
                    {
                        Quality = options.Quality,
                        Start = options.Start,
                        End = options.End,
                        TempDirectory = options.TempDirectory,
                    },
                    videoProgress,
                    ct)
                : await new VodDownloader(api, http, ff).DownloadAsync(
                    new VodDownloadRequest(media.Ref.Id, outputPath)
                    {
                        Quality = options.Quality,
                        Start = options.Start,
                        End = options.End,
                        MaxParallelDownloads = options.MaxParallelDownloads,
                        IncludeAdvancedCodecs = options.IncludeAdvancedCodecs,
                        TempDirectory = options.TempDirectory,
                    },
                    videoProgress,
                    ct);

            videoPath = result.OutputPath;
            quality = result.Quality;
            bytes = result.Bytes;
        }

        if (options.IncludeChat)
        {
            var (videoId, from, to, info) = ChatWindow(media, options);
            if (videoId is null)
            {
                warning = "No chat: the VOD this clip came from is no longer available.";
            }
            else
            {
                var chatProgress = progress is null
                    ? null
                    : new Relay<ChatProgress>(p => progress.Report(new JobProgress(JobPart.Chat, p.Fraction, $"{p.Comments:N0} messages")));
                var comments = await _chat.DownloadAsync(videoId, from, to, info?.Length, chatProgress, ct);

                var basePath = Path.ChangeExtension(outputPath, null);
                var relativeStart = from ?? TimeSpan.Zero;
                await ChatWriter.WriteJsonAsync(basePath + ".chat.json", info, comments, relativeStart, ct);
                await ChatWriter.WriteTextAsync(basePath + ".chat.txt", comments, relativeStart, ct);
                chatPath = basePath + ".chat.json";
                chatCount = comments.Count;
            }
        }

        return new JobResult(videoPath, quality, bytes, chatPath, chatCount, warning);
    }

    /// <summary>A clip's chat is its source VOD's chat over the clip's window.</summary>
    private static (string? VideoId, TimeSpan? From, TimeSpan? To, VideoInfo? Info) ChatWindow(MediaInfo media, JobOptions options)
    {
        if (media.Clip is not { } clip)
        {
            return (media.Ref.Id, options.Start, options.End, media.Video);
        }

        if (clip.SourceVideoId is not { } videoId || clip.SourceVideoOffset is not { } clipStart)
        {
            return (null, null, null, null);
        }

        return (videoId, clipStart + (options.Start ?? TimeSpan.Zero), clipStart + (options.End ?? clip.Length), null);
    }

    private static JobProgress ToJobProgress(DownloadProgress p) => new(
        JobPart.Video,
        p.Stage switch
        {
            DownloadStage.Done => 1,
            DownloadStage.Finalizing => 1,
            DownloadStage.Preparing => 0,
            _ => p.Fraction,
        },
        p.Stage switch
        {
            DownloadStage.Preparing => "Preparing",
            DownloadStage.Downloading when p.PartsTotal > 1 => $"{p.PartsDone}/{p.PartsTotal} segments, {p.BytesDownloaded / (1024.0 * 1024.0):0.0} MB",
            DownloadStage.Downloading => $"{p.BytesDownloaded / (1024.0 * 1024.0):0.0} MB",
            DownloadStage.Finalizing => "Finalizing",
            _ => "Done",
        });

    /// <summary>Forwards synchronously; unlike Progress&lt;T&gt; it does not hop threads.</summary>
    private sealed class Relay<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
