using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IWindowsOptimizerService
{
    /// <summary>Built-in read-only preset profiles the user can apply or copy into their own profiles.</summary>
    IReadOnlyList<SettingsProfile> GetBuiltInPresets();

    Task<bool> ApplyProfileAsync(string profileId);

    /// <summary>
    /// Audit C6: applies a profile and reports the per-optimization outcome instead of a flat
    /// bool that was true even when every bundled optimization failed. UI surfaces should call
    /// this and show "X of Y applied".
    /// </summary>
    Task<ProfileApplyResult> ApplyProfileDetailedAsync(string profileId);

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

/// <summary>Aggregated outcome of applying a profile's bundled optimizations (audit C6).</summary>
public class ProfileApplyResult
{
    public bool ProfileFound { get; set; } = true;
    public int Applied { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();

    /// <summary>True only when the profile was found and nothing failed.</summary>
    public bool Success => ProfileFound && Failed == 0;

    /// <summary>One-line summary for the UI, e.g. "3 of 5 applied — 2 need administrator".</summary>
    public string Summary
    {
        get
        {
            if (!ProfileFound) return "Profile not found.";
            var total = Applied + Failed;
            if (total == 0) return "Nothing to apply.";
            if (Failed == 0) return $"Applied all {Applied} optimization(s).";
            return $"{Applied} of {total} applied — {Failed} failed (often: needs administrator).";
        }
    }
}
