namespace TwitchVodDownloader.Core.Twitch;

/// <summary>The kind of Twitch content a link points at.</summary>
public enum ContentKind
{
    /// <summary>A VOD: past broadcast, highlight or upload. All share the same video ID space.</summary>
    Video,

    /// <summary>A clip, identified by its slug.</summary>
    Clip,
}

/// <summary>A parsed reference to a piece of Twitch content.</summary>
public sealed record ContentRef(ContentKind Kind, string Id);
