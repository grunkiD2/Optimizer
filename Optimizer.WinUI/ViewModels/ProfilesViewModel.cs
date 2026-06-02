using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class ProfilesViewModel : ObservableObject
{
    private readonly ProfileService _profileService;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool hasStatusMessage;

    public ObservableCollection<SettingsProfile> Presets { get; } = [];
    public ObservableCollection<SettingsProfile> Snapshots { get; } = [];

    public string CategoryName => "Profiles";
    public string CategoryIcon => "📋";   // Page icon (Segoe Fluent)

    public int PresetCount => Presets.Count;
    public int SnapshotCount => Snapshots.Count;
    public bool NoPresets => Presets.Count == 0;

    public ProfilesViewModel(ProfileService profileService)
    {
        _profileService = profileService;
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

        OnPropertyChanged(nameof(PresetCount));
        OnPropertyChanged(nameof(SnapshotCount));
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

    // ── Helpers ────────────────────────────────────────────────────────────

    private void SetStatus(string message)
    {
        StatusMessage = message;
        HasStatusMessage = !string.IsNullOrEmpty(message);
    }
}
