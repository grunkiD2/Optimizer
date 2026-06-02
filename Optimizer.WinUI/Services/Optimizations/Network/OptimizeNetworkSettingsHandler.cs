using Microsoft.Win32;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.Network;

public sealed class OptimizeNetworkSettingsHandler : OptimizationHandlerBase
{
    public override string Id => OptimizationIds.OptimizeNetworkSettings;

    public override OptimizationInfo Info { get; } = new()
    {
        Id = "OptimizeNetworkSettings",
        Title = "Disable network throttling",
        Summary = "Removes the multimedia network throttle and maximizes system responsiveness.",
        Changes =
        {
            @"Sets HKLM\…\Multimedia\SystemProfile\NetworkThrottlingIndex = 0xFFFFFFFF",
            @"Sets HKLM\…\Multimedia\SystemProfile\SystemResponsiveness = 0 (DWORD)"
        },
        Pros = { "Lifts the MMCSS packet throttle that can cap gigabit during media playback + large transfers" },
        Cons = { "Requires administrator", "Benefit is narrow — only matters when streaming media during a big network transfer; no effect otherwise", "Windows clamps SystemResponsiveness below 10 up to 20, so '0' is not applied as written", "A restart is recommended" },
        Recommendation = "Niche: mainly helps the specific 'media playing while transferring large files on gigabit' case. Little to no benefit for general use. Reversible via Undo.",
        RequiresAdmin = true,
        Reversible = true,
        RequiresRestart = true
    };

    public override bool? IsApplied()
        => ReadHklm(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness") == "0";

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        if (!elevationService.IsElevated)
            return Task.FromResult(NotElevated("Network tuning writes to HKLM and requires running as administrator."));

        var result = new OptimizationResult { Success = true };
        try
        {
            const string profileKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
            SetRegistryValue(undoService, "HKLM", profileKey, "NetworkThrottlingIndex",
                unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord, "Disable network throttling");
            SetRegistryValue(undoService, "HKLM", profileKey, "SystemResponsiveness",
                0, RegistryValueKind.DWord, "Maximize multimedia/network responsiveness");

            result.Message = "Disabled network throttling and maximized system responsiveness.";
            result.Warnings.Add("A restart may be required for these changes to fully apply.");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not change network settings: {ex.Message}");
        }
        return Task.FromResult(result);
    }
}
