using Microsoft.Win32;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.System;

/// <summary>
/// Suppresses the Windows toast notifications that fire on USB device errors and
/// when a charger is below the device's expected wattage. These notifications are
/// noisy for users running docking stations, hubs, or third-party chargers.
/// </summary>
public sealed class DisableUsbNotificationsHandler : OptimizationHandlerBase
{
    private const string SubKey = @"Software\Microsoft\Shell\USB";

    private static readonly (string Name, int Value, string Why)[] Values =
    {
        ("NotifyOnUsbErrors",   0, "Suppress USB error toasts"),
        ("NotifyOnWeakCharger", 0, "Suppress weak-charger toasts"),
    };

    public override string Id => OptimizationIds.DisableUsbNotifications;

    public override OptimizationInfo Info { get; } = new()
    {
        Id = OptimizationIds.DisableUsbNotifications,
        Title = "Silence USB error / weak-charger toasts",
        Summary = "Stops the Windows notifications about USB errors and underpowered chargers. Useful when you're running a dock or third-party charger that's working fine but Windows keeps flagging it.",
        Changes =
        {
            @"HKCU\Software\Microsoft\Shell\USB\NotifyOnUsbErrors = 0",
            @"HKCU\Software\Microsoft\Shell\USB\NotifyOnWeakCharger = 0",
        },
        Pros =
        {
            "No repeated false-positive USB / charger toasts",
            "Quieter Action Center",
        },
        Cons =
        {
            "Genuine USB problems (drive failing, port damaged) become harder to spot",
        },
        Recommendation = "Reasonable when you've confirmed your dock / charger works. Reversible via Undo.",
        RequiresAdmin = false,
        Reversible = true,
        RequiresRestart = false
    };

    public override bool? IsApplied()
    {
        foreach (var (name, value, _) in Values)
            if (ReadHkcu(SubKey, name) != value.ToString())
                return false;
        return true;
    }

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        var result = new OptimizationResult { Success = true };
        try
        {
            foreach (var (name, value, why) in Values)
                SetRegistryValue(undoService, "HKCU", SubKey, name, value, RegistryValueKind.DWord, why);
            result.Message = "USB and weak-charger notifications silenced.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not adjust USB notifications: {ex.Message}");
        }
        return Task.FromResult(result);
    }
}
