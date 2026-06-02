using WindowsOptimizer.Models;

namespace WindowsOptimizer.Services;

public interface IWindowsOptimizerService
{
    /// <summary>Built-in read-only preset profiles the user can apply or copy into their own profiles.</summary>
    IReadOnlyList<SettingsProfile> GetBuiltInPresets();

    Task<SettingsProfile> CreateProfileAsync(SettingsProfile profile);
    Task<bool> UpdateProfileAsync(SettingsProfile profile);
    Task<SettingsProfile?> GetProfileAsync(string profileId);
    Task<IEnumerable<SettingsProfile>> ListProfilesAsync();
    Task<bool> DeleteProfileAsync(string profileId);
    Task<bool> ApplyProfileAsync(string profileId);
    Task<bool> RevertProfileAsync(string profileId);

    SystemResource GetCurrentResourceUsage();
    Task<IEnumerable<SystemResource>> GetResourceHistoryAsync(int sampleCount);

    Task<IEnumerable<string>> GetAvailableOptimizationsAsync();
    Task<OptimizationResult> ApplyOptimizationAsync(string optimizationId);

    /// <summary>Returns a human-readable explanation of an optimization, or null if unknown.</summary>
    OptimizationInfo? GetOptimizationInfo(string optimizationId);

    /// <summary>Best-effort check of whether an optimization's target state is already in effect (for diffs). Null = unknown.</summary>
    bool? IsOptimizationApplied(string optimizationId);

    /// <summary>True if the host process has administrator rights.</summary>
    bool IsElevated { get; }

    /// <summary>Number of system changes that can currently be reverted.</summary>
    int PendingUndoCount { get; }

    /// <summary>The individual reversible changes captured so far (most-recent first when displayed).</summary>
    IReadOnlyList<UndoEntry> GetUndoEntries();

    /// <summary>Reverts a single captured change. Returns true if reverted.</summary>
    Task<bool> UndoEntryAsync(UndoEntry entry);

    /// <summary>Reverts every change captured since the undo log was last cleared. Returns count restored.</summary>
    Task<int> UndoAllOptimizationsAsync();
}

public class OptimizationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
