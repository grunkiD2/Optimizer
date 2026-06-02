using Microsoft.Win32;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.Performance;

public sealed class DisableVisualEffectsHandler : OptimizationHandlerBase
{
    public override string Id => OptimizationIds.DisableVisualEffects;

    public override OptimizationInfo Info { get; } = new()
    {
        Id = "DisableVisualEffects",
        Title = "Adjust visual effects for best performance",
        Summary = "Sets Windows performance options to 'Adjust for best performance'.",
        Changes = { @"Sets HKCU\…\Explorer\VisualEffects\VisualFXSetting = 2 (DWORD)" },
        Pros = { "Disables shadows, fades and other effects to reduce GPU/CPU load" },
        Cons = { "Windows looks flatter/plainer", "Some users find disabled font smoothing harsh — can be re-enabled separately" },
        Recommendation = "Recommended on low-end hardware. Pairs well with 'Disable animations'.",
        SuggestedImplementation = "For finer control, set individual UserPreferencesMask bits instead of the global 'best performance' switch.",
        RequiresAdmin = false,
        Reversible = true,
        RequiresRestart = true
    };

    public override bool? IsApplied()
        => ReadHkcu(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting") == "2";

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        SetRegistryValue(undoService,
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
            "VisualFXSetting", 2, RegistryValueKind.DWord,
            "Adjust visual effects for best performance");

        return Task.FromResult(new OptimizationResult
        {
            Success = true,
            Message = "Visual effects set to 'best performance'."
        });
    }
}
