using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.System;

public sealed class DisableHibernationHandler : OptimizationHandlerBase
{
    public override string Id => OptimizationIds.DisableHibernation;

    public override OptimizationInfo Info { get; } = new()
    {
        Id = "DisableHibernation",
        Title = "Disable hibernation",
        Summary = "Turns off hibernation and frees the hiberfil.sys file (often several GB).",
        Changes = { "Runs: powercfg /hibernate off" },
        Pros = { "Frees disk space equal to a large fraction of your RAM", "Removes hiberfil.sys" },
        Cons = { "Requires administrator", "Disables Hibernate and may disable Fast Startup", "Not part of Undo" },
        Recommendation = "Good on desktops with limited SSD space. Re-enable with: powercfg /hibernate on",
        RequiresAdmin = true,
        Reversible = false
    };

    public override bool? IsApplied() => null; // not statically detectable without WMI

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        if (!elevationService.IsElevated)
            return Task.FromResult(NotElevated("Toggling hibernation requires running as administrator."));

        var result = new OptimizationResult { Success = true };
        try
        {
            RunProcess("powercfg", "/hibernate off");
            result.Message = "Hibernation disabled (frees the hiberfil.sys disk space).";
            result.Warnings.Add("Not part of Undo. Re-enable any time with: powercfg /hibernate on");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not disable hibernation: {ex.Message}");
        }
        return Task.FromResult(result);
    }
}
