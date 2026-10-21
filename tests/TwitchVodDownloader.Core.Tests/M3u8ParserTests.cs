using TwitchVodDownloader.Core.Hls;

namespace TwitchVodDownloader.Core.Tests;

public class M3u8ParserTests
{
    // Shape of a real usher VOD master playlist, trimmed.
    internal const string Master = """
        #EXTM3U
        #EXT-X-TWITCH-INFO:ORIGIN="s3",B="false",REGION="EU",USER-IP="1.2.3.4",CLUSTER="cloudfront_vod"
        #EXT-X-MEDIA:TYPE=VIDEO,GROUP-ID="chunked",NAME="1080p60 (source)",AUTOSELECT=YES,DEFAULT=YES
        #EXT-X-STREAM-INF:BANDWIDTH=8534030,CODECS="avc1.64002A,mp4a.40.2",RESOLUTION=1920x1080,VIDEO="chunked",FRAME-RATE=60.000
        https://d1m7jfoe9zdc1j.cloudfront.net/abc_channel_123/chunked/index-dvr.m3u8
        #EXT-X-MEDIA:TYPE=VIDEO,GROUP-ID="720p60",NAME="720p60",AUTOSELECT=YES,DEFAULT=YES
        #EXT-X-STREAM-INF:BANDWIDTH=3422999,CODECS="avc1.4D401F,mp4a.40.2",RESOLUTION=1280x720,VIDEO="720p60",FRAME-RATE=60.000
        https://d1m7jfoe9zdc1j.cloudfront.net/abc_channel_123/720p60/index-dvr.m3u8
        #EXT-X-MEDIA:TYPE=VIDEO,GROUP-ID="480p30",NAME="480p",AUTOSELECT=YES,DEFAULT=YES
        #EXT-X-STREAM-INF:BANDWIDTH=1427999,CODECS="avc1.4D401F,mp4a.40.2",RESOLUTION=852x480,VIDEO="480p30",FRAME-RATE=30.000
        https://d1m7jfoe9zdc1j.cloudfront.net/abc_channel_123/480p30/index-dvr.m3u8
        #EXT-X-MEDIA:TYPE=VIDEO,GROUP-ID="audio_only",NAME="Audio Only",AUTOSELECT=NO,DEFAULT=NO
        #EXT-X-STREAM-INF:BANDWIDTH=160000,CODECS="mp4a.40.2",VIDEO="audio_only"
        https://d1m7jfoe9zdc1j.cloudfront.net/abc_channel_123/audio_only/index-dvr.m3u8
        """;

    internal const string Media = """
        #EXTM3U
        #EXT-X-VERSION:3
        #EXT-X-TARGETDURATION:10
        #ID3-EQUIV-TDTG:2026-10-21T12:00:00
        #EXT-X-PLAYLIST-TYPE:EVENT
        #EXT-X-MEDIA-SEQUENCE:0
        #EXT-X-TWITCH-ELAPSED-SECS:0.000
        #EXT-X-TWITCH-TOTAL-SECS:25.500
        #EXTINF:10.000,
        0.ts
        #EXTINF:10.000,
        1-unmuted.ts
        #EXTINF:5.500,
        2.ts
        #EXT-X-ENDLIST
        """;

    private static readonly Uri Base = new("https://usher.ttvnw.net/vod/123.m3u8");

    [Fact]
    public void Parses_master_playlist()
    {
        var variants = M3u8Parser.ParseMaster(Master, Base);

        Assert.Equal(4, variants.Count);
        var source = variants[0];
        Assert.Equal("1080p60 (source)", source.Name);
        Assert.True(source.IsSource);
        Assert.Equal(1920, source.Width);
        Assert.Equal(1080, source.Height);
        Assert.Equal(60, source.FrameRate);
        Assert.Equal(8534030, source.Bandwidth);
        Assert.Equal("avc1.64002A,mp4a.40.2", source.Codecs);
        Assert.Equal("https://d1m7jfoe9zdc1j.cloudfront.net/abc_channel_123/chunked/index-dvr.m3u8", source.Uri.ToString());

        Assert.True(variants[3].IsAudioOnly);
        Assert.False(variants[1].IsSource);
    }

    [Fact]
    public void Parses_media_playlist_with_relative_segments()
    {
        var playlist = M3u8Parser.ParseMedia(Media, new Uri("https://cdn.example/vod/chunked/index-dvr.m3u8"));

        Assert.Equal(3, playlist.Segments.Count);
        Assert.Equal("https://cdn.example/vod/chunked/1-unmuted.ts", playlist.Segments[1].Uri.ToString());
        Assert.Equal(TimeSpan.FromSeconds(20), playlist.Segments[2].Start);
        Assert.Equal(TimeSpan.FromSeconds(25.5), playlist.Duration);
        Assert.False(playlist.IsFragmentedMp4);
    }

    [Fact]
    public void Parses_fmp4_init_segment()
    {
        const string text = """
            #EXTM3U
            #EXT-X-MAP:URI="init-0.mp4"
            #EXTINF:2.000,
            0.mp4
            """;

        var playlist = M3u8Parser.ParseMedia(text, new Uri("https://cdn.example/vod/1440p60/index-dvr.m3u8"));

        Assert.True(playlist.IsFragmentedMp4);
        Assert.Equal("https://cdn.example/vod/1440p60/init-0.mp4", playlist.InitializationUri!.ToString());
    }

    [Fact]
    public void Attribute_values_can_contain_commas()
    {
        var attrs = M3u8Parser.ParseAttributes("""BANDWIDTH=1,CODECS="avc1.64002A,mp4a.40.2",RESOLUTION=1x1""");

        Assert.Equal("avc1.64002A,mp4a.40.2", attrs["CODECS"]);
        Assert.Equal("1x1", attrs["RESOLUTION"]);
    }

    [Fact]
    public void Rejects_non_playlists() =>
        Assert.Throws<FormatException>(() => M3u8Parser.ParseMaster("<html>Forbidden</html>", Base));
}
