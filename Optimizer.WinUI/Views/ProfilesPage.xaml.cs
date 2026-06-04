using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.IO;

namespace Optimizer.WinUI.Views;

/// <summary>
/// "Profiles" — the merged Profiles + Templates destination from the Automate hub.
/// Hosts both <see cref="ViewModel"/> (ProfilesViewModel, drives presets + snapshots +
/// auto-switching rules) and <see cref="TemplatesVM"/> (TemplatesViewModel, drives the
/// DSC/Intune/WinGet template list). An in-page Segmented switches between them.
/// </summary>
public sealed partial class ProfilesPage : Page
{
    public ProfilesViewModel ViewModel { get; }
    public TemplatesViewModel TemplatesVM { get; }

    /// <summary>0 = Profiles &amp; Rules, 1 = Templates. See HubPage hub-aware navigation.</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is int idx && SectionSeg is not null
            && idx >= 0 && idx < SectionSeg.Items.Count)
        {
            SectionSeg.SelectedIndex = idx;
        }
    }

    public ProfilesPage()
    {
        ViewModel   = App.GetService<ProfilesViewModel>();
        TemplatesVM = App.GetService<TemplatesViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
        UpdateEmptyStates();
        await TemplatesVM.LoadCommand.ExecuteAsync(null);
    }

    // ── Section switcher ─────────────────────────────────────────────────────

    private void Section_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PanelProfiles is null) return;
        var i = SectionSeg.SelectedIndex;
        PanelProfiles.Visibility  = i == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelTemplates.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
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
                UpdateEmptyStates();
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
                UpdateEmptyStates();
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
                UpdateEmptyStates();
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
                UpdateEmptyStates();
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

            var hwnd = WindowNative.GetWindowHandle(App.GetService<MainWindow>());
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

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

            if (string.IsNullOrWhiteSpace(json) || (!json.TrimStart().StartsWith('[') && !json.TrimStart().StartsWith('{')))
            {
                await ShowErrorDialogAsync("Import Failed", "The selected file does not appear to be a valid JSON profile export.");
                return;
            }

            await ViewModel.ImportCommand.ExecuteAsync(file.Path);
            UpdateEmptyStates();

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

    // ── Automation Rules ──────────────────────────────────────────────────

    private async void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = "Rule name (e.g. Gaming Mode)", MinWidth = 260 };
        var triggerCombo = new ComboBox { MinWidth = 160, SelectedIndex = 0 };
        triggerCombo.Items.Add("Time Range");
        triggerCombo.Items.Add("Process Running");
        var profileBox = new TextBox { PlaceholderText = "Profile ID (e.g. gaming)" };
        var profileNameBox = new TextBox { PlaceholderText = "Profile display name" };
        var startBox = new TextBox { PlaceholderText = "Start time (HH:MM, e.g. 22:00)", Width = 200 };
        var endBox = new TextBox { PlaceholderText = "End time (HH:MM, e.g. 06:00)", Width = 200 };
        var processBox = new TextBox { PlaceholderText = "Process name (e.g. obs64.exe)", Width = 260 };

        var timePanel = new StackPanel { Spacing = 6 };
        timePanel.Children.Add(new TextBlock { Text = "Time range:", FontSize = 12 });
        timePanel.Children.Add(startBox);
        timePanel.Children.Add(endBox);

        var processPanel = new StackPanel { Spacing = 6, Visibility = Visibility.Collapsed };
        processPanel.Children.Add(new TextBlock { Text = "Process name:", FontSize = 12 });
        processPanel.Children.Add(processBox);

        triggerCombo.SelectionChanged += (_, _) =>
        {
            timePanel.Visibility = triggerCombo.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            processPanel.Visibility = triggerCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        };

        var formPanel = new StackPanel { Spacing = 10, MinWidth = 280 };
        formPanel.Children.Add(new TextBlock { Text = "Rule Name:", FontSize = 12 });
        formPanel.Children.Add(nameBox);
        formPanel.Children.Add(new TextBlock { Text = "Trigger:", FontSize = 12 });
        formPanel.Children.Add(triggerCombo);
        formPanel.Children.Add(timePanel);
        formPanel.Children.Add(processPanel);
        formPanel.Children.Add(new TextBlock { Text = "Target Profile ID:", FontSize = 12 });
        formPanel.Children.Add(profileBox);
        formPanel.Children.Add(new TextBlock { Text = "Profile Display Name:", FontSize = 12 });
        formPanel.Children.Add(profileNameBox);

        var dialog = new ContentDialog
        {
            Title = "Add Auto-Switching Rule",
            Content = new ScrollViewer { Content = formPanel, MaxHeight = 480 },
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var ruleName = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(ruleName)) return;

        var rule = new ProfileRule
        {
            Name = ruleName,
            ProfileId = profileBox.Text.Trim(),
            ProfileName = profileNameBox.Text.Trim(),
            Trigger = triggerCombo.SelectedIndex == 0 ? RuleTrigger.TimeRange : RuleTrigger.ProcessRunning
        };

        if (rule.Trigger == RuleTrigger.TimeRange)
        {
            rule.StartTime = TryParseTime(startBox.Text);
            rule.EndTime = TryParseTime(endBox.Text);
        }
        else
        {
            rule.ProcessName = processBox.Text.Trim();
        }

        await ViewModel.AddRuleAsync(rule);
        UpdateEmptyStates();
    }

    private async void RuleDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string ruleId)
        {
            var rule = ViewModel.AutomationRules.FirstOrDefault(r => r.Id == ruleId);
            if (rule is null) return;

            var dialog = new ContentDialog
            {
                Title = "Delete Rule",
                Content = $"Delete rule \"{rule.Name}\"?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteRuleCommand.ExecuteAsync(rule);
                UpdateEmptyStates();
            }
        }
    }

    private async void RuleToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton btn && btn.Tag is string ruleId)
        {
            var rule = ViewModel.AutomationRules.FirstOrDefault(r => r.Id == ruleId);
            if (rule is not null)
                await ViewModel.ToggleRuleCommand.ExecuteAsync(rule);
        }
    }

    private void RuleDetail_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock tb || tb.Tag is not string ruleId) return;
        var rule = ViewModel.AutomationRules.FirstOrDefault(r => r.Id == ruleId);
        if (rule is null) return;

        tb.Text = rule.Trigger == RuleTrigger.TimeRange
            ? $"{rule.StartTime:hh\\:mm} – {rule.EndTime:hh\\:mm}"
            : $"When process running: {rule.ProcessName}";
    }

    // ── Templates (formerly TemplatesPage) ─────────────────────────────────

    private async void TemplatesRefresh_Click(object sender, RoutedEventArgs e)
        => await TemplatesVM.LoadCommand.ExecuteAsync(null);

    private async void CreateTemplate_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 8, Width = 360 };
        var tbName = new TextBox
        {
            Header          = "Template name",
            PlaceholderText = "My Optimizer Config"
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
            TemplatesVM.NewTemplateName        = tbName.Text.Trim();
            TemplatesVM.NewTemplateDescription = tbDesc.Text.Trim();
            await TemplatesVM.CreateTemplateCommand.ExecuteAsync(null);
        }
    }

    private async void ExportDsc_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ConfigTemplate t)
            await TemplatesVM.ExportDscCommand.ExecuteAsync(t);
    }

    private async void ExportIntune_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ConfigTemplate t)
            await TemplatesVM.ExportIntuneCommand.ExecuteAsync(t);
    }

    private async void ExportWinget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ConfigTemplate t)
            await TemplatesVM.ExportWingetCommand.ExecuteAsync(t);
    }

    private async void TemplateDelete_Click(object sender, RoutedEventArgs e)
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
            await TemplatesVM.DeleteTemplateCommand.ExecuteAsync(id);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void UpdateEmptyStates()
    {
        if (SnapshotsEmptyState is not null)
            SnapshotsEmptyState.Visibility = ViewModel.Snapshots.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (RulesEmptyState is not null)
            RulesEmptyState.Visibility = ViewModel.AutomationRules.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private static TimeSpan TryParseTime(string input)
    {
        if (TimeSpan.TryParseExact(input.Trim(), @"hh\:mm", null, out var ts)) return ts;
        if (TimeSpan.TryParseExact(input.Trim(), @"h\:mm", null, out ts)) return ts;
        return TimeSpan.Zero;
    }
}
