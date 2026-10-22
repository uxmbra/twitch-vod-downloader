using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TwitchVodDownloader.App.Services;
using TwitchVodDownloader.App.ViewModels;
using TwitchVodDownloader.App.Views;

namespace TwitchVodDownloader.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new SettingsStore();
            var viewModel = new MainWindowViewModel(store, store.Load());
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.ShutdownRequested += (_, _) => viewModel.Shutdown();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
