using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Optimizer.WinUI.Services;

public class PowerService : IPowerService
{
    public async Task<IReadOnlyList<PowerPlan>> GetPowerPlansAsync()
    {
        var output = await RunPowerCfgAsync("/list");
        if (output == null) return [];

        var plans = new List<PowerPlan>();
        // Output format: "Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance) *"
        foreach (var line in output.Split('\n'))
        {
            var match = Regex.Match(line, @"GUID:\s*([0-9a-fA-F-]+)\s*\(([^)]+)\)(\s*\*)?");
            if (match.Success)
            {
                plans.Add(new PowerPlan
                {
                    Guid = Guid.Parse(match.Groups[1].Value.Trim()),
                    Name = match.Groups[2].Value.Trim(),
                    IsActive = match.Groups[3].Success
                });
            }
        }
        return plans;
    }

    public async Task<bool> SetActivePowerPlanAsync(Guid guid)
    {
        EngineLog.Write($"[PowerService] Switching active power plan to {guid}");
        return await RunPowerCfgAsync($"/setactive {guid}") != null;
    }

    // The built-in Ultimate Performance scheme template; duplicates inherit its (localized) name.
    private const string UltimateTemplateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    public async Task<bool> CreateUltimatePerformancePlanAsync()
    {
        // Idempotency guard (audit 4a-3): /duplicatescheme mints a NEW random-GUID plan on every
        // call, so re-clicking the button used to litter the list with identical copies. The plan's
        // display name is LOCALIZED (e.g. en-US "Ultimate Performance", da-DK "Ultimativ ydeevne"),
        // so a hard-coded English substring both MISSES the real localized plan and FALSELY matches
        // unrelated third-party plans ("…Ultimate Power Plan"). Resolve the template's friendly name
        // in the OS language at runtime and skip duplication if a plan with that exact name exists.
        var templateName = await GetSchemeFriendlyNameAsync(UltimateTemplateGuid);
        if (templateName != null)
        {
            var existing = await GetPowerPlansAsync();
            if (existing.Any(p => string.Equals(p.Name, templateName, StringComparison.OrdinalIgnoreCase)))
            {
                EngineLog.Write($"[PowerService] '{templateName}' power plan already exists — not duplicating");
                return true;
            }
        }

        EngineLog.Write("[PowerService] Creating Ultimate Performance power plan");
        return await RunPowerCfgAsync($"/duplicatescheme {UltimateTemplateGuid}") != null;
    }

    /// <summary>
    /// Resolves a power scheme's localized friendly name via <c>powercfg /query &lt;guid&gt;</c>.
    /// The header line reads "…GUID: &lt;guid&gt;  (Friendly Name)"; the label itself may be
    /// localized, so we key off the GUID we queried rather than an English label. Null if the
    /// scheme can't be queried (e.g. the template is unavailable on this edition).
    /// </summary>
    private static async Task<string?> GetSchemeFriendlyNameAsync(string guid)
    {
        var output = await RunPowerCfgAsync($"/query {guid}");
        if (output == null) return null;
        var m = Regex.Match(output, Regex.Escape(guid) + @"\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    public bool IsGameModeEnabled()
    {
        try
        {
            var v = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\GameBar",
                "AllowAutoGameMode",
                0);
            return Convert.ToInt32(v ?? 0) == 1;
        }
        catch { return false; }
    }

    public Task<bool> SetGameModeAsync(bool enabled)
    {
        EngineLog.Write($"[PowerService] Game Mode → {(enabled ? "ON" : "OFF")}");
        try
        {
            Microsoft.Win32.Registry.SetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\GameBar",
                "AllowAutoGameMode",
                enabled ? 1 : 0,
                Microsoft.Win32.RegistryValueKind.DWord);
            Microsoft.Win32.Registry.SetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\GameBar",
                "AutoGameModeEnabled",
                enabled ? 1 : 0,
                Microsoft.Win32.RegistryValueKind.DWord);
            return Task.FromResult(true);
        }
        catch { return Task.FromResult(false); }
    }

    private static async Task<string?> RunPowerCfgAsync(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg.exe", args)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0 ? stdout : null;
        }
        catch { return null; }
    }
}
