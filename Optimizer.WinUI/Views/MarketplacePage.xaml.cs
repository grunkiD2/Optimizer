using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Services.Cloud;
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
}
