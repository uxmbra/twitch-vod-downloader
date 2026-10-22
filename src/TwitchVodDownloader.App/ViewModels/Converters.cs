using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TwitchVodDownloader.App.ViewModels;

public static class Converters
{
    public static readonly IValueConverter FailedBrush = new FuncValueConverter<bool, IBrush>(
        failed => failed ? new SolidColorBrush(Color.Parse("#FF6B6B")) : new SolidColorBrush(Color.Parse("#A0A0AB")));
}
