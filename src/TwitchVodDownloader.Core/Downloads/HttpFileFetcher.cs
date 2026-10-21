using System.Net;

namespace TwitchVodDownloader.Core.Downloads;

/// <summary>Downloads a URL to a file with retries, because segment CDNs drop the odd request.</summary>
internal static class HttpFileFetcher
{
    private const int MaxAttempts = 5;
    private const int BufferSize = 81920;

    public static async Task<long> FetchAsync(
        HttpClient http,
        Uri uri,
        string destination,
        Action<long>? onBytes,
        CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            long written = 0;
            try
            {
                using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);

                // Twitch lists some muted VOD segments as "-unmuted", which 403s; the "-muted" copy works.
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                    && TryMutedFallback(uri, out var fallback))
                {
                    uri = fallback;
                    attempt--;
                    continue;
                }

                response.EnsureSuccessStatusCode();

                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    written += read;
                    onBytes?.Invoke(read);
                }

                return written;
            }
            catch (Exception ex) when (attempt < MaxAttempts && !ct.IsCancellationRequested && IsTransient(ex))
            {
                // Undo the progress this failed attempt reported so totals stay honest.
                onBytes?.Invoke(-written);
                await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1)), ct);
            }
        }
    }

    internal static bool TryMutedFallback(Uri uri, out Uri fallback)
    {
        var text = uri.ToString();
        var index = text.LastIndexOf("-unmuted", StringComparison.Ordinal);
        if (index < 0)
        {
            fallback = uri;
            return false;
        }

        fallback = new Uri(string.Concat(text.AsSpan(0, index), "-muted", text.AsSpan(index + "-unmuted".Length)));
        return true;
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: { } code } => (int)code >= 500 || code == HttpStatusCode.TooManyRequests,
        IOException => true,
        TaskCanceledException => true, // HttpClient timeout
        _ => false,
    };
}
