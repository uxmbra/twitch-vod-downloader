using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwitchVodDownloader.App.Services;
using TwitchVodDownloader.Core.Jobs;
using TwitchVodDownloader.Core.Media;
using TwitchVodDownloader.Core.Twitch;
using TwitchVodDownloader.Core.Util;

namespace TwitchVodDownloader.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly HttpClient _http;
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;

    public MainWindowViewModel(SettingsStore store, AppSettings settings, HttpClient? http = null)
    {
        _http = http ?? CreateHttpClient();
        _store = store;
        _settings = settings;

        _outputFolder = settings.OutputFolder;
        _selectedQuality = Qualities.Contains(settings.Quality) ? settings.Quality : Qualities[0];
        _includeVideo = settings.IncludeVideo;
        _includeChat = settings.IncludeChat;
        _segmentThreads = Math.Clamp(settings.SegmentThreads, 1, 32);
        _concurrentDownloads = Math.Clamp(settings.ConcurrentDownloads, 1, 5);
        _h264Only = settings.H264Only;
        _ffmpegPath = settings.FfmpegPath ?? "";
        _authToken = settings.AuthToken ?? "";
        RefreshFfmpegStatus();
    }

    public string Version { get; } = "v" + (typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

    public IReadOnlyList<string> Qualities { get; } = ["Best", "1080p60", "1080p", "720p60", "720p", "480p", "360p", "Audio only"];

    public ObservableCollection<QueueItemViewModel> Items { get; } = [];

    /// <summary>Set by the view: shows a folder picker and returns the chosen path.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    [ObservableProperty]
    private string _linksText = "";

    [ObservableProperty]
    private string _startText = "";

    [ObservableProperty]
    private string _endText = "";

    [ObservableProperty]
    private string? _inputError;

    [ObservableProperty]
    private string _outputFolder;

    [ObservableProperty]
    private string _selectedQuality;

    [ObservableProperty]
    private bool _includeVideo;

    [ObservableProperty]
    private bool _includeChat;

    [ObservableProperty]
    private int _segmentThreads;

    [ObservableProperty]
    private int _concurrentDownloads;

    [ObservableProperty]
    private bool _h264Only;

    [ObservableProperty]
    private string _ffmpegPath;

    [ObservableProperty]
    private string _authToken;

    [ObservableProperty]
    private string? _ffmpegWarning;

    [ObservableProperty]
    private bool _isSettingsOpen;

    public string QueueSummary
    {
        get
        {
            var active = Items.Count(i => i.State == QueueState.Downloading);
            var waiting = Items.Count(i => i.State is QueueState.Queued or QueueState.LookingUp);
            var done = Items.Count(i => i.State == QueueState.Done);
            return Items.Count == 0 ? "Queue is empty" : $"{active} downloading · {waiting} waiting · {done} done";
        }
    }

    public bool HasItems => Items.Count > 0;

    [RelayCommand]
    private void AddLinks()
    {
        InputError = null;

        TimeSpan? start = null, end = null;
        if (!string.IsNullOrWhiteSpace(StartText))
        {
            if (!TimeParser.TryParse(StartText, out var s))
            {
                InputError = $"Start time '{StartText}' isn't valid. Try 1:02:03 or 1h2m3s.";
                return;
            }

            start = s;
        }

        if (!string.IsNullOrWhiteSpace(EndText))
        {
            if (!TimeParser.TryParse(EndText, out var e))
            {
                InputError = $"End time '{EndText}' isn't valid. Try 1:02:03 or 1h2m3s.";
                return;
            }

            end = e;
        }

        if (start is not null && end is not null && end <= start)
        {
            InputError = "End time must be after the start time.";
            return;
        }

        if (!IncludeVideo && !IncludeChat)
        {
            InputError = "Choose video, chat or both.";
            return;
        }

        var lines = LinksText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            InputError = "Paste one or more Twitch VOD, highlight or clip links first.";
            return;
        }

        var options = new JobOptions
        {
            Quality = SelectedQuality switch
            {
                "Best" => null,
                "Audio only" => "audio",
                var q => q,
            },
            Start = start,
            End = end,
            IncludeVideo = IncludeVideo,
            IncludeChat = IncludeChat,
            MaxParallelDownloads = SegmentThreads,
            IncludeAdvancedCodecs = !H264Only,
        };

        var service = CreateService();
        var rejected = new List<string>();
        foreach (var line in lines)
        {
            if (!TwitchUrlParser.TryParse(line, out var content))
            {
                rejected.Add(line);
                continue;
            }

            var item = new QueueItemViewModel(line, content, options, RemoveItem);
            item.Queued += (_, _) => PumpQueue();
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(QueueItemViewModel.State))
                {
                    OnPropertyChanged(nameof(QueueSummary));
                }
            };
            Items.Add(item);
            _ = item.ResolveAsync(service);
        }

        // Leave only the lines we could not use, so they can be fixed.
        LinksText = string.Join(Environment.NewLine, rejected);
        if (rejected.Count > 0)
        {
            InputError = rejected.Count == 1
                ? "That line isn't a Twitch VOD, highlight or clip link."
                : $"{rejected.Count} lines aren't Twitch VOD, highlight or clip links.";
        }

        OnQueueChanged();
        SaveSettings();
    }

    [RelayCommand]
    private async Task BrowseOutputFolder()
    {
        if (PickFolder is not null && await PickFolder() is { } folder)
        {
            OutputFolder = folder;
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        Directory.CreateDirectory(OutputFolder);
        Shell.OpenFolder(OutputFolder);
    }

    [RelayCommand]
    private void CancelAll()
    {
        foreach (var item in Items.Where(i => i.CanCancel).ToList())
        {
            item.Cancel();
        }
    }

    [RelayCommand]
    private void ClearFinished()
    {
        foreach (var item in Items.Where(i => i.IsFinished).ToList())
        {
            Items.Remove(item);
        }

        OnQueueChanged();
    }

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    public void Shutdown()
    {
        SaveSettings();
        CancelAll();
    }

    /// <summary>Starts queued items until the concurrency limit is reached.</summary>
    private void PumpQueue()
    {
        while (Items.Count(i => i.State == QueueState.Downloading) < ConcurrentDownloads
               && Items.FirstOrDefault(i => i.State == QueueState.Queued) is { } next)
        {
            var service = CreateService();
            var task = next.RunAsync(service, OutputFolder);
            var scheduler = SynchronizationContext.Current is null ? TaskScheduler.Current : TaskScheduler.FromCurrentSynchronizationContext();
            _ = task.ContinueWith(_ => PumpQueue(), scheduler);
        }

        OnPropertyChanged(nameof(QueueSummary));
    }

    private void RemoveItem(QueueItemViewModel item)
    {
        Items.Remove(item);
        OnQueueChanged();
        PumpQueue();
    }

    private void OnQueueChanged()
    {
        OnPropertyChanged(nameof(QueueSummary));
        OnPropertyChanged(nameof(HasItems));
    }

    private DownloadService CreateService()
    {
        Ffmpeg? ffmpeg;
        try
        {
            ffmpeg = Ffmpeg.Locate(string.IsNullOrWhiteSpace(FfmpegPath) ? null : FfmpegPath);
        }
        catch (FfmpegException)
        {
            ffmpeg = null;
        }

        return new DownloadService(new TwitchApiClient(_http, AuthToken), _http, ffmpeg);
    }

    private void RefreshFfmpegStatus()
    {
        try
        {
            Ffmpeg.Locate(string.IsNullOrWhiteSpace(FfmpegPath) ? null : FfmpegPath);
            FfmpegWarning = null;
        }
        catch (FfmpegException ex)
        {
            FfmpegWarning = ex.Message;
        }
    }

    partial void OnFfmpegPathChanged(string value)
    {
        RefreshFfmpegStatus();
        SaveSettings();
    }

    partial void OnConcurrentDownloadsChanged(int value)
    {
        SaveSettings();
        PumpQueue();
    }

    partial void OnOutputFolderChanged(string value) => SaveSettings();

    partial void OnSelectedQualityChanged(string value) => SaveSettings();

    partial void OnIncludeVideoChanged(bool value) => SaveSettings();

    partial void OnIncludeChatChanged(bool value) => SaveSettings();

    partial void OnSegmentThreadsChanged(int value) => SaveSettings();

    partial void OnH264OnlyChanged(bool value) => SaveSettings();

    partial void OnAuthTokenChanged(string value) => SaveSettings();

    private void SaveSettings()
    {
        // Called from property setters, some of which run during construction.
        if (_settings is null)
        {
            return;
        }

        _settings.OutputFolder = OutputFolder;
        _settings.Quality = SelectedQuality;
        _settings.IncludeVideo = IncludeVideo;
        _settings.IncludeChat = IncludeChat;
        _settings.SegmentThreads = SegmentThreads;
        _settings.ConcurrentDownloads = ConcurrentDownloads;
        _settings.H264Only = H264Only;
        _settings.FfmpegPath = string.IsNullOrWhiteSpace(FfmpegPath) ? null : FfmpegPath.Trim();
        _settings.AuthToken = string.IsNullOrWhiteSpace(AuthToken) ? null : AuthToken.Trim();
        _store.Save(_settings);
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("twitch-vod-downloader/0.3 (+https://github.com/uxmbra/twitch-vod-downloader)");
        return http;
    }
}
