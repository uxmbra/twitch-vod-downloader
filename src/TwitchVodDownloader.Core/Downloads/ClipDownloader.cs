using System.Globalization;
using TwitchVodDownloader.Core.Media;
using TwitchVodDownloader.Core.Twitch;

namespace TwitchVodDownloader.Core.Downloads;

/// <summary>Downloads clips. Twitch serves them as ready-made MP4 files, one per quality.</summary>
public sealed class ClipDownloader(TwitchApiClient api, HttpClient http, Ffmpeg? ffmpeg)
{
    public async Task<DownloadResult> DownloadAsync(
        ClipDownloadRequest request,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report(new DownloadProgress(DownloadStage.Preparing, 0, 1, 0));

        var clip = await api.GetClipAsync(request.Slug, ct);
        var quality = SelectQuality(clip.Qualities, request.Quality);
        var uri = TwitchApiClient.SignClipUri(quality, clip.AccessToken);
        var trimming = request.Start is not null || request.End is not null;

        if (trimming && ffmpeg is null)
        {
            throw new FfmpegException("Trimming a clip needs ffmpeg.");
        }

        var workDir = Path.Combine(request.TempDirectory ?? Path.GetTempPath(), $"tvd-clip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            var raw = Path.Combine(workDir, "clip.mp4");
            long bytes = 0;
            long? length = await GetContentLengthAsync(uri, ct);
            await HttpFileFetcher.FetchAsync(http, uri, raw, n =>
            {
                bytes += n;
                progress?.Report(new DownloadProgress(DownloadStage.Downloading, 0, 1, bytes, length));
            }, ct);

            var staged = raw;
            if (trimming)
            {
                progress?.Report(new DownloadProgress(DownloadStage.Finalizing, 1, 1, bytes, length));
                var start = request.Start ?? TimeSpan.Zero;
                TimeSpan? span = request.End is { } end ? end - start : null;
                if (span <= TimeSpan.Zero)
                {
                    throw new ArgumentException("End time must be after start time.");
                }

                staged = Path.Combine(workDir, "trimmed.mp4");
                await ffmpeg!.RemuxAsync(raw, staged, start, span, ct);
            }

            var output = Path.GetFullPath(request.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Move(staged, output, overwrite: true);

            var size = new FileInfo(output).Length;
            progress?.Report(new DownloadProgress(DownloadStage.Done, 1, 1, size, size));
            var name = quality.Height + "p" + (quality.FrameRate > 31 ? Math.Round(quality.FrameRate).ToString(CultureInfo.InvariantCulture) : "");
            return new DownloadResult(output, name, size);
        }
        finally
        {
            VodDownloader.TryDeleteDirectory(workDir);
        }
    }

    public static ClipQuality SelectQuality(IReadOnlyList<ClipQuality> qualities, string? preference)
    {
        var ranked = qualities.OrderByDescending(q => q.Height).ThenByDescending(q => q.FrameRate).ToList();
        var pref = preference?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(pref) || pref is "best" or "source" or "max" or "highest")
        {
            return ranked[0];
        }

        var digits = new string(pref.TakeWhile(char.IsDigit).ToArray());
        if (!int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
        {
            throw new ArgumentException($"Unknown clip quality '{preference}'. Available: {string.Join(", ", ranked.Select(q => q.Height + "p"))}");
        }

        return ranked.FirstOrDefault(q => q.Height <= height) ?? ranked[^1];
    }

    private async Task<long?> GetContentLengthAsync(Uri uri, CancellationToken ct)
    {
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await http.SendAsync(head, ct);
            return response.IsSuccessStatusCode ? response.Content.Headers.ContentLength : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
