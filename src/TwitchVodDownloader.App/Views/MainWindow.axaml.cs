using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TwitchVodDownloader.App.ViewModels;

namespace TwitchVodDownloader.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.PickFolder = PickFolderAsync;
            }
        };
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where to save downloads",
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
