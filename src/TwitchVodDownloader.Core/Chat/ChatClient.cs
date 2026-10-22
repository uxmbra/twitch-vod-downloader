using System.Globalization;
using System.Text.Json.Nodes;
using TwitchVodDownloader.Core.Twitch;

namespace TwitchVodDownloader.Core.Chat;

/// <summary>Fetches pages of VOD chat replay from Twitch's GraphQL API.</summary>
internal sealed class ChatClient(TwitchApiClient api)
{
    // The web client ID is challenged for chat replay; this older public client ID is not.
    internal const string ChatClientId = "kd1unb4b3q4t58fwlpcbzcbnm76a8fp";

    private const string CommentsQueryHash = "b70a3591ff0f4e0313d126c6a1502d79a1c02baebb288227c582044aa76adf6a";

    public Task<ChatPage> GetByOffsetAsync(string videoId, int offsetSeconds, CancellationToken ct) =>
        GetAsync(videoId, new JsonObject { ["videoID"] = videoId, ["contentOffsetSeconds"] = offsetSeconds }, ct);

    public Task<ChatPage> GetByCursorAsync(string videoId, string cursor, CancellationToken ct) =>
        GetAsync(videoId, new JsonObject { ["videoID"] = videoId, ["cursor"] = cursor }, ct);

    private async Task<ChatPage> GetAsync(string videoId, JsonObject variables, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["operationName"] = "VideoCommentsByOffsetOrCursor",
            ["variables"] = variables,
            ["extensions"] = new JsonObject
            {
                ["persistedQuery"] = new JsonObject { ["version"] = 1, ["sha256Hash"] = CommentsQueryHash },
            },
        };

        var data = await api.QueryAsync(body, authenticated: false, ct, ChatClientId);
        var comments = data["video"]?["comments"]
            ?? throw new TwitchApiException($"Chat for video {videoId} is unavailable. The video may be deleted or have chat replay disabled.");

        var edges = comments["edges"]?.AsArray() ?? [];
        var parsed = new List<ChatComment>(edges.Count);
        string? lastCursor = null;
        foreach (var edge in edges)
        {
            lastCursor = edge?["cursor"]?.GetValue<string>() ?? lastCursor;
            if (edge?["node"] is JsonObject node)
            {
                parsed.Add(ParseComment(node));
            }
        }

        var hasNext = comments["pageInfo"]?["hasNextPage"]?.GetValue<bool>() ?? false;
        return new ChatPage(parsed, lastCursor, hasNext);
    }

    internal static ChatComment ParseComment(JsonObject node)
    {
        var commenter = node["commenter"];
        var message = node["message"];

        var badges = (message?["userBadges"]?.AsArray() ?? [])
            .Select(b => new ChatBadge(b?["setID"]?.GetValue<string>() ?? "", b?["version"]?.GetValue<string>() ?? ""))
            .Where(b => b.SetId.Length > 0)
            .ToList();

        var fragments = (message?["fragments"]?.AsArray() ?? [])
            .Select(f => new ChatFragment(f?["text"]?.GetValue<string>() ?? "", f?["emote"]?["emoteID"]?.GetValue<string>()))
            .ToList();

        var offset = node["contentOffsetSeconds"]?.GetValue<double>() ?? 0;

        return new ChatComment(
            Id: node["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
            Offset: TimeSpan.FromSeconds(offset),
            CreatedAt: DateTimeOffset.TryParse(node["createdAt"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var created)
                ? created
                : DateTimeOffset.MinValue,
            // A null commenter means the account has since been deleted.
            UserId: commenter?["id"]?.GetValue<string>() ?? "",
            UserLogin: commenter?["login"]?.GetValue<string>() ?? "deleted_user",
            UserName: commenter?["displayName"]?.GetValue<string>() ?? "Deleted User",
            Color: message?["userColor"]?.GetValue<string>(),
            Badges: badges,
            Fragments: fragments);
    }
}
