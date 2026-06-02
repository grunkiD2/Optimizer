using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.Storage;

public sealed class ClearWindowsUpdateCacheHandler : OptimizationHandlerBase
{
    public override string Id => OptimizationIds.ClearWindowsUpdateCache;

    public override OptimizationInfo Info { get; } = new()
    {
        Id = "ClearWindowsUpdateCache",
        Title = "Clear Windows Update cache",
        Summary = "Stops the update service, deletes downloaded update files, then restarts it.",
        Changes = { "Stops wuauserv", @"Deletes %WinDir%\SoftwareDistribution\Download\*", "Starts wuauserv" },
        Pros = { "Frees disk space", "Fixes stuck/corrupted update downloads" },
        Cons = { "Requires administrator", "Cannot be undone", "Windows re-downloads pending updates" },
        Recommendation = "Use when updates are stuck or to reclaim space. Not reversible.",
        RequiresAdmin = true,
        Reversible = false
    };

    public override bool? IsApplied() => null; // one-shot action, no persistent state

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        if (!elevationService.IsElevated)
            return Task.FromResult(NotElevated("Clearing the Windows Update cache requires running as administrator."));

        var result = new OptimizationResult { Success = true };
        try
        {
            RunProcess("net", "stop wuauserv");
            var download = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
            long freed = 0;
            if (Directory.Exists(download))
            {
                foreach (var file in Directory.GetFiles(download, "*", SearchOption.AllDirectories))
                {
                    try { freed += new FileInfo(file).Length; File.Delete(file); } catch { }
                }
            }
            RunProcess("net", "start wuauserv");
            result.Message = $"Cleared the Windows Update download cache (~{freed / 1024 / 1024} MB).";
            result.Warnings.Add("Cannot be undone; Windows will re-download updates as needed.");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not clear update cache: {ex.Message}");
        }
        return Task.FromResult(result);
    }
}
