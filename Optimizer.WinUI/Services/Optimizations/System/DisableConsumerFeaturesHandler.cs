using Microsoft.Win32;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.System;

public sealed class DisableConsumerFeaturesHandler : OptimizationHandlerBase
{
    public override string Id => OptimizationIds.DisableConsumerFeatures;

    public override OptimizationInfo Info { get; } = new()
    {
        Id = "DisableConsumerFeatures",
        Title = "Disable suggested apps & ads",
        Summary = "Stops Windows from auto-installing suggested apps and showing consumer promotions.",
        Changes = { @"Sets HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent\DisableWindowsConsumerFeatures = 1 (DWORD)" },
        Pros = { "No more auto-installed 'recommended' apps", "Cleaner Start menu" },
        Cons = { "Requires administrator", "Mainly effective on Pro/Enterprise" },
        Recommendation = "Recommended for a cleaner setup. Reversible via Undo.",
        RequiresAdmin = true,
        Reversible = true
    };

    public override bool? IsApplied()
        => ReadHklm(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures") == "1";

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        if (!elevationService.IsElevated)
            return Task.FromResult(NotElevated("This policy is written to HKLM and requires running as administrator."));

        var result = new OptimizationResult { Success = true };
        try
        {
            SetRegistryValue(undoService,
                "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\CloudContent",
                "DisableWindowsConsumerFeatures", 1, RegistryValueKind.DWord,
                "Disable Windows consumer features (suggested apps/ads)");
            result.Message = "Disabled Windows consumer features (auto-installed suggested apps and ads).";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not change consumer-features policy: {ex.Message}");
        }
        return Task.FromResult(result);
    }
}
