using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Optimizer.WinUI.Views;

public sealed partial class PluginsPage : Page
{
    public PluginsViewModel ViewModel { get; }

    public PluginsPage()
    {
        ViewModel = App.GetService<PluginsViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshInstalledCommand.ExecuteAsync(null);
        await ViewModel.LoadAvailableCommand.ExecuteAsync(null);
    }

    // ── Installed section ─────────────────────────────────────────────────────

    private async void PluginToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle) return;
        var pluginId = toggle.Tag as string;
        if (pluginId == null) return;

        var vm = ViewModel.Installed.FirstOrDefault(p => p.PluginId == pluginId);
        if (vm == null) return;

        await ViewModel.ToggleInstalledCommand.ExecuteAsync(vm);
    }

    private async void RemovePlugin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string pluginId) return;

        var vm = ViewModel.Installed.FirstOrDefault(p => p.PluginId == pluginId);
        if (vm == null) return;

        var dialog = new ContentDialog
        {
            Title           = $"Remove '{vm.Name}'?",
            Content         = "The plugin will be uninstalled and its manifest deleted. This cannot be undone.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton   = ContentDialogButton.Close,
            XamlRoot        = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.RemoveInstalledCommand.ExecuteAsync(vm);
    }

    // ── Available section ─────────────────────────────────────────────────────

    private async void InstallPlugin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string pluginId) return;

        var outcome = await ViewModel.PrepareInstallAsync(pluginId);

        if (!outcome.Success)
        {
            await ShowErrorAsync("Install Failed", outcome.ErrorMessage ?? "Unknown error.");
            return;
        }

        // Build change list text for the warning dialog
        var changeLines = outcome.Changes != null && outcome.Changes.Count > 0
            ? string.Join("\n", outcome.Changes.Select(c => $"  • {c}"))
            : "  (no changes listed)";

        string dialogTitle;
        string dialogContent;
        bool proceed;

        if (outcome.VerificationResult?.Verified == true)
        {
            // Verified — still show change list for transparency
            dialogTitle   = $"Install '{outcome.Detail!.Name}'?";
            dialogContent = $"This plugin is verified by the Optimizer team.\n\nChanges it will make:\n{changeLines}";

            var dialog = new ContentDialog
            {
                Title             = dialogTitle,
                Content           = dialogContent,
                PrimaryButtonText = "Install",
                CloseButtonText   = "Cancel",
                DefaultButton     = ContentDialogButton.Primary,
                XamlRoot          = XamlRoot
            };
            proceed = await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        else
        {
            // Unsigned or invalid signature — strong warning
            var reason = outcome.VerificationResult?.Reason ?? "unknown";
            dialogTitle   = $"Unverified Plugin: '{outcome.Detail!.Name}'";
            dialogContent = $"Warning: This plugin is NOT signed by the Optimizer team ({reason}).\n\n" +
                            $"Only install plugins you trust. Changes it will make:\n{changeLines}";

            var dialog = new ContentDialog
            {
                Title             = dialogTitle,
                Content           = dialogContent,
                PrimaryButtonText = "Install Anyway",
                CloseButtonText   = "Cancel",
                DefaultButton     = ContentDialogButton.Close,
                XamlRoot          = XamlRoot
            };
            proceed = await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        if (!proceed) return;

        var ok = await ViewModel.CompleteInstallAsync(outcome.Detail!);
        if (!ok)
        {
            await ShowErrorAsync("Install Failed",
                "The plugin could not be installed. It may have an invalid manifest or a duplicate ID.");
        }
    }

    // ── Submit section ────────────────────────────────────────────────────────

    private async void SubmitPlugin_Click(object sender, RoutedEventArgs e)
    {
        // File picker — choose a manifest file
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".yaml");
        picker.FileTypeFilter.Add(".yml");
        picker.FileTypeFilter.Add(".json");

        var hwnd = WindowNative.GetWindowHandle(App.GetService<MainWindow>());
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        string yaml;
        try
        {
            yaml = await Windows.Storage.FileIO.ReadTextAsync(file);
        }
        catch
        {
            await ShowErrorAsync("Read Error", "Could not read the selected file.");
            return;
        }

        var outcome = await ViewModel.SubmitPluginAsync(yaml);

        var resultDialog = new ContentDialog
        {
            Title             = outcome.Success ? "Submission Received" : "Submission Failed",
            Content           = outcome.Success
                                    ? "Your plugin has been submitted and is awaiting moderation by the Optimizer team."
                                    : $"Could not submit: {outcome.ErrorMessage}",
            CloseButtonText   = "OK",
            XamlRoot          = XamlRoot
        };
        await resultDialog.ShowAsync();
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title         = title,
            Content       = message,
            CloseButtonText = "OK",
            XamlRoot      = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
