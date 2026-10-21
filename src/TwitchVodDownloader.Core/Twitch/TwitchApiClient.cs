using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using TwitchVodDownloader.Core.Hls;

namespace TwitchVodDownloader.Core.Twitch;

/// <summary>
/// Talks to Twitch's internal GraphQL API and its "usher" playlist service, the same
/// endpoints the twitch.tv web player uses. These are undocumented and can change.
/// </summary>
public sealed class TwitchApiClient
{
    /// <summary>The public client ID of the twitch.tv web site.</summary>
    public const string WebClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko";

    private static readonly Uri GqlEndpoint = new("https://gql.twitch.tv/gql");

    // Persisted query used by the web player to get a clip's signed download URLs.
    private const string ClipAccessTokenHash = "36b89d2507fce29e5ca551df756d27c1cfe079e2609642b4390aa4c35796eb11";

    private readonly HttpClient _http;
    private readonly string? _oauthToken;

    /// <param name="oauthToken">
    /// Optional twitch.tv "auth-token" cookie value. Only needed for subscriber-only VODs.
    /// </param>
    public TwitchApiClient(HttpClient http, string? oauthToken = null)
    {
        _http = http;
        _oauthToken = string.IsNullOrWhiteSpace(oauthToken) ? null : oauthToken.Trim();
    }

    public async Task<VideoInfo> GetVideoAsync(string videoId, CancellationToken ct = default)
    {
        const string query = """
            query($id: ID!) {
              video(id: $id) {
                id title lengthSeconds broadcastType createdAt
                owner { login displayName }
                game { name }
              }
            }
            """;

        var data = await QueryAsync(new JsonObject { ["query"] = query, ["variables"] = new JsonObject { ["id"] = videoId } }, false, ct);
        var video = data["video"] ?? throw new TwitchApiException($"Video {videoId} was not found. It may be deleted or private.");

        return new VideoInfo(
            Id: video["id"]!.GetValue<string>(),
            Title: video["title"]?.GetValue<string>() ?? "Untitled",
            Length: TimeSpan.FromSeconds(video["lengthSeconds"]?.GetValue<int>() ?? 0),
            BroadcastType: video["broadcastType"]?.GetValue<string>() ?? "ARCHIVE",
            CreatedAt: ParseDate(video["createdAt"]),
            ChannelLogin: video["owner"]?["login"]?.GetValue<string>() ?? "unknown",
            ChannelName: video["owner"]?["displayName"]?.GetValue<string>() ?? "unknown",
            Game: video["game"]?["name"]?.GetValue<string>());
    }

    public async Task<PlaybackAccessToken> GetVideoAccessTokenAsync(string videoId, CancellationToken ct = default)
    {
        const string query = """
            query($id: ID!) {
              videoPlaybackAccessToken(id: $id, params: { platform: "web", playerBackend: "mediaplayer", playerType: "site" }) {
                value signature
              }
            }
            """;

        var data = await QueryAsync(new JsonObject { ["query"] = query, ["variables"] = new JsonObject { ["id"] = videoId } }, true, ct);
        var token = data["videoPlaybackAccessToken"]
            ?? throw new TwitchApiException($"Twitch refused a playback token for video {videoId}.");

        return new PlaybackAccessToken(token["value"]!.GetValue<string>(), token["signature"]!.GetValue<string>());
    }

