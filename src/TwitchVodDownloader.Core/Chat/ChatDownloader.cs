using TwitchVodDownloader.Core.Twitch;

namespace TwitchVodDownloader.Core.Chat;

/// <summary>Downloads a VOD's chat replay, optionally limited to a time range.</summary>
public sealed class ChatDownloader
{
    private readonly ChatClient _client;

    public ChatDownloader(TwitchApiClient api) => _client = new ChatClient(api);

    /// <returns>Comments in video order, restricted to [<paramref name="start"/>, <paramref name="end"/>).</returns>
    public async Task<IReadOnlyList<ChatComment>> DownloadAsync(
        string videoId,
        TimeSpan? start = null,
        TimeSpan? end = null,
        TimeSpan? videoLength = null,
        IProgress<ChatProgress>? progress = null,
        CancellationToken ct = default)
    {
        var from = start ?? TimeSpan.Zero;
        var to = end ?? TimeSpan.MaxValue;
        if (to <= from)
        {
            throw new ArgumentException("End time must be after start time.");
        }

        TimeSpan? total = end is { } e ? e - from : videoLength - from;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var comments = new List<ChatComment>();

        var offset = (int)from.TotalSeconds;
        var page = await _client.GetByOffsetAsync(videoId, offset, ct);
        var useCursor = true;

        while (true)
        {
            var reachedEnd = false;
            foreach (var comment in page.Comments)
            {
                if (comment.Offset >= to)
                {
                    reachedEnd = true;
                    break;
                }

                if (comment.Offset >= from && seen.Add(comment.Id))
                {
                    comments.Add(comment);
                }
            }

            var position = (page.Comments.Count > 0 ? page.Comments[^1].Offset : from) - from;
            progress?.Report(new ChatProgress(comments.Count, position < TimeSpan.Zero ? TimeSpan.Zero : position, total));

            if (reachedEnd || !page.HasNextPage || page.Comments.Count == 0)
            {
                break;
            }

            if (useCursor && page.NextCursor is { } cursor)
            {
                try
                {
                    page = await _client.GetByCursorAsync(videoId, cursor, ct);
                    continue;
                }
                catch (TwitchApiException)
                {
                    // Twitch sometimes refuses cursor paging; offsets always work, just with overlap.
                    useCursor = false;
                }
            }

            var lastOffset = (int)page.Comments[^1].Offset.TotalSeconds;
            offset = lastOffset > offset ? lastOffset : offset + 1;
            page = await _client.GetByOffsetAsync(videoId, offset, ct);
        }

        comments.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        return comments;
    }
}
