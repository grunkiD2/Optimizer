using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Services.Cloud;
using Optimizer.WinUI.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Optimizer.WinUI.Views;

/// <summary>
/// "Extensions" — the merged Marketplace + Plugins destination from the Extend hub.
/// Hosts <see cref="ViewModel"/> (MarketplaceViewModel, drives the community profiles
/// panel) and <see cref="PluginsVM"/> (PluginsViewModel, drives the plugins panel).
/// The in-page <c>Segmented</c> switches between them — one third-party discovery surface.
/// </summary>
public sealed partial class MarketplacePage : Page
{
    public MarketplaceViewModel ViewModel { get; }
    public PluginsViewModel PluginsVM { get; }

    public MarketplacePage()
    {
        ViewModel = App.GetService<MarketplaceViewModel>();
        PluginsVM = App.GetService<PluginsViewModel>();
        InitializeComponent();
        ViewModel.Entries.CollectionChanged += (_, _) => UpdateCountText();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        await PluginsVM.RefreshInstalledCommand.ExecuteAsync(null);
        await PluginsVM.LoadAvailableCommand.ExecuteAsync(null);
    }

    // ── Section switcher ─────────────────────────────────────────────────────

    private void Section_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PanelProfiles is null) return;
        var i = SectionSeg.SelectedIndex;
        PanelProfiles.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelPlugins.Visibility  = i == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Panel A: Profiles (Marketplace) ──────────────────────────────────────

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

    private async void SubmitProfile_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = "Profile name (max 80 chars)", MaxLength = 80 };
        var descBox = new TextBox { PlaceholderText = "Description (max 500 chars)", MaxLength = 500, AcceptsReturn = true, Height = 80 };
        var categoryBox = new TextBox { PlaceholderText = "Category (e.g. Gaming, Productivity)" };
        var tagsBox = new TextBox { PlaceholderText = "Tags (comma-separated, e.g. fps,performance)" };
        var optsBox = new TextBox { PlaceholderText = "Optimization IDs (comma-separated)" };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Name" });
        panel.Children.Add(nameBox);
        panel.Children.Add(new TextBlock { Text = "Description" });
        panel.Children.Add(descBox);
        panel.Children.Add(new TextBlock { Text = "Category" });
        panel.Children.Add(categoryBox);
        panel.Children.Add(new TextBlock { Text = "Tags" });
        panel.Children.Add(tagsBox);
        panel.Children.Add(new TextBlock { Text = "Optimization IDs" });
        panel.Children.Add(optsBox);

        var submitDialog = new ContentDialog
        {
            Title = "Submit Profile",
            Content = new ScrollViewer { Content = panel, MaxHeight = 400 },
            PrimaryButtonText = "Submit",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await submitDialog.ShowAsync() != ContentDialogResult.Primary) return;

        var name = nameBox.Text.Trim();
        var desc = descBox.Text.Trim();
        var category = categoryBox.Text.Trim();
        var tags = tagsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var opts = optsBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        if (string.IsNullOrWhiteSpace(name) || opts.Count == 0)
        {
            var validationDialog = new ContentDialog
            {
                Title = "Validation Error",
                Content = "Name and at least one Optimization ID are required.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };
            await validationDialog.ShowAsync();
            return;
        }

        var submission = new MarketplaceSubmission(name, desc, category, tags, opts);
        await ViewModel.SubmitProfileCommand.ExecuteAsync(submission);

        var successDialog = new ContentDialog
        {
            Title = "Submission Received",
            Content = "Your profile has been submitted and is awaiting moderation.",
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await successDialog.ShowAsync();
    }

    // ── Panel B: Plugins ─────────────────────────────────────────────────────

    private async void PluginToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle) return;
        var pluginId = toggle.Tag as string;
        if (pluginId == null) return;

        var vm = PluginsVM.Installed.FirstOrDefault(p => p.PluginId == pluginId);
        if (vm == null) return;

        await PluginsVM.ToggleInstalledCommand.ExecuteAsync(vm);
    }

    private async void RemovePlugin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string pluginId) return;

        var vm = PluginsVM.Installed.FirstOrDefault(p => p.PluginId == pluginId);
        if (vm == null) return;

        var dialog = new ContentDialog
        {
            Title             = $"Remove '{vm.Name}'?",
            Content           = "The plugin will be uninstalled and its manifest deleted. This cannot be undone.",
            PrimaryButtonText = "Remove",
            CloseButtonText   = "Cancel",
            DefaultButton     = ContentDialogButton.Close,
            XamlRoot          = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await PluginsVM.RemoveInstalledCommand.ExecuteAsync(vm);
    }

    private async void InstallPlugin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string pluginId) return;

        var outcome = await PluginsVM.PrepareInstallAsync(pluginId);

        if (!outcome.Success)
        {
            await ShowErrorAsync("Install Failed", outcome.ErrorMessage ?? "Unknown error.");
            return;
        }

        var changeLines = outcome.Changes != null && outcome.Changes.Count > 0
            ? string.Join("\n", outcome.Changes.Select(c => $"  • {c}"))
            : "  (no changes listed)";

        string dialogTitle;
        string dialogContent;
        bool proceed;

        if (outcome.VerificationResult?.Verified == true)
        {
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

        var ok = await PluginsVM.CompleteInstallAsync(outcome.Detail!);
        if (!ok)
        {
            await ShowErrorAsync("Install Failed",
                "The plugin could not be installed. It may have an invalid manifest or a duplicate ID.");
        }
    }

    private async void SubmitPlugin_Click(object sender, RoutedEventArgs e)
    {
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

        var outcome = await PluginsVM.SubmitPluginAsync(yaml);

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

    private async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title           = title,
            Content         = message,
            CloseButtonText = "OK",
            XamlRoot        = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
