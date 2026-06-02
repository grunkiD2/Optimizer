using Microsoft.Win32;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.System;

public sealed class DisableTelemetryHandler : OptimizationHandlerBase
{
    public override string Id => OptimizationIds.DisableTelemetry;

    public override OptimizationInfo Info { get; } = new()
    {
        Id = "DisableTelemetry",
        Title = "Minimize telemetry",
        Summary = "Sets the diagnostic-data policy to its lowest level.",
        Changes = { @"Sets HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection\AllowTelemetry = 0 (DWORD)" },
        Pros = { "Reduces background diagnostic uploads", "Improves privacy" },
        Cons = { "Requires administrator", "Home/Pro enforce a minimum 'Required' level", "A restart may be needed" },
        Recommendation = "Reversible via Undo. On Home/Pro the effective floor may be higher than Off.",
        RequiresAdmin = true,
        Reversible = true,
        RequiresRestart = true
    };

    public override bool? IsApplied()
        => ReadHklm(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") == "0";

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        if (!elevationService.IsElevated)
            return Task.FromResult(NotElevated("Telemetry policy is written to HKLM and requires running as administrator."));

        var result = new OptimizationResult { Success = true };
        try
        {
            SetRegistryValue(undoService,
                "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                "AllowTelemetry", 0, RegistryValueKind.DWord, "Set diagnostic data to Security/Off");
            result.Message = "Telemetry policy set to the minimum level.";
            result.Warnings.Add("Some editions enforce a higher minimum; a restart may be needed.");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not set telemetry policy: {ex.Message}");
        }
        return Task.FromResult(result);
    }
}
