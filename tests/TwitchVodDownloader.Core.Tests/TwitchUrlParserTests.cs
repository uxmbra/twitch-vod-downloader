using TwitchVodDownloader.Core.Twitch;

namespace TwitchVodDownloader.Core.Tests;

public class TwitchUrlParserTests
{
    [Theory]
    [InlineData("https://www.twitch.tv/videos/2295401234", "2295401234")]
    [InlineData("https://www.twitch.tv/videos/2295401234?t=1h2m3s", "2295401234")]
    [InlineData("twitch.tv/videos/2295401234", "2295401234")]
    [InlineData("https://m.twitch.tv/videos/2295401234", "2295401234")]
    [InlineData("https://www.twitch.tv/somechannel/video/2295401234", "2295401234")]
    [InlineData("2295401234", "2295401234")]
    public void Parses_video_links(string input, string id) =>
        Assert.Equal(new ContentRef(ContentKind.Video, id), TwitchUrlParser.Parse(input));

    [Theory]
    [InlineData("https://clips.twitch.tv/FunnyCleverOtterKappa-AbC_12dEf", "FunnyCleverOtterKappa-AbC_12dEf")]
    [InlineData("https://www.twitch.tv/somechannel/clip/FunnyCleverOtterKappa-AbC?filter=clips", "FunnyCleverOtterKappa-AbC")]
    [InlineData("https://clips.twitch.tv/embed?clip=FunnyCleverOtter&parent=example.com", "FunnyCleverOtter")]
    [InlineData("FunnyCleverOtterKappa-AbC", "FunnyCleverOtterKappa-AbC")]
    public void Parses_clip_links(string input, string slug) =>
        Assert.Equal(new ContentRef(ContentKind.Clip, slug), TwitchUrlParser.Parse(input));

    [Theory]
    [InlineData("")]
    [InlineData("https://youtube.com/watch?v=abc")]
    [InlineData("https://www.twitch.tv/somechannel")]
    [InlineData("https://www.twitch.tv/videos/notanumber")]
    [InlineData("hello world")]
    public void Rejects_other_input(string input) => Assert.False(TwitchUrlParser.TryParse(input, out _));
}
