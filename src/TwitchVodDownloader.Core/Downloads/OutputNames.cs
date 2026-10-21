using System.Globalization;
using TwitchVodDownloader.Core.Twitch;
using TwitchVodDownloader.Core.Util;

namespace TwitchVodDownloader.Core.Downloads;

/// <summary>Default file names, e.g. "2026-10-21 shroud - Late night ranked [2295401234].mp4".</summary>
public static class OutputNames
{
    public static string For(VideoInfo video) => Build(video.CreatedAt, video.ChannelName, video.Title, video.Id);

    public static string For(ClipInfo clip) => Build(clip.CreatedAt, clip.ChannelName, clip.Title, clip.Slug);

    private static string Build(DateTimeOffset created, string channel, string title, string id)
    {
        var date = created == DateTimeOffset.MinValue ? "" : created.ToString("yyyy-MM-dd ", CultureInfo.InvariantCulture);
        return FileNames.Sanitize($"{date}{channel} - {title} [{id}]") + ".mp4";
    }
}
