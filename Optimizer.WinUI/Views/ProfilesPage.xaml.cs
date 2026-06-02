using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Optimizer.WinUI.Views;

public sealed partial class ProfilesPage : Page
{
    public ProfilesViewModel ViewModel { get; }

    public ProfilesPage()
    {
        ViewModel = App.GetService<ProfilesViewModel>();
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
        UpdateEmptyState();
    }

    // ── Presets ────────────────────────────────────────────────────────────

    private async void PresetApply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string presetId)
        {
            var preset = ViewModel.Presets.FirstOrDefault(p => p.Id == presetId);
            if (preset is not null)
                await ViewModel.ApplyPresetCommand.ExecuteAsync(preset);
        }
    }

    // ── Snapshots ──────────────────────────────────────────────────────────

    private async void SnapshotApply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string snapshotId)
        {
            var snapshot = ViewModel.Snapshots.FirstOrDefault(s => s.Id == snapshotId);
            if (snapshot is not null)
            {
                await ViewModel.ApplySnapshotCommand.ExecuteAsync(snapshot);
                UpdateEmptyState();
            }
        }
    }

    private async void SnapshotUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string snapshotId)
        {
            var snapshot = ViewModel.Snapshots.FirstOrDefault(s => s.Id == snapshotId);
            if (snapshot is null) return;

            var dialog = new ContentDialog
            {
                Title = "Update Snapshot",
                Content = $"Refresh \"{snapshot.Name}\" to capture the currently active optimizations?",
                PrimaryButtonText = "Update",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.UpdateSnapshotCommand.ExecuteAsync(snapshot);
                UpdateEmptyState();
            }
        }
    }

    private async void SnapshotDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string snapshotId)
        {
            var snapshot = ViewModel.Snapshots.FirstOrDefault(s => s.Id == snapshotId);
            if (snapshot is null) return;

            var dialog = new ContentDialog
            {
                Title = "Delete Snapshot",
                Content = $"Are you sure you want to delete \"{snapshot.Name}\"? This cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                ViewModel.DeleteSnapshotCommand.Execute(snapshot);
                UpdateEmptyState();
            }
        }
    }

    // ── Header button actions ──────────────────────────────────────────────

    private async void SaveSnapshot_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            PlaceholderText = "e.g. Gaming Setup, Work Mode",
            MinWidth = 280
        };

        var dialog = new ContentDialog
        {
            Title = "Save Snapshot",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Captures all currently active optimizations as a named snapshot.",
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                    },
                    nameBox
                }
            },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var name = nameBox.Text.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                await ViewModel.SaveSnapshotCommand.ExecuteAsync(name);
                UpdateEmptyState();
            }
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add(".json");

        // Associate picker with the window handle
        var hwnd = WindowNative.GetWindowHandle(App.GetService<MainWindow>());
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await ViewModel.ImportCommand.ExecuteAsync(file.Path);
            UpdateEmptyState();
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExportCommand.ExecuteAsync(null);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void UpdateEmptyState()
    {
        if (SnapshotsEmptyState is not null)
            SnapshotsEmptyState.Visibility = ViewModel.Snapshots.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }
}
