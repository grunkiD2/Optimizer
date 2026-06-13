using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace Optimizer.WinUI.Helpers;

public static class DialogHelper
{
    public static async Task<bool> ConfirmAsync(
        XamlRoot root,
        string title,
        string content,
        string actionLabel = "Apply")
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = actionLabel,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Single-button informational dialog (used by list-row context menus, Batch 3).</summary>
    public static async Task InfoAsync(XamlRoot root, string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "OK",
            XamlRoot = root
        };
        await dialog.ShowAsync();
    }
}
