namespace Optimizer.WinUI.Services.Analytics;

/// <summary>
/// Saves and restores a per-context baseline of system state (registry values) so that
/// switching between contexts (Gaming → Work) doesn't let one context's tweaks bleed into
/// another. Snapshots are stored locally in SQLite.
/// </summary>
public interface IContextStateManager
{
    /// <summary>Capture the current values of the tracked registry targets as this context's baseline.</summary>
    Task SaveContextBaselineAsync(string context, IEnumerable<(string root, string subKey, string valueName)> targets);

    /// <summary>Restore a previously-saved baseline for the context. Returns false if none exists.</summary>
    Task<bool> RestoreContextBaselineAsync(string context);

    /// <summary>Whether a baseline snapshot exists for the context.</summary>
    Task<bool> HasBaselineAsync(string context);
}
