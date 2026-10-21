using System.Globalization;

namespace TwitchVodDownloader.Core.Hls;

/// <summary>
/// A small HLS playlist parser covering what Twitch serves. Unknown tags (Twitch adds plenty
/// of its own) are ignored rather than rejected.
/// </summary>
public static class M3u8Parser
{
    public static IReadOnlyList<HlsVariant> ParseMaster(string text, Uri baseUri)
    {
        EnsureHeader(text);

        var groupNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var variants = new List<HlsVariant>();
        Dictionary<string, string>? pending = null;

        foreach (var line in Lines(text))
        {
            if (line.StartsWith("#EXT-X-MEDIA:", StringComparison.Ordinal))
            {
                var media = ParseAttributes(line["#EXT-X-MEDIA:".Length..]);
                if (media.TryGetValue("GROUP-ID", out var group) && media.TryGetValue("NAME", out var name))
                {
                    groupNames[group] = name;
                }
            }
            else if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal))
            {
                pending = ParseAttributes(line["#EXT-X-STREAM-INF:".Length..]);
            }
            else if (!line.StartsWith('#') && pending is not null)
            {
                variants.Add(BuildVariant(pending, new Uri(baseUri, line), groupNames));
                pending = null;
            }
        }

        return variants;
    }

    public static HlsMediaPlaylist ParseMedia(string text, Uri baseUri)
    {
        EnsureHeader(text);

        var segments = new List<HlsSegment>();
        Uri? init = null;
        TimeSpan? nextDuration = null;
        var position = TimeSpan.Zero;

        foreach (var line in Lines(text))
        {
            if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            {
                var value = line["#EXTINF:".Length..];
                var comma = value.IndexOf(',', StringComparison.Ordinal);
                if (comma >= 0)
                {
                    value = value[..comma];
                }

                nextDuration = TimeSpan.FromSeconds(double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture));
            }
            else if (line.StartsWith("#EXT-X-MAP:", StringComparison.Ordinal))
            {
                var map = ParseAttributes(line["#EXT-X-MAP:".Length..]);
                if (map.TryGetValue("URI", out var mapUri))
                {
                    init = new Uri(baseUri, mapUri);
                }
            }
            else if (!line.StartsWith('#'))
            {
                if (nextDuration is not { } duration)
                {
                    throw new FormatException($"Segment '{line}' has no #EXTINF duration.");
                }

                segments.Add(new HlsSegment(new Uri(baseUri, line), position, duration));
                position += duration;
                nextDuration = null;
            }
        }

        return new HlsMediaPlaylist(segments, init);
    }

    /// <summary>Parses an HLS attribute list, honouring commas inside quoted values.</summary>
    internal static Dictionary<string, string> ParseAttributes(string list)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < list.Length)
        {
            var eq = list.IndexOf('=', i);
            if (eq < 0)
            {
                break;
            }

            var key = list[i..eq].Trim();
            i = eq + 1;

            string value;
            if (i < list.Length && list[i] == '"')
            {
                var close = list.IndexOf('"', i + 1);
                if (close < 0)
                {
                    close = list.Length;
                }

                value = list[(i + 1)..close];
                i = close + 1;
                var comma = list.IndexOf(',', Math.Min(i, list.Length));
                i = comma < 0 ? list.Length : comma + 1;
            }
            else
            {
                var comma = list.IndexOf(',', i);
                value = comma < 0 ? list[i..] : list[i..comma];
                i = comma < 0 ? list.Length : comma + 1;
            }

            result[key] = value.Trim();
        }

        return result;
    }

    private static HlsVariant BuildVariant(Dictionary<string, string> attrs, Uri uri, Dictionary<string, string> groupNames)
    {
        var group = attrs.GetValueOrDefault("VIDEO") ?? attrs.GetValueOrDefault("AUDIO") ?? string.Empty;

        int width = 0, height = 0;
        if (attrs.TryGetValue("RESOLUTION", out var resolution))
        {
            var parts = resolution.Split('x', 'X');
            if (parts.Length == 2)
            {
                _ = int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out width);
                _ = int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out height);
            }
        }

        _ = long.TryParse(attrs.GetValueOrDefault("BANDWIDTH"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bandwidth);
        _ = double.TryParse(attrs.GetValueOrDefault("FRAME-RATE"), NumberStyles.Float, CultureInfo.InvariantCulture, out var frameRate);

        var name = groupNames.GetValueOrDefault(group)
            ?? (height > 0 ? $"{height}p{(frameRate > 31 ? Math.Round(frameRate).ToString(CultureInfo.InvariantCulture) : string.Empty)}" : "audio_only");

        return new HlsVariant(name, group, uri, bandwidth, width, height, frameRate, attrs.GetValueOrDefault("CODECS"));
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);

    private static void EnsureHeader(string text)
    {
        if (!text.TrimStart('﻿', ' ', '\r', '\n', '\t').StartsWith("#EXTM3U", StringComparison.Ordinal))
        {
            throw new FormatException("Response is not an HLS playlist (missing #EXTM3U).");
        }
    }
}
