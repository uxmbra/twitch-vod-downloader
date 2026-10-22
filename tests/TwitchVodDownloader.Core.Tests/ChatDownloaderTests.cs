using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using TwitchVodDownloader.Core.Chat;
using TwitchVodDownloader.Core.Twitch;

namespace TwitchVodDownloader.Core.Tests;

public sealed class ChatDownloaderTests
{
    [Fact]
    public async Task Pages_by_cursor_and_trims_to_range()
    {
        var handler = new FakeChat(cursorWorks: true);
        using var http = new HttpClient(handler);

        var comments = await new ChatDownloader(new TwitchApiClient(http))
            .DownloadAsync("123", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(50));

        Assert.Equal([10, 20, 30, 40], comments.Select(c => (int)c.Offset.TotalSeconds));
        Assert.Contains(handler.Requests, r => r.Contains("\"cursor\"", StringComparison.Ordinal));
        Assert.All(handler.ClientIds, id => Assert.Equal(ChatClient.ChatClientId, id));
    }

    [Fact]
    public async Task Falls_back_to_offset_paging_without_duplicates()
    {
        using var http = new HttpClient(new FakeChat(cursorWorks: false));

        var comments = await new ChatDownloader(new TwitchApiClient(http)).DownloadAsync("123");

        Assert.Equal(Enumerable.Range(0, 10).Select(i => i * 10), comments.Select(c => (int)c.Offset.TotalSeconds));
        Assert.Equal(comments.Count, comments.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void Parses_comment_fragments_badges_and_deleted_users()
    {
        var node = JsonNode.Parse("""
            {
              "id": "c1", "contentOffsetSeconds": 61.5, "createdAt": "2026-10-11T09:00:00Z", "commenter": null,
              "message": {
                "userColor": "#FF0000",
                "userBadges": [{ "setID": "subscriber", "version": "12" }],
                "fragments": [{ "text": "hi ", "emote": null }, { "text": "Kappa", "emote": { "emoteID": "25" } }]
              }
            }
            """)!.AsObject();

        var comment = ChatClient.ParseComment(node);

        Assert.Equal("hi Kappa", comment.Text);
        Assert.Equal("25", comment.Fragments[1].EmoteId);
        Assert.Equal(new ChatBadge("subscriber", "12"), comment.Badges.Single());
        Assert.Equal("Deleted User", comment.UserName);
        Assert.Equal(TimeSpan.FromSeconds(61.5), comment.Offset);
    }

    [Fact]
    public async Task Writes_text_relative_to_trim_start()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tvd-chat-{Guid.NewGuid():N}.txt");
        var comment = new ChatComment("c1", TimeSpan.FromSeconds(3725), DateTimeOffset.UnixEpoch, "1", "a", "Alice", null, [], [new ChatFragment("hello", null)]);
        try
        {
            await ChatWriter.WriteTextAsync(path, [comment], TimeSpan.FromSeconds(60));
            Assert.Equal("[1:01:05] Alice: hello", (await File.ReadAllTextAsync(path)).Trim());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Ten comments, one every 10s; pages of three. Cursor requests can be made to fail.</summary>
    private sealed class FakeChat(bool cursorWorks) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public List<string> ClientIds { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            Requests.Add(body);
            ClientIds.Add(request.Headers.GetValues("Client-ID").Single());
            var variables = JsonNode.Parse(body)!["variables"]!;

            int startIndex;
            if (variables["cursor"] is { } cursor)
            {
                if (!cursorWorks)
                {
                    return Json("""{"errors":[{"message":"failed integrity check"}]}""");
                }

                startIndex = int.Parse(cursor.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture) + 1;
            }
            else
            {
                var offset = variables["contentOffsetSeconds"]!.GetValue<int>();
                startIndex = (offset + 9) / 10;
            }

            var edges = new JsonArray();
            for (var i = startIndex; i < Math.Min(startIndex + 3, 10); i++)
            {
                edges.Add(new JsonObject
                {
                    ["cursor"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["node"] = new JsonObject
                    {
                        ["id"] = $"c{i}",
                        ["contentOffsetSeconds"] = i * 10,
                        ["createdAt"] = "2026-10-11T09:00:00Z",
                        ["commenter"] = new JsonObject { ["id"] = "1", ["login"] = "a", ["displayName"] = "A" },
                        ["message"] = new JsonObject { ["fragments"] = new JsonArray(new JsonObject { ["text"] = $"msg {i}" }) },
                    },
                });
            }

            var response = new JsonObject
            {
                ["data"] = new JsonObject
                {
                    ["video"] = new JsonObject
                    {
                        ["comments"] = new JsonObject
                        {
                            ["edges"] = edges,
                            ["pageInfo"] = new JsonObject { ["hasNextPage"] = startIndex + 3 < 10 },
                        },
                    },
                },
            };
            return Json(response.ToJsonString());
        }

        private static HttpResponseMessage Json(string text) =>
            new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }
}
