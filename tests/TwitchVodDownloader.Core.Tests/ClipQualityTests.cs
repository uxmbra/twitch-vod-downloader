using TwitchVodDownloader.Core.Downloads;
using TwitchVodDownloader.Core.Twitch;

namespace TwitchVodDownloader.Core.Tests;

public class ClipQualityTests
{
    private static readonly IReadOnlyList<ClipQuality> Qualities =
    [
        new(360, 30, new Uri("https://clips/360.mp4")),
        new(1080, 60, new Uri("https://clips/1080.mp4")),
        new(720, 60, new Uri("https://clips/720.mp4")),
    ];

    [Theory]
    [InlineData(null, 1080)]
    [InlineData("best", 1080)]
    [InlineData("720p", 720)]
    [InlineData("480", 360)]
    [InlineData("144", 360)]
    public void Selects(string? preference, int height) =>
        Assert.Equal(height, ClipDownloader.SelectQuality(Qualities, preference).Height);

    [Fact]
    public void Signs_clip_urls()
    {
        var uri = TwitchApiClient.SignClipUri(Qualities[1], new PlaybackAccessToken("{\"a\":1}", "abc123"));

        Assert.Equal("https://clips/1080.mp4?sig=abc123&token=%7B%22a%22%3A1%7D", uri.AbsoluteUri);
    }
}
