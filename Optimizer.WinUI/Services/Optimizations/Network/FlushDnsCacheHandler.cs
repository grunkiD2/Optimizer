using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.Network;

public sealed class FlushDnsCacheHandler : OptimizationHandlerBase
{
    public override string Id => OptimizationIds.FlushDnsCache;

    public override OptimizationInfo Info { get; } = new()
    {
        Id = "FlushDnsCache",
        Title = "Flush DNS cache",
        Summary = "Clears the local DNS resolver cache to fix stale name-resolution issues.",
        Changes = { "Runs: ipconfig /flushdns" },
        Pros = { "Resolves 'can't reach site' issues caused by stale/poisoned DNS entries", "Instant, no restart" },
        Cons = { "First lookups after the flush are marginally slower while the cache repopulates" },
        Recommendation = "Safe any time. A one-shot maintenance action — there's nothing to undo.",
        RequiresAdmin = false,
        Reversible = false
    };

    public override bool? IsApplied() => null; // one-shot action, no persistent state

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        var result = new OptimizationResult { Success = true };
        try
        {
            RunProcess("ipconfig", "/flushdns");
            result.Message = "DNS resolver cache flushed.";
            result.Warnings.Add("This is a one-time action; the cache rebuilds automatically (nothing to undo).");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not flush DNS cache: {ex.Message}");
        }
        return Task.FromResult(result);
    }
}
