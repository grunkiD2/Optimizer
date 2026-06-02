using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.Performance;

public sealed class OptimizePowerSettingsHandler : OptimizationHandlerBase
{
    private const string HighPerformanceSchemeGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    public override string Id => OptimizationIds.OptimizePowerSettings;

    public override OptimizationInfo Info { get; } = new()
    {
        Id = "OptimizePowerSettings",
        Title = "Switch to High Performance power plan",
        Summary = "Activates the built-in High Performance power scheme via powercfg.",
        Changes = { "Runs: powercfg /setactive 8c5e7fda-… (High Performance)", "Your previous active scheme is recorded for undo" },
        Pros = { "Keeps CPU at higher clocks, reduces latency/stutter", "Good for desktops and gaming", "The one tweak with a large, measured impact (Intel Arrow Lake saw ~55–67% single-core drops off 'Best performance'; Intel recommends it)" },
        Cons = { "Higher power draw and heat", "Reduces battery life on laptops" },
        Recommendation = "★ Highest-impact optimization here (evidence-backed). Recommended on desktops. On laptops, prefer 'Balanced' unless plugged in.",
        SuggestedImplementation = "Consider the 'Ultimate Performance' plan on workstations (powercfg -duplicatescheme e9a42b02-…).",
        RequiresAdmin = false,
        Reversible = true
    };

    public override bool? IsApplied()
        => string.Equals(GetActivePowerSchemeGuid(), HighPerformanceSchemeGuid, StringComparison.OrdinalIgnoreCase);

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        var result = new OptimizationResult { Success = true };
        try
        {
            var current = GetActivePowerSchemeGuid();
            if (!string.IsNullOrEmpty(current))
                undoService.CapturePowerScheme(current, "Restore previous power scheme");

            UndoService.RunPowerCfg($"/setactive {HighPerformanceSchemeGuid}");
            result.Message = "Switched to the High Performance power scheme.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not change power scheme: {ex.Message}");
        }
        return Task.FromResult(result);
    }
}
