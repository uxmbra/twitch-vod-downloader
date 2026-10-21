using TwitchVodDownloader.Core.Util;

namespace TwitchVodDownloader.Core.Tests;

public class TimeParserTests
{
    [Theory]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData("1:02:03", 3723)]
    [InlineData("01:02:03.5", 3723.5)]
    [InlineData("1h2m3s", 3723)]
    [InlineData("1h", 3600)]
    [InlineData("2m30s", 150)]
    [InlineData("45s", 45)]
    public void Parses(string input, double seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), TimeParser.Parse(input));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:75")]
    [InlineData("1:2:3:4")]
    [InlineData("1x")]
    public void Rejects(string input) => Assert.False(TimeParser.TryParse(input, out _));
}
