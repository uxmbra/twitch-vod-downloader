namespace TwitchVodDownloader.Core.Chat;

/// <summary>The picture for one badge version, e.g. subscriber/12.</summary>
public sealed record ChatBadgeImage(string SetId, string Version, Uri ImageUri, string? Title)
{
    public static string Key(string setId, string version) => setId + "/" + version;
}
