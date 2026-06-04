using Microsoft.Win32;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.Performance;

/// <summary>
/// Turns off the acrylic / Mica blur Windows uses on title bars, the Start menu,
/// taskbar, and many WinUI surfaces. Modest GPU work removed; meaningful on low-end
/// hardware and laptops on battery.
/// </summary>
public sealed class DisableTransparencyEffectsHandler : OptimizationHandlerBase
{
    private const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "EnableTransparency";

    public override string Id => OptimizationIds.DisableTransparencyEffects;

    public override OptimizationInfo Info { get; } = new()
    {
        Id = OptimizationIds.DisableTransparencyEffects,
        Title = "Disable transparency effects",
        Summary = "Turns off the acrylic / Mica blur Windows applies to title bars, Start menu, taskbar, and many surfaces. Same toggle as Settings → Personalization → Colors → Transparency effects.",
        Changes = { @"Sets HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\EnableTransparency = 0 (DWORD)" },
        Pros =
        {
            "Removes per-frame compositor blur work — meaningful on low-end iGPUs",
            "Small battery win on laptops",
            "Slightly faster animations and window transitions",
        },
        Cons =
        {
            "Surfaces become flat / opaque — purely visual",
            "Optimizer itself reads less 'depth-y' (the HudBackdrop refraction is muted)",
        },
        Recommendation = "Pair with Gaming or Battery profiles; safely reversible via Undo.",
        RequiresAdmin = false,
        Reversible = true,
        RequiresRestart = false
    };

    public override bool? IsApplied()
        => ReadHkcu(SubKey, ValueName) == "0";

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        var result = new OptimizationResult { Success = true };
        try
        {
            SetRegistryValue(undoService, "HKCU", SubKey, ValueName, 0,
                RegistryValueKind.DWord, "Disable acrylic / Mica transparency");
            result.Message = "Transparency effects disabled.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not disable transparency: {ex.Message}");
        }
        return Task.FromResult(result);
    }
}
