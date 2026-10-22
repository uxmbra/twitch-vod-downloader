using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwitchVodDownloader.App.Services;

public sealed class AppSettings
{
    public string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Twitch");

    public string Quality { get; set; } = "Best";

    public bool IncludeVideo { get; set; } = true;

    public bool IncludeChat { get; set; }

    public int SegmentThreads { get; set; } = 8;

    public int ConcurrentDownloads { get; set; } = 2;

    public bool H264Only { get; set; }

    public string? FfmpegPath { get; set; }

    /// <summary>Stored encrypted for the current Windows user; see <see cref="SettingsStore"/>.</summary>
    public string? AuthToken { get; set; }
}

/// <summary>Reads and writes settings as JSON in %APPDATA%\TwitchVodDownloader.</summary>
public sealed class SettingsStore(string? directory = null)
{
    private const string ProtectedPrefix = "dpapi:";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path = Path.Combine(
        directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TwitchVodDownloader"),
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
            settings.AuthToken = Unprotect(settings.AuthToken);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or CryptographicException)
        {
            // A damaged settings file should never stop the app from opening.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
            copy.AuthToken = Protect(settings.AuthToken);
            File.WriteAllText(_path, JsonSerializer.Serialize(copy, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; failing to save them is not worth interrupting a download.
        }
    }

    // The auth token grants access to the user's Twitch account, so on Windows it is encrypted
    // with DPAPI and only readable by the same Windows user on the same machine.
    private static string? Protect(string? value)
    {
        if (string.IsNullOrEmpty(value) || !OperatingSystem.IsWindows())
        {
            return value;
        }

        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        return ProtectedPrefix + Convert.ToBase64String(bytes);
    }

    private static string? Unprotect(string? value)
    {
        if (value is null || !value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
        {
            return value;
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value[ProtectedPrefix.Length..]), null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
