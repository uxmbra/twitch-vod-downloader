using System.Diagnostics;

namespace TwitchVodDownloader.App.Services;

internal static class Shell
{
    /// <summary>Opens a folder in Explorer, or a file in its default app.</summary>
    public static void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    /// <summary>Opens Explorer with the file selected (or the folder elsewhere).</summary>
    public static void RevealFile(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path }, UseShellExecute = false });
            }
            else
            {
                Process.Start(new ProcessStartInfo(Path.GetDirectoryName(path)!) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing useful to do if the shell refuses.
        }
    }
}
