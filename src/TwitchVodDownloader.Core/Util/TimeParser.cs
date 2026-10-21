using System.Globalization;
using System.Text.RegularExpressions;

namespace TwitchVodDownloader.Core.Util;

/// <summary>
/// Parses timestamps as people type them: <c>90</c>, <c>1:30</c>, <c>1:02:03.5</c>, or <c>1h2m3s</c>.
/// </summary>
public static partial class TimeParser
{
    [GeneratedRegex(@"^(?:(?<h>\d+)h)?(?:(?<m>\d+)m)?(?:(?<s>\d+(?:\.\d+)?)s)?$", RegexOptions.IgnoreCase)]
    private static partial Regex UnitFormat();

    public static TimeSpan Parse(string input) =>
        TryParse(input, out var value)
            ? value
            : throw new FormatException($"Could not read '{input}' as a time. Try 1:02:03, 62:03 or 1h2m3s.");

    public static bool TryParse(string? input, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();

        if (text.Contains(':'))
        {
            var parts = text.Split(':');
            if (parts.Length > 3)
            {
                return false;
            }

            double total = 0;
            for (var i = 0; i < parts.Length; i++)
            {
                var isLast = i == parts.Length - 1;
                if (!double.TryParse(parts[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n)
                    || (!isLast && parts[i].Contains('.'))
                    || (i > 0 && n >= 60))
                {
                    return false;
                }

                total = (total * 60) + n;
            }

            value = TimeSpan.FromSeconds(total);
            return true;
        }

        if (double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds))
        {
            value = TimeSpan.FromSeconds(seconds);
            return true;
        }

        var match = UnitFormat().Match(text);
        if (!match.Success || match.Length == 0)
        {
            return false;
        }

        static double Group(Match m, string name) =>
            m.Groups[name].Success ? double.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture) : 0;

        value = TimeSpan.FromSeconds((Group(match, "h") * 3600) + (Group(match, "m") * 60) + Group(match, "s"));
        return true;
    }
}
