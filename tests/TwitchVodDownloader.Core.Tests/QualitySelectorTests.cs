using TwitchVodDownloader.Core.Hls;

namespace TwitchVodDownloader.Core.Tests;

public class QualitySelectorTests
{
    private static readonly IReadOnlyList<HlsVariant> Variants =
        M3u8Parser.ParseMaster(M3u8ParserTests.Master, new Uri("https://usher.ttvnw.net/vod/123.m3u8"));

    [Theory]
    [InlineData(null, "1080p60 (source)")]
    [InlineData("best", "1080p60 (source)")]
    [InlineData("720p60", "720p60")]
    [InlineData("720", "720p60")]
    [InlineData("720p30", "480p")]
    [InlineData("1440p60", "1080p60 (source)")]
    [InlineData("480p", "480p")]
    [InlineData("audio", "Audio Only")]
    [InlineData("144p", "480p")]
    public void Selects(string? preference, string expected) =>
        Assert.Equal(expected, QualitySelector.Select(Variants, preference).Name);

    [Fact]
    public void Prefers_higher_resolution_over_source_flag()
    {
        var uri = new Uri("https://example/x.m3u8");
        var variants = new List<HlsVariant>
        {
            new("1080p60 (source)", "chunked", uri, 8_000_000, 1920, 1080, 60, "avc1"),
            new("1440p60", "1440p60", uri, 9_000_000, 2560, 1440, 60, "hev1"),
        };

        Assert.Equal("1440p60", QualitySelector.Select(variants, null).Name);
    }

    [Fact]
    public void Unknown_quality_text_is_an_error() =>
        Assert.Throws<ArgumentException>(() => QualitySelector.Select(Variants, "ultra"));
}
