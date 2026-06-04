using Microsoft.Win32;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.System;

/// <summary>
/// Calms down the Windows Update UX for power users who manage updates manually: stops
/// the feature-update opt-in nag, stops "expedited" security surprises that bypass
/// deferral policy, and disables Microsoft Update for other Microsoft apps.
///
/// Does NOT change deferral periods — those are a separate, larger surface; see
/// docs/POWER-INSIGHTS.md and the WUFB section in research synthesis for the deeper plan.
/// </summary>
public sealed class ConfigureWindowsUpdateUxHandler : OptimizationHandlerBase
{
    private const string SubKey = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";

    private static readonly (string Name, int Value, string Why)[] Values =
    {
        ("IsContinuousInnovationOptedIn", 0, "Stop opting in to preview feature updates"),
        ("IsExpedited",                   0, "Do not expedite security updates ahead of deferral"),
        ("AllowMUUpdateService",          0, "Do not use Microsoft Update for other Microsoft apps"),
    };

    public override string Id => OptimizationIds.ConfigureWindowsUpdateUX;

    public override OptimizationInfo Info { get; } = new()
    {
        Id = OptimizationIds.ConfigureWindowsUpdateUX,
        Title = "Quiet Windows Update UX",
        Summary = "Opts out of the Windows Update UI behaviours that bypass your deferral preferences: continuous-innovation feature offers, expedited security pushes, and Microsoft Update for other apps.",
        Changes =
        {
            @"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings\IsContinuousInnovationOptedIn = 0",
            @"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings\IsExpedited = 0",
            @"HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings\AllowMUUpdateService = 0",
        },
        Pros =
        {
            "No 'get the latest features as available' surprises",
            "Security updates respect your deferral policy",
            "Office / Edge / Teams update via their own channels instead of WU",
        },
        Cons =
        {
            "Requires administrator",
            "You take on more manual responsibility for keeping the system patched",
        },
        Recommendation = "Fits the Plex / coding / gaming workloads where uncontrolled updates cause real disruption. Reversible via Undo.",
        RequiresAdmin = true,
        Reversible = true,
        RequiresRestart = false
    };

    public override bool? IsApplied()
    {
        foreach (var (name, value, _) in Values)
            if (ReadHklm(SubKey, name) != value.ToString())
                return false;
        return true;
    }

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        if (!elevationService.IsElevated)
            return Task.FromResult(NotElevated("Windows Update UX settings are stored under HKLM and require running as administrator."));

        var result = new OptimizationResult { Success = true };
        try
        {
            foreach (var (name, value, why) in Values)
                SetRegistryValue(undoService, "HKLM", SubKey, name, value, RegistryValueKind.DWord, why);
            result.Message = "Windows Update UX quieted.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not adjust Windows Update UX: {ex.Message}");
        }
        return Task.FromResult(result);
    }
}
