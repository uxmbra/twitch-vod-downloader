# twitch-vod-downloader

A Windows desktop app (in progress) for downloading Twitch VODs, highlights and clips at the
highest quality Twitch offers, with batch downloads, trimming and chat export.

Video is never re-encoded: the original segments are fetched and stream-copied into an MP4,
so what you get is exactly what Twitch stores, including the untranscoded "source" rendition
and HEVC/AV1 renditions (up to 1440p) from channels using Enhanced Broadcasting.

## Status

| Step | What | State |
| --- | --- | --- |
| 1 | Download engine + command-line tool | ✅ |
| 2 | Chat download (JSON + text) | ✅ |
| 3 | Desktop app (Avalonia) with download queue | planned |
| 4 | Installer with bundled ffmpeg, Twitch login for sub-only VODs | planned |

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- ffmpeg on your PATH: `winget install Gyan.FFmpeg`

## Command-line usage

```powershell
dotnet run --project src/TwitchVodDownloader.Cli -- <command> ...
```

```text
tvd info <link>                          show title, length and available qualities
tvd download <link> [<link> ...]         download at the best quality
tvd download --batch links.txt -o D:\Twitch\

Options:
  -o, --output <path>     Output file (one link) or folder (several). Default: current folder.
  -q, --quality <q>       best (default), 1080p60, 720p, audio ... Falls back to the next best.
  -s, --start <time>      Start of the range to keep, e.g. 1:02:03 or 1h2m3s.
  -e, --end <time>        End of the range to keep.
  -b, --batch <file>      Text file with one link per line (# for comments).
  -t, --threads <n>       Parallel segment downloads for VODs (default 8).
      --auth-token <tok>  twitch.tv auth-token cookie, for subscriber-only VODs
                          (or set TVD_AUTH_TOKEN).
      --ffmpeg <path>     Path to ffmpeg if it is not on PATH.
      --chat              Also save the chat (.chat.json and .chat.txt next to the video).
      --chat-only         Save only the chat, no video.
      --h264-only         Skip HEVC/AV1 renditions (for players without those codecs).
      --temp <dir>        Folder for temporary segment files.
```

Examples:

```powershell
# Whole VOD at source quality
tvd download https://www.twitch.tv/videos/2295401234

# Just one hour of it
tvd download https://www.twitch.tv/videos/2295401234 --start 1:00:00 --end 2:00:00 -o highlight.mp4

# A clip
tvd download https://clips.twitch.tv/FunnyCleverOtterKappa-AbC123

# A VOD's chat only, as JSON and readable text
tvd download https://www.twitch.tv/videos/2295401234 --chat-only
```

Chat timestamps are relative to the start of the saved video, so they line up with trimmed
downloads. Clip chat comes from the VOD the clip was cut from, so it is only available while
that VOD still exists.

Trimmed VOD ranges are fetched segment by segment, so only the part you asked for is
downloaded. Because the video is not re-encoded, cuts land on the nearest keyframe
(usually within two seconds).

## Project layout

```text
src/TwitchVodDownloader.Core   download engine: Twitch API, HLS parsing, downloaders, ffmpeg
src/TwitchVodDownloader.Cli    command-line front end (tvd)
tests/                         unit tests and an end-to-end test against a fake Twitch
```

## How it works

1. Asks Twitch's GraphQL API (the one twitch.tv uses) for a signed playback token.
2. Fetches the master playlist from Twitch's usher service and picks the best rendition by
   resolution, then frame rate, then the untranscoded source, then bitrate.
3. Downloads segments in parallel with retries, swapping in the `-muted` copy where Twitch's
   playlist lists an `-unmuted` segment it won't serve.
4. Joins the segments and remuxes them to MP4 with ffmpeg (`-c copy`).

These are undocumented endpoints, so Twitch can change them without notice.

## Disclaimer

For personal use. Respect creators' rights and Twitch's terms of service.

## License

[MIT](LICENSE)
