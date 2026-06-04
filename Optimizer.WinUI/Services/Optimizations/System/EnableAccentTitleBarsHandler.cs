using Microsoft.Win32;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.System;

/// <summary>
/// Applies the user's accent colour to title bars and Start / taskbar / Action Center.
/// Two registry keys are involved — DWM honours <c>ColorPrevalence</c> for title bars,
/// the Personalize subkey controls Start / taskbar / Action Center.
/// </summary>
public sealed class EnableAccentTitleBarsHandler : OptimizationHandlerBase
{
    private const string DwmSubKey       = @"Software\Microsoft\Windows\DWM";
    private const string PersonalizeSubKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName       = "ColorPrevalence";

    public override string Id => OptimizationIds.EnableAccentTitleBars;

    public override OptimizationInfo Info { get; } = new()
    {
        Id = OptimizationIds.EnableAccentTitleBars,
        Title = "Use accent colour on title bars + Start",
        Summary = "Applies your current accent colour to window title bars and to the Start menu / taskbar / Action Center. Cosmetic; mirrors the toggles in Settings → Personalization → Colors.",
        Changes =
        {
            @"Sets HKCU\Software\Microsoft\Windows\DWM\ColorPrevalence = 1 (DWORD) — title-bar tint",
            @"Sets HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\ColorPrevalence = 1 (DWORD) — Start / taskbar / Action Center tint",
        },
        Pros =
        {
            "Easier to see which window has focus",
            "Matches the personalization preference Optimizer already encourages",
        },
        Cons =
        {
            "Purely cosmetic — no performance impact",
            "Some users prefer the default neutral title bars",
        },
        Recommendation = "Personalization tweak; safe to flip either way. Fully reversible via Undo.",
        RequiresAdmin = false,
        Reversible = true,
        RequiresRestart = false
    };

    public override bool? IsApplied()
    {
        // Treat as applied only when BOTH keys are 1; either-or is a partial state we should fix.
        var dwm = ReadHkcu(DwmSubKey, ValueName);
        var personalize = ReadHkcu(PersonalizeSubKey, ValueName);
        return dwm == "1" && personalize == "1";
    }

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        var result = new OptimizationResult { Success = true };
        try
        {
            SetRegistryValue(undoService, "HKCU", DwmSubKey, ValueName, 1,
                RegistryValueKind.DWord, "Tint title bars with accent colour");
            SetRegistryValue(undoService, "HKCU", PersonalizeSubKey, ValueName, 1,
                RegistryValueKind.DWord, "Tint Start / taskbar / Action Center with accent colour");
            result.Message = "Accent colour applied to title bars and Start.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not apply accent colouring: {ex.Message}");
        }
        return Task.FromResult(result);
    }
}
