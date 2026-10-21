using System.Globalization;
using TwitchVodDownloader.Core.Util;

namespace TwitchVodDownloader.Cli;

internal sealed class CliOptions
{
    public string Command { get; private set; } = "help";

    public List<string> Links { get; } = [];

    public string? Output { get; private set; }

    public string? Quality { get; private set; }

    public TimeSpan? Start { get; private set; }

    public TimeSpan? End { get; private set; }

    public int Threads { get; private set; } = 8;

    public string? AuthToken { get; private set; } = Environment.GetEnvironmentVariable("TVD_AUTH_TOKEN");

    public string? FfmpegPath { get; private set; }

    public string? TempDirectory { get; private set; }

    public bool H264Only { get; private set; }

    public bool Chat { get; private set; }

    public bool ChatOnly { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        if (args.Length == 0)
        {
            return options;
        }

        options.Command = args[0].ToLowerInvariant();
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new CliException($"{arg} needs a value.");

            switch (arg)
            {
                case "-o" or "--output":
                    options.Output = Next();
                    break;
                case "-q" or "--quality":
                    options.Quality = Next();
                    break;
                case "-s" or "--start":
                    options.Start = ParseTime(arg, Next());
                    break;
                case "-e" or "--end":
                    options.End = ParseTime(arg, Next());
                    break;
                case "-t" or "--threads":
                    options.Threads = int.TryParse(Next(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n is > 0 and <= 64
                        ? n
                        : throw new CliException("--threads must be between 1 and 64.");
                    break;
                case "--auth-token":
                    options.AuthToken = Next();
                    break;
                case "--ffmpeg":
                    options.FfmpegPath = Next();
                    break;
                case "--temp":
                    options.TempDirectory = Next();
                    break;
                case "--chat":
                    options.Chat = true;
                    break;
                case "--chat-only":
                    options.ChatOnly = true;
                    break;
                case "--h264-only":
                    options.H264Only = true;
                    break;
                case "-b" or "--batch":
                    var file = Next();
                    if (!File.Exists(file))
                    {
                        throw new CliException($"Batch file '{file}' does not exist.");
                    }

                    options.Links.AddRange(File.ReadLines(file)
                        .Select(l => l.Trim())
                        .Where(l => l.Length > 0 && !l.StartsWith('#')));
                    break;
                case "-h" or "--help":
                    options.Command = "help";
                    break;
                default:
                    if (arg.StartsWith('-') && arg.Length > 1)
                    {
                        throw new CliException($"Unknown option '{arg}'. Run `tvd help` for usage.");
                    }

                    options.Links.Add(arg);
                    break;
            }
        }

        return options;
    }

    private static TimeSpan ParseTime(string option, string value) =>
        TimeParser.TryParse(value, out var time)
            ? time
            : throw new CliException($"{option}: could not read '{value}' as a time. Try 1:02:03 or 1h2m3s.");
}

internal sealed class CliException(string message) : Exception(message);
