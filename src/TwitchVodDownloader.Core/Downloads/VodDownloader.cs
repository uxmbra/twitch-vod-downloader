using TwitchVodDownloader.Core.Hls;
using TwitchVodDownloader.Core.Media;
using TwitchVodDownloader.Core.Twitch;

namespace TwitchVodDownloader.Core.Downloads;

/// <summary>
/// Downloads past broadcasts, highlights and uploads. Segments are fetched in parallel,
/// joined in order and remuxed to MP4 without re-encoding.
/// </summary>
public sealed class VodDownloader(TwitchApiClient api, HttpClient http, Ffmpeg ffmpeg)
{
    public async Task<DownloadResult> DownloadAsync(
        VodDownloadRequest request,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaxParallelDownloads, 1);
        progress?.Report(new DownloadProgress(DownloadStage.Preparing, 0, 0, 0));

        var variants = await api.GetVideoVariantsAsync(request.VideoId, request.IncludeAdvancedCodecs, ct);
        var variant = QualitySelector.Select(variants, request.Quality);

        var playlistText = await http.GetStringAsync(variant.Uri, ct);
        var playlist = M3u8Parser.ParseMedia(playlistText, variant.Uri);
        if (playlist.Segments.Count == 0)
        {
            throw new TwitchApiException($"Video {request.VideoId} has no segments. It may still be processing.");
        }

        var plan = TrimPlan.Create(playlist, request.Start, request.End);

        var workDir = Path.Combine(request.TempDirectory ?? Path.GetTempPath(), $"tvd-{request.VideoId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            var combined = Path.Combine(workDir, playlist.IsFragmentedMp4 ? "combined.mp4" : "combined.ts");
            await DownloadSegmentsAsync(playlist, plan, workDir, combined, request.MaxParallelDownloads, progress, ct);

            progress?.Report(new DownloadProgress(DownloadStage.Finalizing, plan.Segments.Count, plan.Segments.Count, new FileInfo(combined).Length));
            var staged = Path.Combine(workDir, "output.mp4");
            await ffmpeg.RemuxAsync(combined, staged, plan.SeekOffset, plan.Length, ct);

            var output = Path.GetFullPath(request.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Move(staged, output, overwrite: true);

            var size = new FileInfo(output).Length;
            progress?.Report(new DownloadProgress(DownloadStage.Done, plan.Segments.Count, plan.Segments.Count, size, size));
            return new DownloadResult(output, variant.Name, size);
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    private async Task DownloadSegmentsAsync(
        HlsMediaPlaylist playlist,
        TrimPlan plan,
        string workDir,
        string combinedPath,
        int parallelism,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        var total = plan.Segments.Count;
        var done = 0;
        long bytes = 0;

        void Report() => progress?.Report(new DownloadProgress(
            DownloadStage.Downloading, Volatile.Read(ref done), total, Interlocked.Read(ref bytes)));

        Report();

        var options = new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct };
        await Parallel.ForEachAsync(Enumerable.Range(0, total), options, async (i, token) =>
        {
            await HttpFileFetcher.FetchAsync(http, plan.Segments[i].Uri, PartPath(workDir, i), n => Interlocked.Add(ref bytes, n), token);
            Interlocked.Increment(ref done);
            Report();
        });

        // MPEG-TS segments (and fMP4 fragments after their init segment) can be joined byte for byte.
        await using var combined = new FileStream(combinedPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
        if (playlist.InitializationUri is { } init)
        {
            var initPath = Path.Combine(workDir, "init.part");
            await HttpFileFetcher.FetchAsync(http, init, initPath, null, ct);
            await AppendAndDeleteAsync(initPath, combined, ct);
        }

        for (var i = 0; i < total; i++)
        {
            await AppendAndDeleteAsync(PartPath(workDir, i), combined, ct);
        }
    }

    private static string PartPath(string workDir, int index) => Path.Combine(workDir, $"{index:D6}.part");

    private static async Task AppendAndDeleteAsync(string path, Stream target, CancellationToken ct)
    {
        await using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
        {
            await source.CopyToAsync(target, ct);
        }

        File.Delete(path);
    }

    internal static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a locked temp file should not hide the real result or error.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
