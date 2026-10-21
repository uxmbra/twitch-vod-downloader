using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace TwitchVodDownloader.Core.Media;

public class FfmpegException(string message) : Exception(message);

/// <summary>Runs ffmpeg for remuxing and cutting. Video is never re-encoded, so quality is untouched.</summary>
public sealed class Ffmpeg(string executablePath)
{
    public string ExecutablePath { get; } = executablePath;

    /// <summary>
    /// Finds ffmpeg: an explicit path first, then next to the app (where a bundled copy will
    /// live), then the PATH.
    /// </summary>
    public static Ffmpeg Locate(string? explicitPath = null)
    {
        var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var candidate = Directory.Exists(explicitPath) ? Path.Combine(explicitPath, exe) : explicitPath;
            return File.Exists(candidate)
                ? new Ffmpeg(candidate)
                : throw new FfmpegException($"ffmpeg was not found at '{explicitPath}'.");
        }

        var local = Path.Combine(AppContext.BaseDirectory, exe);
        if (File.Exists(local))
        {
            return new Ffmpeg(local);
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (dir.Length == 0)
            {
                continue;
            }

            var onPath = Path.Combine(dir.Trim('"'), exe);
            if (File.Exists(onPath))
            {
                return new Ffmpeg(onPath);
            }
        }

        throw new FfmpegException(
            "ffmpeg was not found. Install it (for example `winget install Gyan.FFmpeg`) or pass its path with --ffmpeg.");
    }

    /// <summary>Copies streams from <paramref name="input"/> into an MP4, optionally cutting a range.</summary>
    public Task RemuxAsync(string input, string output, TimeSpan seek, TimeSpan? length, CancellationToken ct = default)
    {
        var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-y" };
        if (seek > TimeSpan.Zero)
        {
            args.AddRange(["-ss", Seconds(seek)]);
        }

        args.AddRange(["-i", input]);
        if (length is { } l)
        {
            args.AddRange(["-t", Seconds(l)]);
        }

        args.AddRange(["-map", "0:v?", "-map", "0:a?", "-c", "copy", "-avoid_negative_ts", "make_zero", "-movflags", "+faststart", output]);
        return RunAsync(args, ct);
    }

    public async Task RunAsync(IEnumerable<string> arguments, CancellationToken ct = default)
    {
        var info = new ProcessStartInfo(ExecutablePath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info) ?? throw new FfmpegException("Could not start ffmpeg.");
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (stderr)
                {
                    stderr.AppendLine(e.Data);
                }
            }
        };
        process.OutputDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            throw;
        }

        if (process.ExitCode != 0)
        {
            throw new FfmpegException($"ffmpeg failed with exit code {process.ExitCode}: {stderr.ToString().Trim()}");
        }
    }

    private static string Seconds(TimeSpan value) => value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}
