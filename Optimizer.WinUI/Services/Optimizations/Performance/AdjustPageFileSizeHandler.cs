using Microsoft.Win32;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.Performance;

public sealed class AdjustPageFileSizeHandler : OptimizationHandlerBase
{
    public override string Id => OptimizationIds.AdjustPageFileSize;

    public override OptimizationInfo Info { get; } = new()
    {
        Id = "AdjustPageFileSize",
        Title = "Set page file to system-managed",
        Summary = "Lets Windows manage the page file size on the system drive (recommended default).",
        Changes = { @"Sets HKLM\…\Memory Management\PagingFiles to '<SystemDrive>\pagefile.sys 0 0' (system-managed)" },
        Pros = { "Avoids out-of-memory errors from a too-small fixed page file", "Good general-purpose default" },
        Cons = { "Requires administrator", "Needs a restart to take effect", "If 'Automatically manage' is on in System Properties it must be turned off first" },
        Recommendation = "Recommended if your page file was previously set too small or disabled. Restart afterwards.",
        SuggestedImplementation = "For SSD systems with lots of RAM, a fixed size (e.g. 1.5× RAM) can reduce fragmentation; expose size as an option.",
        RequiresAdmin = true,
        Reversible = true,
        RequiresRestart = true
    };

    public override bool? IsApplied() => null; // not statically determinable

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        if (!elevationService.IsElevated)
            return Task.FromResult(NotElevated("Page-file changes write to HKLM and require running as administrator."));

        var result = new OptimizationResult { Success = true };
        try
        {
            var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            var pagingFiles = new[] { $@"{systemDrive}\pagefile.sys 0 0" };
            SetRegistryValue(undoService,
                "HKLM",
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "PagingFiles", pagingFiles, RegistryValueKind.MultiString,
                "Set page file to system-managed");

            result.Message = $"Page file set to system-managed on {systemDrive}.";
            result.Warnings.Add("Restart required. If 'Automatically manage paging file size' is on in System Properties, turn it off for this to take effect.");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not change page-file settings: {ex.Message}");
        }
        return Task.FromResult(result);
    }
}
