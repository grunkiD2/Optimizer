using System.Text.Json;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class ProfileService
{
    private readonly IWindowsOptimizerService _optimizer;
    private readonly List<SettingsProfile> _snapshots = [];
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Optimizer", "snapshots.json");

    public ProfileService(IWindowsOptimizerService optimizer)
    {
        _optimizer = optimizer;
    }

    public IReadOnlyList<SettingsProfile> BuiltInPresets => _optimizer.GetBuiltInPresets();
    public IReadOnlyList<SettingsProfile> Snapshots => _snapshots;

    public void Load()
    {
        _snapshots.Clear();
        if (!File.Exists(FilePath)) return;

        try
        {
            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<List<SettingsProfile>>(json);
            if (loaded != null) _snapshots.AddRange(loaded);
        }
        catch
        {
            // corrupted file — start fresh
        }
    }

    /// <summary>Apply a built-in preset by its profile ID.</summary>
    public Task<bool> ApplyPresetAsync(string profileId)
        => _optimizer.ApplyProfileAsync(profileId);

    /// <summary>Capture the currently-applied optimizations as a named snapshot.</summary>
    public async Task SaveSnapshotAsync(string name)
    {
        var availableIds = await _optimizer.GetAvailableOptimizationsAsync();
        var activeIds = availableIds
            .Where(id => _optimizer.IsOptimizationApplied(id) == true)
            .ToList();

        var snapshot = new SettingsProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Description = $"Snapshot saved {DateTime.UtcNow:g}",
            ProfileType = ProfileType.Custom,
            CreatedAt = DateTime.UtcNow,
            Optimizations = activeIds
        };

        _snapshots.Add(snapshot);
        SaveSnapshots();
    }

    /// <summary>Re-apply all optimizations stored in a snapshot.</summary>
    public async Task<bool> RestoreSnapshotAsync(SettingsProfile snapshot)
    {
        var allSucceeded = true;
        foreach (var id in snapshot.Optimizations)
        {
            var result = await _optimizer.ApplyOptimizationAsync(id);
            if (!result.Success) allSucceeded = false;
        }
        snapshot.LastAppliedAt = DateTime.UtcNow;
        SaveSnapshots();
        return allSucceeded;
    }

    /// <summary>Refresh a snapshot's optimization list to the current applied state.</summary>
    public async Task UpdateSnapshotAsync(SettingsProfile snapshot)
    {
        var availableIds = await _optimizer.GetAvailableOptimizationsAsync();
        snapshot.Optimizations = availableIds
            .Where(id => _optimizer.IsOptimizationApplied(id) == true)
            .ToList();
        snapshot.LastAppliedAt = DateTime.UtcNow;
        SaveSnapshots();
    }

    public void DeleteSnapshot(string snapshotId)
    {
        _snapshots.RemoveAll(s => s.Id == snapshotId);
        SaveSnapshots();
    }

    public string ExportAll()
        => JsonSerializer.Serialize(_snapshots, new JsonSerializerOptions { WriteIndented = true });

    public void ImportFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Import file is empty.");

        List<SettingsProfile>? imported;
        try
        {
            // Accept both an array [ {...}, ... ] or a single object { ... }
            var trimmed = json.TrimStart();
            if (trimmed.StartsWith('{'))
            {
                var single = JsonSerializer.Deserialize<SettingsProfile>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                imported = single != null ? new List<SettingsProfile> { single } : null;
            }
            else
            {
                imported = JsonSerializer.Deserialize<List<SettingsProfile>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }
        catch (JsonException jex)
        {
            throw new InvalidDataException($"The file does not contain valid profile data: {jex.Message}", jex);
        }

        if (imported == null || imported.Count == 0)
            throw new InvalidDataException("No profiles found in the import file.");

        var newSnapshots = imported.Where(i => !_snapshots.Any(s => s.Id == i.Id)).ToList();
        _snapshots.AddRange(newSnapshots);
        SaveSnapshots();
    }

    private void SaveSnapshots()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(_snapshots, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
