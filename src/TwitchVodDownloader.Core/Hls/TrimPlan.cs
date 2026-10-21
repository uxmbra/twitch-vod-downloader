namespace TwitchVodDownloader.Core.Hls;

/// <summary>
/// Which segments to fetch for a time range, and how far into the first fetched segment the
/// range really starts. Trimming happens in two stages: whole segments are skipped here so we
/// never download what is not needed, then ffmpeg cuts the remainder precisely.
/// </summary>
public sealed record TrimPlan(IReadOnlyList<HlsSegment> Segments, TimeSpan SeekOffset, TimeSpan? Length)
{
    public bool NeedsCut => SeekOffset > TimeSpan.Zero || Length is not null;

    public static TrimPlan Create(HlsMediaPlaylist playlist, TimeSpan? start, TimeSpan? end)
    {
        var total = playlist.Duration;
        var from = start ?? TimeSpan.Zero;
        var to = end ?? total;

        if (from < TimeSpan.Zero || to < TimeSpan.Zero)
        {
            throw new ArgumentException("Start and end times cannot be negative.");
        }

        if (from >= total)
        {
            throw new ArgumentException($"Start {from:hh\\:mm\\:ss} is past the end of the video ({total:hh\\:mm\\:ss}).");
        }

        if (to <= from)
        {
            throw new ArgumentException("End time must be after start time.");
        }

        to = to > total ? total : to;

        var segments = playlist.Segments.Where(s => s.End > from && s.Start < to).ToList();
        var offset = from - segments[0].Start;
        TimeSpan? length = end is null || to >= total ? null : to - from;

        return new TrimPlan(segments, offset, length);
    }
}
