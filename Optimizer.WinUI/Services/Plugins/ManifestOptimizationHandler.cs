using Optimizer.WinUI.Models;
using Optimizer.WinUI.Models.Plugins;
using Optimizer.WinUI.Services.Optimizations;

namespace Optimizer.WinUI.Services.Plugins;

/// <summary>
/// An <see cref="IOptimizationHandler"/> whose behaviour is driven by a parsed
/// <see cref="OptimizationManifest"/> rather than hard-coded C#.
/// Delegates all system mutations to <see cref="IDeclarativeChangeExecutor"/>,
/// which enforces the permission allow-list and captures undo state automatically.
/// </summary>
public sealed class ManifestOptimizationHandler : IOptimizationHandler
{
    private readonly OptimizationManifest _manifest;
    private readonly IDeclarativeChangeExecutor _executor;
    private readonly OptimizationInfo _info;

    public ManifestOptimizationHandler(OptimizationManifest manifest, IDeclarativeChangeExecutor executor)
    {
        _manifest = manifest;
        _executor = executor;
        _info = BuildInfo(manifest);
    }

    // ── IOptimizationHandler ──────────────────────────────────────────────────

    public string Id => _manifest.Id;

    public OptimizationInfo Info => _info;

    /// <summary>
    /// Returns true if all registry changes declared by the manifest are currently in the applied state.
    /// Returns null (via the bool? interface) when the state cannot be determined — we map the executor's
    /// non-nullable bool to nullable here.
    /// </summary>
    public bool? IsApplied()
    {
        try
        {
            return _executor.IsApplied(_manifest);
        }
        catch
        {
            return null;
        }
    }

    public Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        // Validate permissions before touching the system (belt-and-suspenders:
        // the executor also validates, but checking here lets us short-circuit cleanly).
        if (!_executor.ValidatePermissions(_manifest, out var violations))
        {
            return Task.FromResult(new OptimizationResult
            {
                Success = false,
                Message = "Plugin permission validation failed.",
                Errors = violations.ToList()
            });
        }

        return ApplyCoreAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<OptimizationResult> ApplyCoreAsync()
    {
        var changeResult = await _executor.ApplyAsync(_manifest);
        return new OptimizationResult
        {
            Success = changeResult.Success,
            Message = changeResult.Message
        };
    }

    private static OptimizationInfo BuildInfo(OptimizationManifest manifest) => new()
    {
        Id          = manifest.Id,
        Title       = manifest.Name,
        Summary     = manifest.Description,
        // Surface the declared changes as human-readable lines (path + value for registry entries)
        Changes     = manifest.Changes
                        .Select(c => c.Type?.ToLowerInvariant() switch
                        {
                            "registry" => $"Registry: {c.Path}\\{c.Value} = {c.Apply}",
                            "service"  => $"Service: {c.ServiceName} → {c.ApplyStartup ?? c.ApplyState}",
                            "file"     => $"File: {c.FileAction} {c.FilePath}",
                            "powercfg" => $"powercfg {c.PowerCfgArgs}",
                            "scheduled-task" => $"Task: {c.TaskAction} {c.TaskName}",
                            _          => $"{c.Type}: (unknown)"
                        })
                        .ToList(),
        Pros           = manifest.Pros.ToList(),
        Cons           = manifest.Cons.ToList(),
        RequiresAdmin  = manifest.RequiresAdmin,
        RequiresRestart = manifest.RequiresRestart,
        Reversible     = manifest.Reversible,
        Author         = manifest.Author,
        IsPlugin       = true
    };
}
