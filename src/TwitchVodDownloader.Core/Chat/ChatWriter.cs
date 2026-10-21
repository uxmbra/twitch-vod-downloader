using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwitchVodDownloader.Core.Twitch;

namespace TwitchVodDownloader.Core.Chat;

/// <summary>
/// Saves chat as JSON (everything, for tools and later rendering) and as plain text (for
/// reading). Timestamps are relative to <c>start</c>, so they line up with a trimmed video.
/// </summary>
public static class ChatWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task WriteJsonAsync(
        string path, VideoInfo? video, IReadOnlyList<ChatComment> comments, TimeSpan start, CancellationToken ct = default)
    {
        var document = new
        {
            format = "twitch-vod-downloader/chat@1",
            video = video is null ? null : new
            {
                id = video.Id,
                title = video.Title,
                channel = video.ChannelName,
                channelLogin = video.ChannelLogin,
                createdAt = video.CreatedAt,
                lengthSeconds = video.Length.TotalSeconds,
            },
            startSeconds = start.TotalSeconds,
            comments = comments.Select(c => new
            {
                id = c.Id,
                offsetSeconds = Math.Round((c.Offset - start).TotalSeconds, 3),
                videoOffsetSeconds = c.Offset.TotalSeconds,
                createdAt = c.CreatedAt,
                user = new { id = c.UserId, login = c.UserLogin, displayName = c.UserName, color = c.Color },
                badges = c.Badges.Select(b => new { setId = b.SetId, version = b.Version }),
                message = c.Text,
                fragments = c.Fragments.Select(f => new { text = f.Text, emoteId = f.EmoteId }),
            }),
        };

        EnsureDirectory(path);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, ct);
    }

    public static async Task WriteTextAsync(string path, IReadOnlyList<ChatComment> comments, TimeSpan start, CancellationToken ct = default)
    {
        var builder = new StringBuilder(comments.Count * 64);
        foreach (var c in comments)
        {
            builder.Append('[').Append(FormatTimestamp(c.Offset - start)).Append("] ")
                .Append(c.UserName).Append(": ").AppendLine(c.Text);
        }

        EnsureDirectory(path);
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);
    }

    internal static string FormatTimestamp(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return ((int)value.TotalHours).ToString(CultureInfo.InvariantCulture) + value.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private static void EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}
