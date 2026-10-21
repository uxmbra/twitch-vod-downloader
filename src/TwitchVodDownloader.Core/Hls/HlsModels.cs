namespace TwitchVodDownloader.Core.Hls;

/// <summary>One rendition listed in a master playlist, e.g. "1080p60 (source)".</summary>
public sealed record HlsVariant(
    string Name,
    string GroupId,
    Uri Uri,
    long Bandwidth,
    int Width,
    int Height,
    double FrameRate,
    string? Codecs)
{
    /// <summary>Twitch calls the untranscoded original rendition "chunked" and labels it "(source)".</summary>
    public bool IsSource =>
        GroupId.Equals("chunked", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("source", StringComparison.OrdinalIgnoreCase);

    public bool IsAudioOnly => Height == 0 || GroupId.Equals("audio_only", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Name} ({Width}x{Height}, {Bandwidth / 1000} kbps{(Codecs is null ? "" : ", " + Codecs)})";
}

public sealed record HlsSegment(Uri Uri, TimeSpan Start, TimeSpan Duration)
{
    public TimeSpan End => Start + Duration;
}

public sealed record HlsMediaPlaylist(IReadOnlyList<HlsSegment> Segments, Uri? InitializationUri)
{
    public TimeSpan Duration => Segments.Count == 0 ? TimeSpan.Zero : Segments[^1].End;

    /// <summary>fMP4 playlists carry an init segment; MPEG-TS ones do not.</summary>
    public bool IsFragmentedMp4 => InitializationUri is not null;
}
