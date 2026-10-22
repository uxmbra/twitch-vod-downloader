namespace TwitchVodDownloader.Core.Chat;

public sealed record ChatBadge(string SetId, string Version);

/// <summary>A run of message text; emotes are fragments that carry an emote ID.</summary>
public sealed record ChatFragment(string Text, string? EmoteId);

public sealed record ChatComment(
    string Id,
    TimeSpan Offset,
    DateTimeOffset CreatedAt,
    string UserId,
    string UserLogin,
    string UserName,
    string? Color,
    IReadOnlyList<ChatBadge> Badges,
    IReadOnlyList<ChatFragment> Fragments)
{
    public string Text => string.Concat(Fragments.Select(f => f.Text));
}

/// <param name="Position">How far into the video the download has reached.</param>
public sealed record ChatProgress(int Comments, TimeSpan Position, TimeSpan? Total)
{
    public double? Fraction => Total is { TotalSeconds: > 0 } t ? Math.Min(1, Position / t) : null;
}

/// <summary>One page of comments from Twitch, with the cursor for the next page.</summary>
internal sealed record ChatPage(IReadOnlyList<ChatComment> Comments, string? NextCursor, bool HasNextPage);
