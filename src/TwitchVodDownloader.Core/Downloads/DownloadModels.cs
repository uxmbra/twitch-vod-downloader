namespace TwitchVodDownloader.Core.Downloads;

public enum DownloadStage
{
    Preparing,
    Downloading,
    Finalizing,
    Done,
}

/// <param name="PartsDone">Segments for a VOD; 0 or 1 for a clip.</param>
/// <param name="BytesTotal">Known for clips; unknown for VODs until every segment is fetched.</param>
public sealed record DownloadProgress(
    DownloadStage Stage,
    int PartsDone,
    int PartsTotal,
    long BytesDownloaded,
    long? BytesTotal = null)
{
    public double? Fraction =>
        BytesTotal is > 0 ? (double)BytesDownloaded / BytesTotal.Value
        : PartsTotal > 0 ? (double)PartsDone / PartsTotal
        : null;
}

public sealed record VodDownloadRequest(string VideoId, string OutputPath)
{
    /// <summary>See <see cref="Hls.QualitySelector.Select"/>. Null means best available.</summary>
    public string? Quality { get; init; }

    public TimeSpan? Start { get; init; }

    public TimeSpan? End { get; init; }

    public int MaxParallelDownloads { get; init; } = 8;

    /// <summary>Allow HEVC/AV1 renditions, which can be higher resolution than H.264 ones.</summary>
    public bool IncludeAdvancedCodecs { get; init; } = true;

    /// <summary>Where segments are staged. Defaults to the system temp folder.</summary>
    public string? TempDirectory { get; init; }
}

public sealed record ClipDownloadRequest(string Slug, string OutputPath)
{
    /// <summary>"best", or a height such as "720". Null means best available.</summary>
    public string? Quality { get; init; }

    public TimeSpan? Start { get; init; }

    public TimeSpan? End { get; init; }

    public string? TempDirectory { get; init; }
}

public sealed record DownloadResult(string OutputPath, string Quality, long Bytes);
