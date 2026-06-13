using Microsoft.Win32;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.System;

/// <summary>
/// Removes per-user (HKCU) Run entries so they no longer launch at sign-in.
/// Each removed value is captured first so it can be restored via Undo.
/// </summary>
public sealed class DisableStartupProgramsHandler : OptimizationHandlerBase
{
    public override string Id => "DisableStartupPrograms";

    public override OptimizationInfo Info { get; } = new()
    {
        Id = "DisableStartupPrograms",
        Title = "Disable per-user startup programs",
        Summary = "Removes programs that auto-start at sign-in for the current user.",
        Changes = { @"Removes all values under HKCU\…\CurrentVersion\Run (each captured for undo)" },
        Pros = { "Faster sign-in and lower idle resource use" },
        Cons = { "Disables ALL per-user Run entries indiscriminately", "Does not touch machine-wide (HKLM) or Startup-folder items" },
        Recommendation = "Prefer the Startup tab for per-item control. This bulk action removes everything at once; use Undo to restore if needed.",
        SuggestedImplementation = "The Startup tab lets you toggle individual entries (HKCU + HKLM Run) one by one instead of this all-or-nothing action.",
        RequiresAdmin = false,
        Reversible = true,
        // Wipes EVERY per-user Run entry in one shot — gated so it can't run from a bundled/headless
        // surface (tray Quick Cleanup, REST, scheduler, assistant) without explicit confirmation.
        IsDestructive = true
    };

    public override bool? IsApplied() => null; // not deterministic without enumerating the key

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        var result = new OptimizationResult { Success = true };
        const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        try
        {
            string[] names;
            using (var key = Registry.CurrentUser.OpenSubKey(runKey, writable: false))
            {
                names = key?.GetValueNames().Where(n => !string.IsNullOrEmpty(n)).ToArray()
                        ?? Array.Empty<string>();
            }

            foreach (var name in names)
                DeleteRegistryValue(undoService, "HKCU", runKey, name, $"Startup program '{name}'");

            result.Message = names.Length == 0
                ? "No per-user startup programs were found."
                : $"Disabled {names.Length} per-user startup program(s).";
            if (names.Length > 0)
                result.Warnings.Add("Only per-user (HKCU) entries were removed; machine-wide and Startup-folder items are unchanged. Use Undo to restore them.");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not modify startup programs: {ex.Message}");
        }
        return Task.FromResult(result);
    }
}
