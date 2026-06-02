using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.IO;

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
                        TextWrapping = TextWrapping.Wrap
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
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add(".json");

            // WinUI 3 requires an HWND to be associated before showing the picker.
            var hwnd = WindowNative.GetWindowHandle(App.GetService<MainWindow>());
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;   // user cancelled — not an error

            string json;
            try
            {
                json = await File.ReadAllTextAsync(file.Path);
            }
            catch (Exception ioEx)
            {
                await ShowErrorDialogAsync("Import Failed", $"Could not read the file:\n{ioEx.Message}");
                return;
            }

            // Basic JSON validation before handing off to the service.
            if (string.IsNullOrWhiteSpace(json) || (!json.TrimStart().StartsWith('[') && !json.TrimStart().StartsWith('{')))
            {
                await ShowErrorDialogAsync("Import Failed", "The selected file does not appear to be a valid JSON profile export.");
                return;
            }

            await ViewModel.ImportCommand.ExecuteAsync(file.Path);
            UpdateEmptyState();

            // If the ViewModel set an error status, surface it as a dialog too.
            if (ViewModel.StatusMessage.StartsWith("Import failed", StringComparison.OrdinalIgnoreCase))
            {
                await ShowErrorDialogAsync("Import Failed", ViewModel.StatusMessage);
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync("Import Failed", $"An unexpected error occurred:\n{ex.Message}");
        }
    }

    private async Task ShowErrorDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
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
