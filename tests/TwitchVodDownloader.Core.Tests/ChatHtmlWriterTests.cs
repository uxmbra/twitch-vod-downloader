using TwitchVodDownloader.Core.Chat;

namespace TwitchVodDownloader.Core.Tests;

public class ChatHtmlWriterTests
{
    private static ChatComment Comment(string text, string? color = "#1E90FF", string login = "alice", string name = "Alice", IReadOnlyList<ChatFragment>? fragments = null, IReadOnlyList<ChatBadge>? badges = null) =>
        new("id", TimeSpan.FromSeconds(3725), DateTimeOffset.UnixEpoch, "1", login, name, color, badges ?? [], fragments ?? [new ChatFragment(text, null)]);

    [Fact]
    public void Renders_names_timestamps_relative_to_start_and_escapes_text()
    {
        var html = ChatHtmlWriter.Render("A <b>title</b>", "Chan", null, [Comment("<script>alert(1)</script>")], TimeSpan.FromSeconds(60), []);

        Assert.Contains("<span class=\"ts\">1:01:05</span>", html, StringComparison.Ordinal);
        Assert.Contains(">Alice</span>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("A &lt;b&gt;title&lt;/b&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Renders_emotes_and_known_badges()
    {
        var comment = Comment("", fragments: [new ChatFragment("hi ", null), new ChatFragment("Kappa", "25")], badges: [new ChatBadge("subscriber", "12"), new ChatBadge("unknown", "1")]);
        var badges = new[] { new ChatBadgeImage("subscriber", "12", new Uri("https://cdn.example/sub12.png"), "1-Year Subscriber") };

        var html = ChatHtmlWriter.Render("t", null, null, [comment], TimeSpan.Zero, badges);

        Assert.Contains("emoticons/v2/25/default/dark/1.0", html, StringComparison.Ordinal);
        Assert.Contains("alt=\"Kappa\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"https://cdn.example/sub12.png\"", html, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "class=\"badge\""));
    }

    [Fact]
    public void Shows_login_when_display_name_differs() =>
        Assert.Contains(">日本語 (nihongo)</span>", ChatHtmlWriter.Render("t", null, null, [Comment("x", login: "nihongo", name: "日本語")], TimeSpan.Zero, []), StringComparison.Ordinal);

    [Theory]
    [InlineData("#0000FF")]
    [InlineData("#000000")]
    [InlineData("#1A0A00")]
    public void Lifts_colours_too_dark_to_read(string color) =>
        Assert.NotEqual(color, ChatHtmlWriter.NameColor(Comment("x", color)));

    [Fact]
    public void Keeps_readable_colours_and_defaults_consistently()
    {
        Assert.Equal("#FF69B4", ChatHtmlWriter.NameColor(Comment("x", "#FF69B4")));
        Assert.Equal(ChatHtmlWriter.NameColor(Comment("x", null, "bob")), ChatHtmlWriter.NameColor(Comment("y", null, "bob")));
    }
}
