using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Web;

namespace TwitchVodDownloader.Core.Twitch;

/// <summary>Turns the many shapes of Twitch links (or bare IDs) into a <see cref="ContentRef"/>.</summary>
public static partial class TwitchUrlParser
{
    [GeneratedRegex(@"^\d+$")]
    private static partial Regex NumericId();

    [GeneratedRegex(@"^[A-Za-z0-9_-]+$")]
    private static partial Regex Slug();

    public static ContentRef Parse(string input) =>
        TryParse(input, out var result)
            ? result
            : throw new FormatException($"Not a recognised Twitch VOD, highlight or clip link: '{input}'");

    public static bool TryParse(string? input, [NotNullWhen(true)] out ContentRef? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();

        // Bare IDs: numbers are videos, anything else slug-shaped is a clip.
        if (NumericId().IsMatch(text))
        {
            result = new ContentRef(ContentKind.Video, text);
            return true;
        }

        if (!text.Contains('.') && !text.Contains('/'))
        {
            if (Slug().IsMatch(text))
            {
                result = new ContentRef(ContentKind.Clip, text);
                return true;
            }

            return false;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        foreach (var prefix in new[] { "www.", "m." })
        {
            if (host.StartsWith(prefix, StringComparison.Ordinal))
            {
                host = host[prefix.Length..];
            }
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (host == "clips.twitch.tv")
        {
            var slug = segments switch
            {
                ["embed"] => HttpUtility.ParseQueryString(uri.Query)["clip"],
                [var s] => s,
                _ => null,
            };
            return TryClip(slug, out result);
        }

        if (host != "twitch.tv")
        {
            return false;
        }

        var videoId = segments switch
        {
            ["videos", var id] => id,
            [_, "video" or "v", var id] => id,
            _ => null,
        };
        if (videoId is not null)
        {
            result = NumericId().IsMatch(videoId) ? new ContentRef(ContentKind.Video, videoId) : null;
            return result is not null;
        }

        return segments is [_, "clip", var clipSlug] && TryClip(clipSlug, out result);
    }

    private static bool TryClip(string? slug, [NotNullWhen(true)] out ContentRef? result)
    {
        result = !string.IsNullOrEmpty(slug) && Slug().IsMatch(slug)
            ? new ContentRef(ContentKind.Clip, slug)
            : null;
        return result is not null;
    }
}