    /// <summary>Fetches and parses the master playlist that lists every rendition of a VOD.</summary>
    /// <param name="includeAdvancedCodecs">
    /// Ask for HEVC/AV1 renditions too. Channels using Enhanced Broadcasting can offer higher
    /// resolutions (e.g. 1440p) only in those codecs.
    /// </param>
    public async Task<IReadOnlyList<HlsVariant>> GetVideoVariantsAsync(
        string videoId, bool includeAdvancedCodecs = true, CancellationToken ct = default)
    {
        var token = await GetVideoAccessTokenAsync(videoId, ct);

        var query = new Dictionary<string, string>
        {
            ["allow_source"] = "true",
            ["allow_audio_only"] = "true",
            ["playlist_include_framerate"] = "true",
            ["player"] = "twitchweb",
            ["supported_codecs"] = includeAdvancedCodecs ? "av1,h265,h264" : "h264",
            ["sig"] = token.Signature,
            ["token"] = token.Value,
            ["p"] = Random.Shared.Next(1_000_000, 9_999_999).ToString(CultureInfo.InvariantCulture),
        };
        var uri = new Uri($"https://usher.ttvnw.net/vod/{videoId}.m3u8?{ToQueryString(query)}");

        using var response = await _http.GetAsync(uri, ct);
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new TwitchApiException(_oauthToken is null
                ? $"Video {videoId} is restricted (usually subscriber-only). Supply your Twitch auth token to download it."
                : $"Video {videoId} is restricted and your account does not have access to it.");
        }

        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(ct);
        return M3u8Parser.ParseMaster(text, uri);
    }

    public async Task<ClipInfo> GetClipAsync(string slug, CancellationToken ct = default)
    {
        const string metadataQuery = """
            query($slug: ID!) {
              clip(slug: $slug) {
                slug title durationSeconds createdAt videoOffsetSeconds
                broadcaster { login displayName }
                game { name }
                video { id }
              }
            }
            """;

        var metadata = await QueryAsync(
            new JsonObject { ["query"] = metadataQuery, ["variables"] = new JsonObject { ["slug"] = slug } }, false, ct);
        var clip = metadata["clip"] ?? throw new TwitchApiException($"Clip '{slug}' was not found. It may have been deleted.");

        var tokenRequest = new JsonObject
        {
            ["operationName"] = "VideoAccessToken_Clip",
            ["variables"] = new JsonObject { ["slug"] = slug },
            ["extensions"] = new JsonObject
            {
                ["persistedQuery"] = new JsonObject { ["version"] = 1, ["sha256Hash"] = ClipAccessTokenHash },
            },
        };
        var tokenData = await QueryAsync(tokenRequest, false, ct);
        var tokenClip = tokenData["clip"] ?? throw new TwitchApiException($"Twitch returned no download URLs for clip '{slug}'.");
        var token = tokenClip["playbackAccessToken"] ?? throw new TwitchApiException($"Twitch refused a playback token for clip '{slug}'.");

        var qualities = new List<ClipQuality>();
        foreach (var q in tokenClip["videoQualities"]?.AsArray() ?? [])
        {
            if (q?["sourceURL"]?.GetValue<string>() is not { Length: > 0 } source)
            {
                continue;
            }

            _ = int.TryParse(q["quality"]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var height);
            var fps = q["frameRate"]?.GetValue<double>() ?? 0;
            qualities.Add(new ClipQuality(height, fps, new Uri(source)));
        }

        if (qualities.Count == 0)
        {
            throw new TwitchApiException($"Clip '{slug}' has no downloadable renditions.");
        }

        return new ClipInfo(
            Slug: clip["slug"]?.GetValue<string>() ?? slug,
            Title: clip["title"]?.GetValue<string>() ?? "Untitled",
            Length: TimeSpan.FromSeconds(clip["durationSeconds"]?.GetValue<double>() ?? 0),
            CreatedAt: ParseDate(clip["createdAt"]),
            ChannelLogin: clip["broadcaster"]?["login"]?.GetValue<string>() ?? "unknown",
            ChannelName: clip["broadcaster"]?["displayName"]?.GetValue<string>() ?? "unknown",
            Game: clip["game"]?["name"]?.GetValue<string>(),
            Qualities: qualities,
            AccessToken: new PlaybackAccessToken(token["value"]!.GetValue<string>(), token["signature"]!.GetValue<string>()))
        {
            SourceVideoId = clip["video"]?["id"]?.GetValue<string>(),
            SourceVideoOffset = clip["videoOffsetSeconds"] is JsonValue offset ? TimeSpan.FromSeconds(offset.GetValue<double>()) : null,
        };
    }

    /// <summary>The clip file URL only works with the signed token appended.</summary>
    public static Uri SignClipUri(ClipQuality quality, PlaybackAccessToken token) =>
        new($"{quality.SourceUri}?sig={Uri.EscapeDataString(token.Signature)}&token={Uri.EscapeDataString(token.Value)}");

    internal async Task<JsonNode> QueryAsync(JsonObject body, bool authenticated, CancellationToken ct, string clientId = WebClientId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GqlEndpoint) { Content = JsonContent.Create(body) };
        request.Headers.Add("Client-ID", clientId);
        if (authenticated && _oauthToken is not null)
        {
            request.Headers.Add("Authorization", $"OAuth {_oauthToken}");
        }

        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new TwitchApiException("Twitch rejected the auth token. It may have expired; grab a fresh one.");
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonNode>(ct)
            ?? throw new TwitchApiException("Twitch returned an empty response.");

        if (json["errors"] is JsonArray { Count: > 0 } errors)
        {
            var messages = string.Join("; ", errors.Select(e => e?["message"]?.GetValue<string>() ?? "unknown error"));
            throw new TwitchApiException($"Twitch API error: {messages}");
        }

        return json["data"] ?? throw new TwitchApiException("Twitch returned no data.");
    }

    private static DateTimeOffset ParseDate(JsonNode? node) =>
        DateTimeOffset.TryParse(node?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : DateTimeOffset.MinValue;

    private static string ToQueryString(Dictionary<string, string> values) =>
        string.Join('&', values.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
}
