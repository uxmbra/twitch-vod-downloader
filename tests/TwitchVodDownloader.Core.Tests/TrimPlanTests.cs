using TwitchVodDownloader.Core.Hls;

namespace TwitchVodDownloader.Core.Tests;

public class TrimPlanTests
{
    private static readonly HlsMediaPlaylist Playlist = new(
        Enumerable.Range(0, 10)
            .Select(i => new HlsSegment(new Uri($"https://cdn/{i}.ts"), TimeSpan.FromSeconds(i * 10), TimeSpan.FromSeconds(10)))
            .ToList(),
        null);

    [Fact]
    public void No_range_takes_everything()
    {
        var plan = TrimPlan.Create(Playlist, null, null);

        Assert.Equal(10, plan.Segments.Count);
        Assert.False(plan.NeedsCut);
    }

    [Fact]
    public void Range_skips_whole_segments_and_keeps_the_remainder_offset()
    {
        var plan = TrimPlan.Create(Playlist, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(41));

        Assert.Equal(["https://cdn/2.ts", "https://cdn/3.ts", "https://cdn/4.ts"], plan.Segments.Select(s => s.Uri.ToString()));
        Assert.Equal(TimeSpan.FromSeconds(5), plan.SeekOffset);
        Assert.Equal(TimeSpan.FromSeconds(16), plan.Length);
    }

    [Fact]
    public void End_past_video_is_clamped()
    {
        var plan = TrimPlan.Create(Playlist, TimeSpan.FromSeconds(90), TimeSpan.FromHours(5));

        Assert.Single(plan.Segments);
        Assert.Null(plan.Length);
    }

    [Theory]
    [InlineData(50.0, 40.0)]
    [InlineData(100.0, null)]
    [InlineData(-1.0, null)]
    public void Invalid_ranges_throw(double start, double? end) =>
        Assert.Throws<ArgumentException>(() => TrimPlan.Create(
            Playlist, TimeSpan.FromSeconds(start), end is null ? null : TimeSpan.FromSeconds(end.Value)));
}
