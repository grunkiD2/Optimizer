using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class MarketplacePage : Page
{
    public MarketplaceViewModel ViewModel { get; }

    public MarketplacePage()
    {
        ViewModel = App.GetService<MarketplaceViewModel>();
        InitializeComponent();
        ViewModel.Entries.CollectionChanged += (_, _) => UpdateCountText();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
        => await ViewModel.LoadAsync();

    private void UpdateCountText()
    {
        CountText.Text = ViewModel.Entries.Count > 0
            ? $"{ViewModel.Entries.Count} profile(s) available"
            : "";
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;

        var entry = ViewModel.Entries.FirstOrDefault(x => x.Id == id);
        if (entry is null) return;

        var dialog = new ContentDialog
        {
            Title = $"Install '{entry.Name}'?",
            Content = $"This will apply {entry.Optimizations.Count} optimization(s) to your system.",
            PrimaryButtonText = "Install",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.InstallCommand.ExecuteAsync(entry);
    }
}
