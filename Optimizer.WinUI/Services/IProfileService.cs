using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IProfileService
{
    IReadOnlyList<SettingsProfile> BuiltInPresets { get; }
    IReadOnlyList<SettingsProfile> Snapshots { get; }
    void Load();
    Task<bool> ApplyPresetAsync(string profileId);
    Task SaveSnapshotAsync(string name);
    Task<bool> RestoreSnapshotAsync(SettingsProfile snapshot);
    Task UpdateSnapshotAsync(SettingsProfile snapshot);
    void DeleteSnapshot(string snapshotId);
    string ExportAll();
    void ImportFromJson(string json);
    void UpsertSnapshot(SettingsProfile snapshot);
}
