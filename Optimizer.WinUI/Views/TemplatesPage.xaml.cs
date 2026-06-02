using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class TemplatesPage : Page
{
    public TemplatesViewModel ViewModel { get; }

    public TemplatesPage()
    {
        ViewModel = App.GetService<TemplatesViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
        => await ViewModel.LoadCommand.ExecuteAsync(null);

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await ViewModel.LoadCommand.ExecuteAsync(null);

    // ── Create template dialog ────────────────────────────────────────────────

    private async void CreateTemplate_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 8, Width = 360 };
        var tbName = new TextBox
        {
            Header            = "Template name",
            PlaceholderText   = "My Optimizer Config"
        };
        var tbDesc = new TextBox
        {
            Header          = "Description (optional)",
            PlaceholderText = "Security + privacy hardening settings"
        };
        panel.Children.Add(tbName);
        panel.Children.Add(tbDesc);

        var dialog = new ContentDialog
        {
            Title             = "Create Configuration Template",
            Content           = panel,
            PrimaryButtonText = "Create",
            CloseButtonText   = "Cancel",
            DefaultButton     = ContentDialogButton.Primary,
            XamlRoot          = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.NewTemplateName        = tbName.Text.Trim();
            ViewModel.NewTemplateDescription = tbDesc.Text.Trim();
            await ViewModel.CreateTemplateCommand.ExecuteAsync(null);
        }
    }

    // ── Export buttons ────────────────────────────────────────────────────────

    private async void ExportDsc_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ConfigTemplate t)
            await ViewModel.ExportDscCommand.ExecuteAsync(t);
    }

    private async void ExportIntune_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ConfigTemplate t)
            await ViewModel.ExportIntuneCommand.ExecuteAsync(t);
    }

    private async void ExportWinget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ConfigTemplate t)
            await ViewModel.ExportWingetCommand.ExecuteAsync(t);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;

        var dialog = new ContentDialog
        {
            Title             = "Delete Template?",
            Content           = "This will permanently remove the template.",
            PrimaryButtonText = "Delete",
            CloseButtonText   = "Cancel",
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteTemplateCommand.ExecuteAsync(id);
    }
}
