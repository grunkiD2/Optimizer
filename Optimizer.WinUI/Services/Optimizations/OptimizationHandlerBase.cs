using System.Diagnostics;
using Microsoft.Win32;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations;

/// <summary>
/// Shared registry and process helpers used by concrete optimization handler implementations.
/// </summary>
public abstract class OptimizationHandlerBase : IOptimizationHandler
{
    public abstract string Id { get; }
    public abstract OptimizationInfo Info { get; }
    public abstract bool? IsApplied();
    public abstract Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService);

    // ── Registry reads (static, no undo capture) ─────────────────────────────

    protected static string? ReadHkcu(string subKey, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey);
        return key?.GetValue(name)?.ToString();
    }

    protected static string? ReadHklm(string subKey, string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(subKey);
        return key?.GetValue(name)?.ToString();
    }

    // ── Registry writes (capture → mutate) ───────────────────────────────────
    // Instance (not static) so the capture carries this handler's Id — audit C5: undo now
    // matches on the optimization id instead of the prose Description that never contained it.

    protected void SetRegistryValue(
        IUndoService undoService,
        string root, string subKey, string valueName,
        object value, RegistryValueKind kind, string description)
    {
        undoService.CaptureRegistry(root, subKey, valueName, description, Id);

        var hive = root == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
        using var key = hive.CreateSubKey(subKey);
        key.SetValue(valueName, value, kind);
        EngineLog.Write($"Set {root}\\{subKey}\\{valueName} = {value}");
    }

    protected void DeleteRegistryValue(
        IUndoService undoService,
        string root, string subKey, string valueName, string description)
    {
        undoService.CaptureRegistry(root, subKey, valueName, description, Id);

        var hive = root == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
        using var key = hive.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
        EngineLog.Write($"Deleted {root}\\{subKey}\\{valueName}");
    }

    // ── Process helper ────────────────────────────────────────────────────────

    protected static void RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(psi);
        process?.WaitForExit(15000);
    }

    // ── Power scheme helper ───────────────────────────────────────────────────

    protected static string GetActivePowerSchemeGuid()
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg", "/getactivescheme")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var process = Process.Start(psi);
            if (process == null) return string.Empty;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            var match = global::System.Text.RegularExpressions.Regex.Match(output, "[0-9a-fA-F-]{36}");
            return match.Success ? match.Value : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    protected static OptimizationResult NotElevated(string why) => new()
    {
        Success = false,
        Message = "Not applied.",
        Errors = new List<string> { why }
    };
}
