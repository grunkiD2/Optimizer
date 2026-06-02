using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Optimizer.WinUI.ViewModels;
using Windows.UI;

namespace Optimizer.WinUI.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();

        // Sync the color picker to the loaded accent color
        if (TryParseHexColor(ViewModel.AccentColorHex, out var color))
            AccentColorPicker.Color = color;
    }

    private async void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Reset Settings",
            Content = "Are you sure? All settings will be restored to defaults.",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.ResetSettingsCommand.Execute(null);
            // Re-sync color picker after reset
            if (TryParseHexColor(ViewModel.AccentColorHex, out var color))
                AccentColorPicker.Color = color;
        }
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Clear History",
            Content = "All recorded optimization events will be permanently deleted. This cannot be undone.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.ClearHistoryCommand.Execute(null);
    }

    private void AccentColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        var c = args.NewColor;
        ViewModel.AccentColorHex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private void CopyToken_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CopyApiTokenCommand.Execute(null);
    }

    private async void RegenerateToken_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Regenerate API Token",
            Content = "This will invalidate the current token. Any connected clients will need to update their token. Continue?",
            PrimaryButtonText = "Regenerate",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.RegenerateTokenCommand.ExecuteAsync(null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool TryParseHexColor(string? hex, out Color color)
    {
        color = Colors.Blue;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        hex = hex.TrimStart('#');
        if (hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            color = Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            return true;
        }
        return false;
    }
}
