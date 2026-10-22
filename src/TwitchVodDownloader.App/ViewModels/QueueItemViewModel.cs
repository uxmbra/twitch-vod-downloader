using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwitchVodDownloader.App.Services;
using TwitchVodDownloader.Core.Jobs;
using TwitchVodDownloader.Core.Twitch;

namespace TwitchVodDownloader.App.ViewModels;

public enum QueueState
{
    LookingUp,
    Queued,
    Downloading,
    Done,
    Failed,
    Cancelled,
}

/// <summary>One link in the download queue.</summary>
public sealed partial class QueueItemViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;

    public QueueItemViewModel(string link, ContentRef content, JobOptions options, Action<QueueItemViewModel> remove)
    {
        Link = link;
        Content = content;
        Options = options;
        _remove = remove;
        _title = link;
        _subtitle = content.Kind == ContentKind.Clip ? "Clip" : "Video";
    }

    private readonly Action<QueueItemViewModel> _remove;

    public string Link { get; }

    public ContentRef Content { get; }

    public JobOptions Options { get; }

    public MediaInfo? Media { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(IsFinished), nameof(CanCancel), nameof(IsFailed), nameof(CanRetry))]
    private QueueState _state = QueueState.LookingUp;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _subtitle;

    [ObservableProperty]
    private string _statusText = "Looking up…";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReveal))]
    private string? _outputPath;

    public bool IsActive => State is QueueState.LookingUp or QueueState.Downloading;

    public bool IsFinished => State is QueueState.Done or QueueState.Failed or QueueState.Cancelled;

    public bool IsFailed => State == QueueState.Failed;

    public bool CanCancel => State is QueueState.LookingUp or QueueState.Queued or QueueState.Downloading;

    public bool CanRetry => State is QueueState.Failed or QueueState.Cancelled && Media is not null;

    public bool CanReveal => OutputPath is not null && State == QueueState.Done;

    public async Task ResolveAsync(DownloadService service)
    {
        _cts = new CancellationTokenSource();
        try
        {
            Media = await service.ResolveAsync(Content, _cts.Token);
            Title = Media.Title;
            Subtitle = BuildSubtitle(Media, Options);
            SetQueued();
        }
        catch (OperationCanceledException)
        {
            Finish(QueueState.Cancelled, "Cancelled");
        }
        catch (Exception ex)
        {
            Finish(QueueState.Failed, ex.Message);
        }
    }

    public async Task RunAsync(DownloadService service, string outputFolder)
    {
        if (Media is null)
        {
            Finish(QueueState.Failed, "Could not look up this link.");
            return;
        }

        _cts = new CancellationTokenSource();
        State = QueueState.Downloading;
        IsIndeterminate = true;
        StatusText = "Starting…";

        var path = UniquePath(Path.Combine(outputFolder, Media.DefaultFileName));
        var progress = new Progress<JobProgress>(OnProgress);

        try
        {
            var result = await service.RunAsync(Media, path, Options, progress, _cts.Token);
            OutputPath = result.VideoPath ?? result.ChatPath;
            Progress = 100;
            IsIndeterminate = false;

            var parts = new List<string>();
            if (result.VideoPath is not null)
            {
                parts.Add($"{result.Quality}, {FormatBytes(result.VideoBytes)}");
            }

            if (result.ChatPath is not null)
            {
                parts.Add($"{result.ChatMessages.ToString("N0", CultureInfo.CurrentCulture)} chat messages");
            }

            if (result.Warning is not null)
            {
                parts.Add(result.Warning);
            }

            Finish(QueueState.Done, "Done · " + string.Join(" · ", parts));
        }
        catch (OperationCanceledException)
        {
            Finish(QueueState.Cancelled, "Cancelled");
        }
        catch (Exception ex)
        {
            Finish(QueueState.Failed, ex.Message);
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        if (State == QueueState.Queued)
        {
            Finish(QueueState.Cancelled, "Cancelled");
        }

        _cts?.Cancel();
    }

    [RelayCommand]
    private void Remove()
    {
        _cts?.Cancel();
        _remove(this);
    }

    [RelayCommand]
    private void Reveal()
    {
        if (OutputPath is not null)
        {
            Shell.RevealFile(OutputPath);
        }
    }

    [RelayCommand]
    private void Retry() => SetQueued();

    /// <summary>Raised when the item becomes ready to download, so the queue can pick it up.</summary>
    public event EventHandler? Queued;

    private void SetQueued()
    {
        State = QueueState.Queued;
        StatusText = "Waiting";
        Progress = 0;
        IsIndeterminate = false;
        Queued?.Invoke(this, EventArgs.Empty);
    }

    private void OnProgress(JobProgress p)
    {
        if (State != QueueState.Downloading)
        {
            return;
        }

        IsIndeterminate = p.Fraction is null;
        Progress = (p.Fraction ?? 0) * 100;
        var label = p.Part == JobPart.Chat ? "Chat" : "Video";
        var percent = p.Fraction is { } f ? $" {f * 100:0}%" : "";
        StatusText = $"{label}{percent} · {p.Detail}";
    }

    private void Finish(QueueState state, string text)
    {
        State = state;
        StatusText = text;
        IsIndeterminate = false;
        if (state != QueueState.Done)
        {
            Progress = 0;
        }
    }

    internal static string BuildSubtitle(MediaInfo media, JobOptions options)
    {
        var parts = new List<string> { media.Channel, media.KindLabel, FormatTime(media.Length) };
        if (options.Start is not null || options.End is not null)
        {
            parts.Add($"{FormatTime(options.Start ?? TimeSpan.Zero)}–{FormatTime(options.End ?? media.Length)}");
        }

        parts.Add(options switch
        {
            { IncludeVideo: true, IncludeChat: true } => $"{options.Quality ?? "Best"} + chat",
            { IncludeVideo: true } => options.Quality ?? "Best",
            _ => "Chat only",
        });
        return string.Join(" · ", parts);
    }

    internal static string UniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string FormatTime(TimeSpan t) =>
        ((int)t.TotalHours).ToString(CultureInfo.InvariantCulture) + t.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture);

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.00} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };
}
