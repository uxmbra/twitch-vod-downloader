# twitch-vod-downloader

A Windows desktop app for downloading Twitch VODs, highlights and clips at the
highest quality Twitch offers, with batch downloads, trimming and chat export.

Video is never re-encoded: the original segments are fetched and stream-copied into an MP4,
so what you get is exactly what Twitch stores, including the untranscoded "source" rendition
and HEVC/AV1 renditions (up to 1440p) from channels using Enhanced Broadcasting.

## Status

| Step | What | State |
| --- | --- | --- |
| 1 | Download engine + command-line tool | ✅ |
| 2 | Chat download (JSON + text) | ✅ |
| 3 | Desktop app (Avalonia) with download queue | ✅ |
| 4 | Single-file Windows release with bundled ffmpeg | ✅ |

## Download

Grab the latest `TwitchVodDownloader-<version>-win-x64.zip` from the
[Releases page](https://github.com/uxmbra/twitch-vod-downloader/releases), unzip it anywhere and
run `TwitchVodDownloader.exe`. Nothing to install: .NET and ffmpeg are included.
(Windows SmartScreen may warn the first time because the exe isn't code-signed; choose
*More info → Run anyway*.)

The command-line tool is a separate `tvd-cli-<version>-win-x64.zip`; it uses ffmpeg from your
PATH (`winget install Gyan.FFmpeg`) or `--ffmpeg <path>`.

## Building from source

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- ffmpeg on your PATH: `winget install Gyan.FFmpeg`

To build the release zips yourself: `./build/package.ps1 -Version 0.4.0` (output in `artifacts/`).

## Releasing

Push a tag like `v0.4.0`. The Release workflow runs the tests, builds both zips (downloading
an LGPL ffmpeg build) and publishes them as a GitHub release. Every pull request also builds
a test copy, downloadable from the CI run's *Artifacts*.

## Desktop app

```powershell
dotnet run --project src/TwitchVodDownloader.App
```

Paste one or more links (one per line), pick a quality and an optional start/end time, tick
Video and/or Chat, and press **Add to queue**. Downloads run in the background with progress
for each one; finished items have a **Show** button that opens Explorer on the file, and a
**Chat** button that opens the Twitch-styled chat in your browser.
Settings (output folder, parallelism, ffmpeg path, auth token) are saved in
`%APPDATA%\TwitchVodDownloader\settings.json`; the auth token is encrypted for your Windows user.

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
      --chat              Also save the chat next to the video (.chat.html, .chat.json, .chat.txt).
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

Chat is saved three ways: `.chat.html` is a Twitch-styled page (dark theme, name colours,
badges and emotes) made for reading and clean screenshots, with toggles for timestamps and
badges, text size, chat width and a search box; use your browser's *Print → Save as PDF* for a
PDF. `.chat.json` keeps everything for other tools, and `.chat.txt` is plain text. Emotes and
badges load from Twitch's servers, so the HTML needs an internet connection to show them.

Chat timestamps are relative to the start of the saved video, so they line up with trimmed
downloads. Clip chat comes from the VOD the clip was cut from, so it is only available while
that VOD still exists.

Trimmed VOD ranges are fetched segment by segment, so only the part you asked for is
downloaded. Because the video is not re-encoded, cuts land on the nearest keyframe
(usually within two seconds).

## Project layout

```text
src/TwitchVodDownloader.Core   download engine: Twitch API, HLS, chat, downloaders, ffmpeg
src/TwitchVodDownloader.App    desktop app (Avalonia, MVVM)
src/TwitchVodDownloader.Cli    command-line front end (tvd)
tests/                         unit tests, an end-to-end test against a fake Twitch, app tests
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

[MIT](LICENSE). Release builds include an unmodified LGPL build of [FFmpeg](https://ffmpeg.org);
its license is in the `licenses` folder of the zip.
