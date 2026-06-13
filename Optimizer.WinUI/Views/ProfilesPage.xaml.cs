using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;
using System.IO;

namespace Optimizer.WinUI.Views;

/// <summary>
/// "Profiles" — presets + snapshots + auto-switching rules in the Automate hub,
/// driven by <see cref="ViewModel"/> (ProfilesViewModel).
/// </summary>
public sealed partial class ProfilesPage : Page
{
    public ProfilesViewModel ViewModel { get; }
    private readonly IFancontrolCommandService _fc = App.GetService<IFancontrolCommandService>();

    public ProfilesPage()
    {
        ViewModel = App.GetService<ProfilesViewModel>();
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
        UpdateEmptyStates();
        LoadFancontrolProfiles();
    }

    // ── Presets ────────────────────────────────────────────────────────────

    private async void PresetApply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string presetId) return;
        var preset = ViewModel.Presets.FirstOrDefault(p => p.Id == presetId);
        if (preset is null) return;

        // Safe-Tune gate (audit 4b): if this preset bundles any DESTRUCTIVE optimization, the shared
        // apply path skips it unless we explicitly opt in. Confirm here (the view owns the dialog;
        // the VM stays UI-type-free) and let the user apply everything, apply the rest, or cancel.
        var optimizer = App.GetService<IWindowsOptimizerService>();
        var destructive = preset.Optimizations
            .Select(id => optimizer.GetOptimizationInfo(id))
            .Where(info => info is { IsDestructive: true })
            .ToList();

        if (destructive.Count == 0)
        {
            await ViewModel.ApplyPresetCommand.ExecuteAsync(preset);
            return;
        }

        var list = string.Join("\n", destructive.Select(d => $"• {d!.Title}"));
        var dialog = new ContentDialog
        {
            Title = "This preset makes a destructive change",
            Content = $"\"{preset.Name}\" also runs:\n\n{list}\n\n" +
                      "That removes ALL of those items at once (reversible via Undo). Apply everything, " +
                      "or apply the rest and skip the destructive part?",
            PrimaryButtonText = "Apply everything",
            SecondaryButtonText = "Apply without these",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = XamlRoot
        };

        var choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.Primary)
            await ViewModel.ApplyPresetIncludingDestructiveAsync(preset);
        else if (choice == ContentDialogResult.Secondary)
            await ViewModel.ApplyPresetCommand.ExecuteAsync(preset);
        // Close / Cancel → do nothing
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

    // ── Fancontrol profiles (Profil 2.0 P2.0-c editor) ──────────────────────
    // Lag-2 fields are editable; lag-1 gamingClass is shown read-only with a source badge. Every
    // mutation goes through the ctl bridge (FancontrolCommandService) — never writes profiles.json.

    private static readonly (string Name, string Guid)[] FcPowerPlans =
    {
        ("Balanced", "381b4222-f694-41f0-9685-ff5bb260df2e"),
        ("High Performance", "36531193-92c9-4772-911e-af2fa6f81bb0"),
        ("Power Saver", "a1841308-3541-4fab-bc81-f71556f20b4a"),
    };

    private static string FcPowerName(string guid)
        => FcPowerPlans.FirstOrDefault(p => string.Equals(p.Guid, guid, StringComparison.OrdinalIgnoreCase)).Name ?? "Custom";

    private static Microsoft.UI.Xaml.Media.Brush Res(string key)
        => Application.Current.Resources.TryGetValue(key, out var v) && v is Microsoft.UI.Xaml.Media.Brush b
            ? b : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);

    private void LoadFancontrolProfiles()
    {
        if (FancontrolProfilesPanel is null || FancontrolSection is null) return;
        FancontrolProfilesPanel.Children.Clear();
        if (!_fc.IsConfigured) { FancontrolSection.Visibility = Visibility.Collapsed; return; }
        FancontrolSection.Visibility = Visibility.Visible;

        var profiles = _fc.GetProfiles();
        if (profiles.Count == 0)
        {
            FancontrolProfilesPanel.Children.Add(new TextBlock
            {
                Text = "No Fancontrol profiles found (profiles.json unreadable?).",
                FontSize = 12, Foreground = Res("MutedBrush"), Margin = new Thickness(0, 4, 0, 0)
            });
            return;
        }
        foreach (var p in profiles)
            FancontrolProfilesPanel.Children.Add(BuildFcRow(p));
    }

    private Border BuildFcRow(FancontrolProfile p)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock { Text = p.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        if (p.GamingClass)
        {
            var badge = new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1),
                Background = Res("HudSurfaceBrush"), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "GAMING", FontSize = 9, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Res("VioletBrush") }
            };
            ToolTipService.SetToolTip(badge, "Lag 1 (system-owned): classified gaming by the Fancontrol context authority — read-only here.");
            titleRow.Children.Add(badge);
        }
        info.Children.Add(titleRow);

        var lysText = p.LysMode == "static" && !string.IsNullOrEmpty(p.LysColor) ? $"{p.LysMode} {p.LysColor}" : p.LysMode;
        var details = $"DC 0x{p.Dc:X2} · {p.Bright}% · {(p.Hdr ? "HDR" : "SDR")} · {FcPowerName(p.Power)} · lys {lysText}"
                    + (string.IsNullOrEmpty(p.Lyd) ? "" : $" · audio {p.Lyd}");
        info.Children.Add(new TextBlock { Text = details, FontSize = 11, Foreground = Res("MutedBrush"), TextWrapping = TextWrapping.Wrap });
        info.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(p.Optimizer) ? "no preset link" : $"→ preset: {p.Optimizer}",
            FontSize = 11,
            Foreground = string.IsNullOrEmpty(p.Optimizer) ? Res("MutedBrush") : Res("AccentCyanBrush")
        });
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        Button RowBtn(int col, string text, bool danger, Func<Task> onClick)
        {
            var b = new Button { Content = text, VerticalAlignment = VerticalAlignment.Center };
            if (danger) b.Foreground = Res("DangerBrush");
            b.Click += async (_, _) => await onClick();
            Grid.SetColumn(b, col);
            return b;
        }
        grid.Children.Add(RowBtn(1, "Edit", false, () => FcEditProfile(p)));
        grid.Children.Add(RowBtn(2, "Clone", false, () => FcCloneProfile(p.Name)));
        grid.Children.Add(RowBtn(3, "Rename", false, () => FcRenameProfile(p.Name)));
        grid.Children.Add(RowBtn(4, "Delete", true, () => FcDeleteProfile(p.Name)));

        return new Border
        {
            Background = Res("HudSurfaceAltBrush"), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 10, 16, 10), BorderBrush = Res("HudHairlineBrush"), BorderThickness = new Thickness(1),
            Child = grid
        };
    }

    private void FcRefresh_Click(object sender, RoutedEventArgs e) => LoadFancontrolProfiles();

    private async void FcNewProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = await FcPromptName("New Fancontrol profile", "Profile name (e.g. Movie Night)");
        if (string.IsNullOrEmpty(name)) return;
        await FcRun(_fc.CreateProfileAsync(name));
    }

    private async Task FcCloneProfile(string src)
    {
        var name = await FcPromptName($"Clone '{src}'", "New profile name");
        if (string.IsNullOrEmpty(name)) return;
        await FcRun(_fc.CloneProfileAsync(src, name));
    }

    private async Task FcRenameProfile(string oldName)
    {
        var name = await FcPromptName($"Rename '{oldName}'", "New profile name", oldName);
        if (string.IsNullOrEmpty(name) || name == oldName) return;
        await FcRun(_fc.RenameProfileAsync(oldName, name));
    }

    private async Task FcDeleteProfile(string name)
    {
        var dlg = new ContentDialog
        {
            Title = $"Delete '{name}'?",
            Content = "The Fancontrol engine refuses deletion if the profile is mapped to a program, is the fgwatch fallback, is currently active, or would leave fewer than 5 profiles.",
            PrimaryButtonText = "Delete", CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            await FcRun(_fc.DeleteProfileAsync(name));
    }

    private async Task FcEditProfile(FancontrolProfile p)
    {
        var dc = new TextBox { Text = p.Dc.ToString(), Width = 110 };
        var bright = new TextBox { Text = p.Bright.ToString(), Width = 110 };
        var hdr = new CheckBox { Content = "HDR", IsChecked = p.Hdr };
        var power = new ComboBox { MinWidth = 220 };
        foreach (var pl in FcPowerPlans) power.Items.Add(pl.Name);
        var curPower = FcPowerName(p.Power);
        if (FcPowerPlans.All(x => x.Name != curPower)) power.Items.Add(curPower);
        power.SelectedItem = curPower;
        var lyd = new TextBox { Text = p.Lyd, PlaceholderText = "audio device substring (optional)", MinWidth = 220 };
        var lysMode = new ComboBox { MinWidth = 160 };
        foreach (var m in new[] { "static", "off", "synapse", "ambient" }) lysMode.Items.Add(m);
        lysMode.SelectedItem = new[] { "static", "off", "synapse", "ambient" }.Contains(p.LysMode) ? p.LysMode : "synapse";
        var lysColor = new TextBox { Text = p.LysColor ?? "", PlaceholderText = "#RRGGBB", Width = 130 };
        var optimizer = new ComboBox { MinWidth = 240 };
        const string noPreset = "(no preset)";
        optimizer.Items.Add(noPreset);
        var presets = App.GetService<IProfileService>().BuiltInPresets;
        foreach (var pr in presets) optimizer.Items.Add(pr.Id);
        if (!string.IsNullOrEmpty(p.Optimizer) && presets.All(pr => pr.Id != p.Optimizer)) optimizer.Items.Add(p.Optimizer);
        optimizer.SelectedItem = string.IsNullOrEmpty(p.Optimizer) ? noPreset : p.Optimizer;
        var uiIcon = new TextBox { Text = p.UiIcon, Width = 90 };
        var uiDesc = new TextBox { Text = p.UiDesc, MinWidth = 220 };

        var form = new StackPanel { Spacing = 8, MinWidth = 340 };
        form.Children.Add(new TextBlock
        {
            Text = $"Lag-2 fields of '{p.Name}'. gamingClass (lag-1, system-owned) is read-only here: {(p.GamingClass ? "GAMING" : "not gaming")}.",
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Res("MutedBrush")
        });
        void AddField(string label, FrameworkElement ctl)
        {
            form.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Res("MutedBrush"), Margin = new Thickness(0, 4, 0, 0) });
            form.Children.Add(ctl);
        }
        AddField("GameVisual DC (0-255)", dc);
        AddField("Brightness (0-100)", bright);
        form.Children.Add(hdr);
        AddField("Power plan", power);
        AddField("Audio device (lyd)", lyd);
        AddField("Lighting mode", lysMode);
        AddField("Lighting color (static)", lysColor);
        AddField("Optimizer preset-link (auto-applied on switch)", optimizer);
        AddField("Icon", uiIcon);
        AddField("Description", uiDesc);

        var dlg = new ContentDialog
        {
            Title = $"Edit — {p.Name}",
            Content = new ScrollViewer { Content = form, MaxHeight = 520 },
            PrimaryButtonText = "Save", CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        if (!int.TryParse(dc.Text.Trim(), out var dcv) || dcv < 0 || dcv > 255)
        { await ShowErrorDialogAsync("Edit", "GameVisual DC must be an integer 0-255."); return; }
        if (!int.TryParse(bright.Text.Trim(), out var brv) || brv < 0 || brv > 100)
        { await ShowErrorDialogAsync("Edit", "Brightness must be an integer 0-100."); return; }

        var powerGuid = FcPowerPlans.FirstOrDefault(x => x.Name == (string)power.SelectedItem).Guid ?? p.Power;
        var optSel = (string)optimizer.SelectedItem;
        var optVal = optSel == noPreset ? "" : optSel;
        var patch = FancontrolCommandService.BuildProfilePatch(
            dcv, brv, hdr.IsChecked == true, powerGuid, lyd.Text.Trim(),
            (string)lysMode.SelectedItem, lysColor.Text.Trim(), optVal, uiIcon.Text.Trim(), uiDesc.Text.Trim());
        await FcRun(_fc.EditProfileAsync(p.Name, patch));
    }

    private async Task<string?> FcPromptName(string title, string placeholder, string initial = "")
    {
        var box = new TextBox { PlaceholderText = placeholder, Text = initial, MinWidth = 300 };
        var dlg = new ContentDialog
        {
            Title = title, Content = box,
            PrimaryButtonText = "OK", CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = XamlRoot
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary ? box.Text.Trim() : null;
    }

    private async Task FcRun(Task<CtlResult> op)
    {
        CtlResult r;
        try { r = await op; }
        catch (Exception ex) { await ShowErrorDialogAsync("Fancontrol", ex.Message); return; }
        LoadFancontrolProfiles();   // reflect the new state regardless of outcome
        if (!r.Success) await ShowErrorDialogAsync("Fancontrol", r.Output);
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
