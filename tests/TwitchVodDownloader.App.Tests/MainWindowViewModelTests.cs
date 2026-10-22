using System.Net;
using TwitchVodDownloader.App.Services;
using TwitchVodDownloader.App.ViewModels;

namespace TwitchVodDownloader.App.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tvd-app-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private MainWindowViewModel Create() =>
        new(new SettingsStore(_dir), new AppSettings { OutputFolder = _dir }, new HttpClient(new OfflineHandler()));

    [Theory]
    [InlineData("abc", "", "Start time")]
    [InlineData("", "xyz", "End time")]
    [InlineData("1:00", "0:30", "after the start")]
    public void Rejects_bad_times(string start, string end, string expected)
    {
        var vm = Create();
        vm.LinksText = "https://www.twitch.tv/videos/123";
        vm.StartText = start;
        vm.EndText = end;

        vm.AddLinksCommand.Execute(null);

        Assert.Contains(expected, vm.InputError, StringComparison.Ordinal);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public void Requires_video_or_chat()
    {
        var vm = Create();
        vm.LinksText = "https://www.twitch.tv/videos/123";
        vm.IncludeVideo = false;
        vm.IncludeChat = false;

        vm.AddLinksCommand.Execute(null);

        Assert.NotNull(vm.InputError);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public void Adds_valid_links_and_keeps_the_rest_for_fixing()
    {
        var vm = Create();
        vm.LinksText = "https://www.twitch.tv/videos/123\nnot a link\nhttps://clips.twitch.tv/SomeClipSlug\n";

        vm.AddLinksCommand.Execute(null);

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("not a link", vm.LinksText);
        Assert.NotNull(vm.InputError);
    }

    [Fact]
    public void Settings_round_trip()
    {
        var vm = Create();
        vm.SelectedQuality = "720p60";
        vm.IncludeChat = true;
        vm.ConcurrentDownloads = 3;
        vm.AuthToken = "secret";

        var loaded = new SettingsStore(_dir).Load();

        Assert.Equal("720p60", loaded.Quality);
        Assert.True(loaded.IncludeChat);
        Assert.Equal(3, loaded.ConcurrentDownloads);
        Assert.Equal("secret", loaded.AuthToken);
    }

    [Fact]
    public void Unique_path_does_not_overwrite()
    {
        var path = Path.Combine(_dir, "video.mp4");
        File.WriteAllText(path, "");

        Assert.Equal(Path.Combine(_dir, "video (2).mp4"), QueueItemViewModel.UniquePath(path));
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
