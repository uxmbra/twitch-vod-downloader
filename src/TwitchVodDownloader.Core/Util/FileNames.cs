using System.Text;

namespace TwitchVodDownloader.Core.Util;

/// <summary>Builds safe Windows file names from Twitch titles, which can contain anything.</summary>
public static class FileNames
{
    // Union of Windows and Unix invalid characters, so names are portable whatever OS builds them.
    private static readonly HashSet<char> Invalid = [.. "<>:\"/\\|?*", .. Enumerable.Range(0, 32).Select(i => (char)i)];

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string Sanitize(string name, int maxLength = 180)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(Invalid.Contains(c) ? '_' : c);
        }

        var result = builder.ToString().Trim().TrimEnd('.', ' ');
        if (result.Length > maxLength)
        {
            result = result[..maxLength].TrimEnd('.', ' ');
        }

        if (result.Length == 0)
        {
            return "download";
        }

        return Reserved.Contains(Path.GetFileNameWithoutExtension(result)) ? "_" + result : result;
    }
}
