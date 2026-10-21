namespace TwitchVodDownloader.Core.Twitch;

public sealed record VideoInfo(
    string Id,
    string Title,
    TimeSpan Length,
    string BroadcastType,
    DateTimeOffset CreatedAt,
    string ChannelLogin,
    string ChannelName,
    string? Game);

public sealed record ClipQuality(int Height, double FrameRate, Uri SourceUri);

public sealed record ClipInfo(
    string Slug,
    string Title,
    TimeSpan Length,
    DateTimeOffset CreatedAt,
    string ChannelLogin,
    string ChannelName,
    string? Game,
    IReadOnlyList<ClipQuality> Qualities,
    PlaybackAccessToken AccessToken);

/// <summary>A signed token that authorises fetching a playlist or clip file.</summary>
public sealed record PlaybackAccessToken(string Value, string Signature);

public class TwitchApiException(string message, Exception? inner = null) : Exception(message, inner);
