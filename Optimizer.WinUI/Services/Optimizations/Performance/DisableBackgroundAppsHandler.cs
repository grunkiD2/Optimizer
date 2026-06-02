using Microsoft.Win32;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.Performance;

public sealed class DisableBackgroundAppsHandler : OptimizationHandlerBase
{
    public override string Id => OptimizationIds.DisableBackgroundApps;

    public override OptimizationInfo Info { get; } = new()
    {
        Id = "DisableBackgroundApps",
        Title = "Disable background apps",
        Summary = "Stops Microsoft Store / UWP apps from running in the background for the current user.",
        Changes = { @"Sets HKCU\…\BackgroundAccessApplications\GlobalUserDisabled = 1 (DWORD)" },
        Pros = { "Frees CPU/RAM and reduces battery drain from idle apps", "Takes effect immediately, no restart" },
        Cons = { "Live tiles, push notifications and some sync for Store apps may stop", "Does not affect classic desktop (Win32) programs" },
        Recommendation = "Safe and recommended on laptops and low-RAM machines. Reversible at any time.",
        RequiresAdmin = false,
        Reversible = true
    };

    public override bool? IsApplied()
        => ReadHkcu(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled") == "1";

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        SetRegistryValue(undoService,
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications",
            "GlobalUserDisabled", 1, RegistryValueKind.DWord,
            "Disable background apps");

        return Task.FromResult(new OptimizationResult
        {
            Success = true,
            Message = "Background apps disabled for the current user."
        });
    }
}
