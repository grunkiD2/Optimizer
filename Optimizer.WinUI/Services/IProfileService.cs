using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IProfileService
{
    IReadOnlyList<SettingsProfile> BuiltInPresets { get; }
    IReadOnlyList<SettingsProfile> Snapshots { get; }
    void Load();
    Task<bool> ApplyPresetAsync(string profileId, bool includeDestructive = false);

    /// <summary>Apply a preset and report the per-optimization outcome (audit C6). Destructive
    /// optimizations are skipped unless includeDestructive is set after an interactive confirmation.</summary>
    Task<ProfileApplyResult> ApplyPresetDetailedAsync(string profileId, bool includeDestructive = false);

    Task SaveSnapshotAsync(string name);
    Task<bool> RestoreSnapshotAsync(SettingsProfile snapshot);
    Task UpdateSnapshotAsync(SettingsProfile snapshot);
    void DeleteSnapshot(string snapshotId);
    string ExportAll();
    void ImportFromJson(string json);
    void UpsertSnapshot(SettingsProfile snapshot);
}
