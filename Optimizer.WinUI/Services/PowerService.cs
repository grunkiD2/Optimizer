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
        return await RunPowerCfgAsync($"/setactive {guid}") != null;
    }

    public async Task<bool> CreateUltimatePerformancePlanAsync()
    {
        // Ultimate Performance plan template GUID
        return await RunPowerCfgAsync("/duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61") != null;
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
