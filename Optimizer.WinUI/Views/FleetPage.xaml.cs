using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.ViewModels;
using Windows.Storage.Pickers;

namespace Optimizer.WinUI.Views;

public sealed partial class FleetPage : Page
{
    public FleetViewModel ViewModel { get; }

    public FleetPage()
    {
        ViewModel = App.GetService<FleetViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
        => await ViewModel.LoadCommand.ExecuteAsync(null);

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await ViewModel.LoadCommand.ExecuteAsync(null);

    private async void PingAll_Click(object sender, RoutedEventArgs e)
        => await ViewModel.PingAllCommand.ExecuteAsync(null);

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
        => await ViewModel.ExportCsvCommand.ExecuteAsync(null);

    // ── Import CSV via file picker ────────────────────────────────────────────

    private async void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(
            App.GetService<MainWindow>());
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".csv");
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        await ViewModel.ImportCsvCommand.ExecuteAsync(file.Path);
    }

    // ── Ping single machine ───────────────────────────────────────────────────

    private async void Ping_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is FleetMachine machine)
            await ViewModel.PingMachineCommand.ExecuteAsync(machine);
    }

    // ── Delete machine ────────────────────────────────────────────────────────

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string name) return;

        var dialog = new ContentDialog
        {
            Title             = "Remove Machine?",
            Content           = $"Remove '{name}' from the fleet roster?",
            PrimaryButtonText = "Remove",
            CloseButtonText   = "Cancel",
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteMachineCommand.ExecuteAsync(name);
    }

    // ── Add machine dialog ────────────────────────────────────────────────────

    private async void AddMachine_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 8, Width = 320 };
        var tbName       = new TextBox { Header = "Display name",  PlaceholderText = "DESKTOP-01" };
        var tbHost       = new TextBox { Header = "Hostname / IP", PlaceholderText = "DESKTOP-01 or 192.168.1.10" };
        var tbDepartment = new TextBox { Header = "Department",    PlaceholderText = "IT" };
        var tbOwner      = new TextBox { Header = "Owner",         PlaceholderText = "Jane Doe" };
        panel.Children.Add(tbName);
        panel.Children.Add(tbHost);
        panel.Children.Add(tbDepartment);
        panel.Children.Add(tbOwner);

        var dialog = new ContentDialog
        {
            Title             = "Add Machine",
            Content           = panel,
            PrimaryButtonText = "Add",
            CloseButtonText   = "Cancel",
            DefaultButton     = ContentDialogButton.Primary,
            XamlRoot          = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.NewName       = tbName.Text.Trim();
            ViewModel.NewHostName   = tbHost.Text.Trim();
            ViewModel.NewDepartment = tbDepartment.Text.Trim();
            ViewModel.NewOwner      = tbOwner.Text.Trim();
            await ViewModel.AddMachineCommand.ExecuteAsync(null);
        }
    }
}
