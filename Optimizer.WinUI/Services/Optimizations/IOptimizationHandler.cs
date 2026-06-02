using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations;

/// <summary>
/// Encapsulates a single named optimization: its metadata, a check for whether
/// it is already applied, and the logic to apply it.
/// </summary>
public interface IOptimizationHandler
{
    /// <summary>The stable string ID that identifies this optimization (e.g. "DisableBackgroundApps").</summary>
    string Id { get; }

    /// <summary>Human-readable description of this optimization.</summary>
    OptimizationInfo Info { get; }

    /// <summary>
    /// Best-effort check of whether the optimization is currently in effect.
    /// Returns <c>null</c> when the state cannot be determined statically (e.g. file ops).
    /// </summary>
    bool? IsApplied();

    /// <summary>Applies the optimization and records an undo entry if reversible.</summary>
    Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService);
}
