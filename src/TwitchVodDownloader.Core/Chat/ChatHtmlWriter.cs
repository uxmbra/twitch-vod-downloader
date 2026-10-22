using System.Globalization;
using System.Net;
using System.Text;
using TwitchVodDownloader.Core.Twitch;

namespace TwitchVodDownloader.Core.Chat;

/// <summary>
/// Renders chat as a single HTML page styled like Twitch's dark chat, for reading and for clean
/// screenshots. Emotes and badges load from Twitch's CDN, so they need an internet connection.
/// </summary>
public static class ChatHtmlWriter
{
    // Twitch assigns one of these to users who never picked a name colour.
    private static readonly string[] DefaultColors =
    [
        "#FF0000", "#0000FF", "#008000", "#B22222", "#FF7F50", "#9ACD32", "#FF4500", "#2E8B57",
        "#DAA520", "#D2691E", "#5F9EA0", "#1E90FF", "#FF69B4", "#8A2BE2", "#00FF7F",
    ];

    public static async Task WriteAsync(
        string path,
        string title,
        string? channel,
        DateTimeOffset? createdAt,
        IReadOnlyList<ChatComment> comments,
        TimeSpan start,
        IReadOnlyList<ChatBadgeImage> badges,
        CancellationToken ct = default)
    {
        var html = Render(title, channel, createdAt, comments, start, badges);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(path, html, new UTF8Encoding(false), ct);
    }

