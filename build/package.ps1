<#
.SYNOPSIS
  Builds the Windows release zips: the desktop app (with ffmpeg) and the command-line tool.

.EXAMPLE
  ./build/package.ps1 -Version 0.4.0
  Output lands in ./artifacts.
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Runtime = 'win-x64',
    [string] $Output = (Join-Path $PSScriptRoot '..' 'artifacts')
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$work = Join-Path $Output 'work'
Remove-Item $Output -Recurse -Force -ErrorAction SilentlyContinue
New-Item $work -ItemType Directory -Force | Out-Null

function Publish($project, $dir) {
    dotnet publish (Join-Path $root $project) -c Release -r $Runtime -o $dir `
        -p:Version=$Version -p:ContinuousIntegrationBuild=true
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project" }
}

# ffmpeg: LGPL build from BtbN/FFmpeg-Builds (the app only stream-copies, so no GPL encoders are needed).
$ffmpegZip = Join-Path $work 'ffmpeg.zip'
$headers = @{ 'User-Agent' = 'twitch-vod-downloader-build' }
if ($env:GH_TOKEN) { $headers.Authorization = "Bearer $env:GH_TOKEN" }  # avoids anonymous API rate limits on CI
$release = Invoke-RestMethod 'https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest' -Headers $headers
$asset = $release.assets |
    Where-Object { $_.name -match '^ffmpeg-n[\d.]+-latest-win64-lgpl-[\d.]+\.zip$' } |
    Sort-Object name -Descending |
    Select-Object -First 1
if (-not $asset) {
    $asset = $release.assets | Where-Object { $_.name -eq 'ffmpeg-master-latest-win64-lgpl.zip' } | Select-Object -First 1
}
if (-not $asset) { throw 'Could not find an LGPL win64 ffmpeg build.' }
Write-Host "Using $($asset.name)"
Invoke-WebRequest $asset.browser_download_url -OutFile $ffmpegZip
Expand-Archive $ffmpegZip -DestinationPath (Join-Path $work 'ffmpeg')
$ffmpegDir = Get-ChildItem (Join-Path $work 'ffmpeg') -Directory | Select-Object -First 1

# Desktop app + ffmpeg
$app = Join-Path $work 'TwitchVodDownloader'
Publish 'src/TwitchVodDownloader.App/TwitchVodDownloader.App.csproj' $app
Copy-Item (Join-Path $ffmpegDir.FullName 'bin/ffmpeg.exe') $app
New-Item (Join-Path $app 'licenses') -ItemType Directory | Out-Null
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $app 'licenses/twitch-vod-downloader-LICENSE.txt')
Copy-Item (Join-Path $ffmpegDir.FullName 'LICENSE.txt') (Join-Path $app 'licenses/ffmpeg-LICENSE.txt')
@"
ffmpeg.exe is an unmodified LGPL build of FFmpeg ($($asset.name)) from
https://github.com/BtbN/FFmpeg-Builds. FFmpeg source code: https://ffmpeg.org/download.html
"@ | Set-Content (Join-Path $app 'licenses/ffmpeg-NOTICE.txt')
Get-ChildItem $app -Filter *.pdb | Remove-Item

# Command-line tool (uses the ffmpeg on PATH, or --ffmpeg)
$cli = Join-Path $work 'tvd'
Publish 'src/TwitchVodDownloader.Cli/TwitchVodDownloader.Cli.csproj' $cli
Get-ChildItem $cli -Filter *.pdb | Remove-Item

Compress-Archive (Join-Path $app '*') (Join-Path $Output "TwitchVodDownloader-$Version-$Runtime.zip")
Compress-Archive (Join-Path $cli '*') (Join-Path $Output "tvd-cli-$Version-$Runtime.zip")
Remove-Item $work -Recurse -Force
Get-ChildItem $Output | Format-Table Name, Length
