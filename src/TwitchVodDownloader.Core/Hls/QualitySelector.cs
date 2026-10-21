using System.Globalization;
using System.Text.RegularExpressions;

namespace TwitchVodDownloader.Core.Hls;

/// <summary>Picks a rendition. With no preference, the best one wins.</summary>
public static partial class QualitySelector
{
    [GeneratedRegex(@"^(?<h>\d+)p?(?<fps>\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex QualityPattern();

    /// <summary>Ranks renditions best-first: resolution, then frame rate, then the untouched source, then bitrate.</summary>
    public static IOrderedEnumerable<HlsVariant> Rank(IEnumerable<HlsVariant> variants) =>
        variants
            .OrderByDescending(v => v.Height)
            .ThenByDescending(v => Math.Round(v.FrameRate))
            .ThenByDescending(v => v.IsSource)
            .ThenByDescending(v => v.Bandwidth);

    /// <param name="preference">
    /// <c>null</c>, "best" or "source" for the top rendition; "audio" for audio only; or a
    /// quality like "720p60" / "720p" / "720". A specific quality falls back to the best
    /// rendition at or below it so a batch never fails just because one VOD lacks 60fps.
    /// </param>
    public static HlsVariant Select(IReadOnlyList<HlsVariant> variants, string? preference)
    {
        if (variants.Count == 0)
        {
            throw new InvalidOperationException("The playlist did not list any renditions.");
        }

        var pref = preference?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(pref) || pref is "best" or "source" or "max" or "highest")
        {
            return Rank(variants).First();
        }

        if (pref is "audio" or "audio_only" or "audio-only")
        {
            return variants.FirstOrDefault(v => v.IsAudioOnly)
                ?? throw new InvalidOperationException("This video has no audio-only rendition.");
        }

        var exact = variants.FirstOrDefault(v => v.Name.Equals(pref, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var match = QualityPattern().Match(pref);
        if (!match.Success)
        {
            throw new ArgumentException(
                $"Unknown quality '{preference}'. Available: {string.Join(", ", variants.Select(v => v.Name))}",
                nameof(preference));
        }

        var height = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        var fps = match.Groups["fps"].Success ? int.Parse(match.Groups["fps"].Value, CultureInfo.InvariantCulture) : int.MaxValue;

        return Rank(variants.Where(v => !v.IsAudioOnly && v.Height <= height && Math.Round(v.FrameRate) <= fps)).FirstOrDefault()
            ?? Rank(variants.Where(v => !v.IsAudioOnly)).Last();
    }
}