    public static string Render(
        string title,
        string? channel,
        DateTimeOffset? createdAt,
        IReadOnlyList<ChatComment> comments,
        TimeSpan start,
        IReadOnlyList<ChatBadgeImage> badges)
    {
        var badgeLookup = badges.ToDictionary(b => ChatBadgeImage.Key(b.SetId, b.Version), StringComparer.Ordinal);
        var sb = new StringBuilder(4096 + (comments.Count * 300));

        var subtitle = string.Join(" · ", new[]
        {
            channel,
            createdAt is { } d && d != DateTimeOffset.MinValue ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
            $"{comments.Count.ToString("N0", CultureInfo.InvariantCulture)} messages",
        }.Where(s => !string.IsNullOrEmpty(s)));

        sb.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
          .Append("<title>Chat · ").Append(E(title)).Append("</title>\n<style>\n").Append(Css).Append("</style>\n</head>\n<body>\n");

        sb.Append("<header class=\"toolbar\">\n<div class=\"meta\"><div class=\"title\">").Append(E(title)).Append("</div>")
          .Append("<div class=\"sub\">").Append(E(subtitle)).Append("</div></div>\n")
          .Append(Controls)
          .Append("</header>\n");

        sb.Append("<main><section class=\"chat\" id=\"chat\">\n<div class=\"chat-head\">STREAM CHAT</div>\n");
        foreach (var c in comments)
        {
            sb.Append("<div class=\"msg\"><span class=\"ts\">").Append(Timestamp(c.Offset - start)).Append("</span>");

            if (c.Badges.Count > 0)
            {
                sb.Append("<span class=\"badges\">");
                foreach (var b in c.Badges)
                {
                    if (badgeLookup.TryGetValue(ChatBadgeImage.Key(b.SetId, b.Version), out var image))
                    {
                        sb.Append("<img class=\"badge\" loading=\"lazy\" src=\"").Append(E(image.ImageUri.ToString()))
                          .Append("\" alt=\"").Append(E(image.Title ?? b.SetId)).Append("\" title=\"").Append(E(image.Title ?? b.SetId)).Append("\">");
                    }
                }

                sb.Append("</span>");
            }

            var name = string.Equals(c.UserName, c.UserLogin, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(c.UserLogin)
                ? c.UserName
                : $"{c.UserName} ({c.UserLogin})";
            sb.Append("<span class=\"name\" style=\"color:").Append(NameColor(c)).Append("\">").Append(E(name)).Append("</span>")
              .Append("<span class=\"colon\">: </span><span class=\"text\">");

            foreach (var f in c.Fragments)
            {
                if (f.EmoteId is { Length: > 0 } emote)
                {
                    var id = Uri.EscapeDataString(emote);
                    sb.Append("<img class=\"emote\" loading=\"lazy\" src=\"https://static-cdn.jtvnw.net/emoticons/v2/").Append(id)
                      .Append("/default/dark/1.0\" srcset=\"https://static-cdn.jtvnw.net/emoticons/v2/").Append(id)
                      .Append("/default/dark/2.0 2x\" alt=\"").Append(E(f.Text)).Append("\" title=\"").Append(E(f.Text)).Append("\">");
                }
                else
                {
                    sb.Append(E(f.Text));
                }
            }

            sb.Append("</span></div>\n");
        }

        sb.Append("</section></main>\n<script>\n").Append(Script).Append("</script>\n</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>
    /// The user's chosen colour, or Twitch's default for them, lifted if it is too dark to read
    /// on the dark background (Twitch does the same).
    /// </summary>
    internal static string NameColor(ChatComment c)
    {
        var hex = c.Color is { Length: 7 } color && color[0] == '#' ? color : DefaultColors[StableHash(c.UserLogin) % DefaultColors.Length];
        var r = int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        for (var i = 0; i < 10 && Luminance(r, g, b) < 0.25; i++)
        {
            r += (255 - r) / 4;
            g += (255 - g) / 4;
            b += (255 - b) / 4;
        }

        return string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");
    }

    private static double Luminance(int r, int g, int b)
    {
        static double Channel(int v)
        {
            var c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(r)) + (0.7152 * Channel(g)) + (0.0722 * Channel(b));
    }

    // string.GetHashCode is randomised per process; colours must not change between exports.
    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 17;
            foreach (var ch in value)
            {
                hash = (hash * 31) + ch;
            }

            return hash & int.MaxValue;
        }
    }

    private static string Timestamp(TimeSpan value) => ChatWriter.FormatTimestamp(value);

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");

    private const string Controls = """
        <div class="controls">
          <label><input type="checkbox" id="opt-ts" checked> Timestamps</label>
          <label><input type="checkbox" id="opt-badges" checked> Badges</label>
          <label>Text <input type="range" id="opt-size" min="11" max="24" value="13"></label>
          <label>Width <input type="range" id="opt-width" min="280" max="900" step="10" value="340"></label>
          <label>Find <input type="search" id="opt-find" placeholder="user or text"></label>
        </div>

        """;

    private const string Css = """
        :root { --size: 13px; --width: 340px; }
        * { box-sizing: border-box; }
        html, body { margin: 0; background: #0e0e10; color: #efeff1; }
        body { font-family: Inter, "Roobert", "Helvetica Neue", Helvetica, Arial, sans-serif; }
        .toolbar { position: sticky; top: 0; z-index: 2; display: flex; flex-wrap: wrap; gap: 12px 24px;
          align-items: center; justify-content: space-between; padding: 12px 20px; background: #18181b;
          border-bottom: 1px solid #2f2f35; }
        .toolbar .title { font-weight: 700; font-size: 15px; }
        .toolbar .sub { color: #adadb8; font-size: 12px; margin-top: 2px; }
        .controls { display: flex; flex-wrap: wrap; gap: 8px 16px; font-size: 12px; color: #adadb8; }
        .controls label { display: inline-flex; align-items: center; gap: 6px; }
        .controls input[type=range] { width: 90px; accent-color: #9147ff; }
        .controls input[type=checkbox] { accent-color: #9147ff; }
        .controls input[type=search] { background: #0e0e10; color: #efeff1; border: 1px solid #3a3a3d;
          border-radius: 4px; padding: 3px 6px; width: 140px; }
        main { display: flex; justify-content: center; padding: 24px 12px 48px; }
        .chat { width: var(--width); max-width: 100%; background: #18181b; border: 1px solid #2f2f35; }
        .chat-head { text-align: center; font-size: 13px; font-weight: 600; letter-spacing: .04em; color: #efeff1;
          padding: 12px; border-bottom: 1px solid #2f2f35; }
        .msg { padding: 5px 20px; font-size: var(--size); line-height: 1.55; overflow-wrap: anywhere; }
        .msg:hover { background: #26262c; }
        .ts { color: #adadb8; margin-right: 5px; font-variant-numeric: tabular-nums; }
        .badges { margin-right: 3px; }
        .badge { width: 18px; height: 18px; margin: 0 3px 0 0; vertical-align: middle; position: relative; top: -1px; }
        .name { font-weight: 700; }
        .emote { height: 28px; width: auto; vertical-align: middle; margin: -5px 0; }
        body.no-ts .ts, body.no-badges .badges { display: none; }
        .msg.hidden { display: none; }
        @media print {
          .toolbar { display: none; }
          html, body { background: #18181b; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
          main { padding: 0; }
          .msg { break-inside: avoid; }
        }

        """;

    private const string Script = """
        (() => {
          const body = document.body, root = document.documentElement;
          const bind = (id, fn) => { const el = document.getElementById(id); el.addEventListener('input', () => fn(el)); fn(el); };
          bind('opt-ts', el => body.classList.toggle('no-ts', !el.checked));
          bind('opt-badges', el => body.classList.toggle('no-badges', !el.checked));
          bind('opt-size', el => root.style.setProperty('--size', el.value + 'px'));
          bind('opt-width', el => root.style.setProperty('--width', el.value + 'px'));
          const msgs = Array.from(document.querySelectorAll('.msg'));
          let timer;
          document.getElementById('opt-find').addEventListener('input', e => {
            clearTimeout(timer);
            timer = setTimeout(() => {
              const q = e.target.value.trim().toLowerCase();
              for (const m of msgs) m.classList.toggle('hidden', q !== '' && !m.textContent.toLowerCase().includes(q));
            }, 150);
          });
        })();

        """;
}
