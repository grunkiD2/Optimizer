using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Analytics;

namespace Optimizer.WinUI.ViewModels;

public partial class ProfilesViewModel : ObservableObject
{
    private readonly IProfileService _profileService;
    private readonly IProfileAutomationService _automationService;
    private readonly IProfileContextService _profileContext;
    private readonly IContextDetectionService _contextDetection;

    [ObservableProperty] private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    public ObservableCollection<SettingsProfile> Presets { get; } = [];
    public ObservableCollection<SettingsProfile> Snapshots { get; } = [];
    public ObservableCollection<ProfileRule> AutomationRules { get; } = [];

    public string CategoryName => "Profiles";
    public string CategoryIcon => "📋";

    public int PresetCount => Presets.Count;
    public int SnapshotCount => Snapshots.Count;
    public bool NoPresets => Presets.Count == 0;
    public int RuleCount => AutomationRules.Count;

    public ProfilesViewModel(
        IProfileService profileService,
        IProfileAutomationService automationService,
        IProfileContextService profileContext,
        IContextDetectionService contextDetection)
    {
        _profileService = profileService;
        _automationService = automationService;
        _profileContext = profileContext;
        _contextDetection = contextDetection;
    }

    /// <summary>Record a profile application against the detected context for learning.</summary>
    private async Task RecordApplicationAsync(string profileId)
    {
        try
        {
            var context = await _contextDetection.DetectContextAsync();
            await _profileContext.RecordApplicationAsync(profileId, context);
        }
        catch (Exception ex)
        {
            EngineLog.Error("Failed to record profile application", ex);
        }
    }

    /// <summary>Called by the page on Loaded — reloads both lists from the service.</summary>
    public void Load()
    {
        Presets.Clear();
        foreach (var p in _profileService.BuiltInPresets)
            Presets.Add(p);

        Snapshots.Clear();
        foreach (var s in _profileService.Snapshots)
            Snapshots.Add(s);

        AutomationRules.Clear();
        foreach (var r in _automationService.Rules)
            AutomationRules.Add(r);

        OnPropertyChanged(nameof(PresetCount));
        OnPropertyChanged(nameof(SnapshotCount));
        OnPropertyChanged(nameof(NoPresets));
        OnPropertyChanged(nameof(RuleCount));
    }

    // ── Presets ────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task ApplyPresetAsync(SettingsProfile preset)
    {
        if (preset is null) return;
        IsBusy = true;
        SetStatus($"Applying preset \"{preset.Name}\"…");
        try
        {
            var ok = await _profileService.ApplyPresetAsync(preset.Id);
            await RecordApplicationAsync(preset.Id);
            SetStatus(ok
                ? $"Preset \"{preset.Name}\" applied successfully."
                : $"Preset \"{preset.Name}\" completed with errors — check the history log.");
        }
        catch (Exception ex)
        {
            SetStatus($"Error applying preset: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Snapshots ──────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task ApplySnapshotAsync(SettingsProfile snapshot)
    {
        if (snapshot is null) return;
        IsBusy = true;
        SetStatus($"Restoring snapshot \"{snapshot.Name}\"…");
        try
        {
            var ok = await _profileService.RestoreSnapshotAsync(snapshot);
            await RecordApplicationAsync(snapshot.Id);
            SetStatus(ok
                ? $"Snapshot \"{snapshot.Name}\" restored."
                : $"Snapshot \"{snapshot.Name}\" restored with some errors.");
            Load();
        }
        catch (Exception ex)
        {
            SetStatus($"Error restoring snapshot: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Save a new snapshot with the given name (called from the page after user input).</summary>
    [RelayCommand]
    public async Task SaveSnapshotAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        IsBusy = true;
        SetStatus("Saving snapshot…");
        try
        {
            await _profileService.SaveSnapshotAsync(name.Trim());
            Load();
            SetStatus($"Snapshot \"{name.Trim()}\" saved.");
        }
        catch (Exception ex)
        {
            SetStatus($"Error saving snapshot: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Update a snapshot's optimizations to the current applied state.</summary>
    [RelayCommand]
    public async Task UpdateSnapshotAsync(SettingsProfile snapshot)
    {
        if (snapshot is null) return;
        IsBusy = true;
        SetStatus($"Updating snapshot \"{snapshot.Name}\"…");
        try
        {
            await _profileService.UpdateSnapshotAsync(snapshot);
            Load();
            SetStatus($"Snapshot \"{snapshot.Name}\" updated.");
        }
        catch (Exception ex)
        {
            SetStatus($"Error updating snapshot: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void DeleteSnapshot(SettingsProfile snapshot)
    {
        if (snapshot is null) return;
        _profileService.DeleteSnapshot(snapshot.Id);
        Load();
        SetStatus($"Snapshot \"{snapshot.Name}\" deleted.");
    }

    [RelayCommand]
    public async Task ExportAsync()
    {
        IsBusy = true;
        SetStatus("Exporting snapshots…");
        try
        {
            var json = _profileService.ExportAll();
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var path = Path.Combine(folder, $"optimizer-snapshots-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(path, json);
            SetStatus($"Exported to {path}");
        }
        catch (Exception ex)
        {
            SetStatus($"Export failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Import from a JSON file path (called from the page after the picker resolves).</summary>
    [RelayCommand]
    public async Task ImportAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        IsBusy = true;
        SetStatus("Importing snapshots…");
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            _profileService.ImportFromJson(json);
            Load();
            SetStatus("Snapshots imported successfully.");
        }
        catch (Exception ex)
        {
            SetStatus($"Import failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Automation Rules ───────────────────────────────────────────────────

    /// <summary>
    /// Add a new rule. Called from the page after the user fills the dialog.
    /// </summary>
    public async Task AddRuleAsync(ProfileRule rule)
    {
        await _automationService.AddRuleAsync(rule);
        AutomationRules.Add(rule);
        OnPropertyChanged(nameof(RuleCount));
        SetStatus($"Rule \"{rule.Name}\" added.");
    }

    [RelayCommand]
    public async Task DeleteRuleAsync(ProfileRule rule)
    {
        if (rule is null) return;
        await _automationService.DeleteRuleAsync(rule.Id);
        AutomationRules.Remove(rule);
        OnPropertyChanged(nameof(RuleCount));
        SetStatus($"Rule \"{rule.Name}\" deleted.");
    }

    [RelayCommand]
    public async Task ToggleRuleAsync(ProfileRule rule)
    {
        if (rule is null) return;
        rule.IsEnabled = !rule.IsEnabled;
        await _automationService.UpdateRuleAsync(rule);
        SetStatus($"Rule \"{rule.Name}\" {(rule.IsEnabled ? "enabled" : "disabled")}.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void SetStatus(string message)
    {
        StatusMessage = message;
    }
}
