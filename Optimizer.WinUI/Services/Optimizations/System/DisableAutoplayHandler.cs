using Microsoft.Win32;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.System;

/// <summary>
/// Disables Windows Autoplay so the system never silently launches content from inserted
/// USB sticks, optical media, or card readers. Pure HKCU registry tweak.
/// </summary>
public sealed class DisableAutoplayHandler : OptimizationHandlerBase
{
    private const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers";
    private const string ValueName = "DisableAutoplay";

    public override string Id => OptimizationIds.DisableAutoplay;

    public override OptimizationInfo Info { get; } = new()
    {
        Id = OptimizationIds.DisableAutoplay,
        Title = "Disable Autoplay",
        Summary = "Stops Windows from automatically opening / running content from inserted USB sticks, optical media, and card readers.",
        Changes = { @"Sets HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers\DisableAutoplay = 1 (DWORD)" },
        Pros =
        {
            "Removes a common social-engineering attack surface (auto-launching from removable media)",
            "No more surprise Settings/Explorer pop-ups when you plug a drive in",
        },
        Cons =
        {
            "You'll need to manually open removable drives in Explorer",
            "Some legitimate setup discs won't auto-start",
        },
        Recommendation = "Safe single-user default; fully reversible via Undo.",
        RequiresAdmin = false,
        Reversible = true,
        RequiresRestart = false
    };

    public override bool? IsApplied()
        => ReadHkcu(SubKey, ValueName) == "1";

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        var result = new OptimizationResult { Success = true };
        try
        {
            SetRegistryValue(undoService, "HKCU", SubKey, ValueName, 1,
                RegistryValueKind.DWord, "Disable Autoplay for all removable media");
            result.Message = "Autoplay disabled.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not disable Autoplay: {ex.Message}");
        }
        return Task.FromResult(result);
    }
}
