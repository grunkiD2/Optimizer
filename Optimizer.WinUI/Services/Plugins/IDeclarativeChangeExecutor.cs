using Optimizer.WinUI.Models.Plugins;

namespace Optimizer.WinUI.Services.Plugins;

/// <summary>Result of applying (or attempting to apply) a manifest's changes.</summary>
public record ChangeResult(bool Success, string Message);

/// <summary>
/// Applies the declarative changes described in an <see cref="OptimizationManifest"/>,
/// enforcing the permission allow-list and capturing undo state into <see cref="IUndoService"/>.
/// </summary>
public interface IDeclarativeChangeExecutor
{
    /// <summary>
    /// Checks every change in <paramref name="manifest"/> against the permission allow-list.
    /// Returns <c>false</c> if any change violates the allow-list; <paramref name="violations"/>
    /// contains a human-readable description of each violation.
    /// </summary>
    bool ValidatePermissions(OptimizationManifest manifest, out IReadOnlyList<string> violations);

    /// <summary>
    /// Best-effort read of current system state: returns <c>true</c> if all registry/service
    /// changes in the manifest already match their applied values.
    /// </summary>
    bool IsApplied(OptimizationManifest manifest);

    /// <summary>
    /// Validates permissions, then applies all changes declared in the manifest,
    /// capturing undo state via <see cref="IUndoService"/> so the existing Undo UI works.
    /// </summary>
    Task<ChangeResult> ApplyAsync(OptimizationManifest manifest);
}
