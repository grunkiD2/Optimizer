using System.Diagnostics;
using Microsoft.Win32;
using Optimizer.WinUI.Models.Plugins;

namespace Optimizer.WinUI.Services.Plugins;

/// <summary>
/// Applies the declarative changes in an <see cref="OptimizationManifest"/> while enforcing
/// the permission allow-list and capturing undo state via <see cref="IUndoService"/>,
/// so manifest-driven changes are indistinguishable from built-in optimizations in the Undo UI.
/// </summary>
public sealed class DeclarativeChangeExecutor : IDeclarativeChangeExecutor
{
    private readonly IUndoService _undoService;

    public DeclarativeChangeExecutor(IUndoService undoService)
    {
        _undoService = undoService;
    }

    // ── ValidatePermissions ───────────────────────────────────────────────────

    public bool ValidatePermissions(OptimizationManifest manifest, out IReadOnlyList<string> violations)
    {
        var list = new List<string>();

        foreach (var change in manifest.Changes)
        {
            switch (change.Type?.ToLowerInvariant())
            {
                case "registry":
                    if (!string.IsNullOrWhiteSpace(change.Path) &&
                        !ManifestPermissions.IsRegistryPathAllowed(change.Path))
                    {
                        list.Add($"Registry path '{change.Path}' is outside the permitted allow-list.");
                    }
                    break;

                case "file":
                    if (!string.IsNullOrWhiteSpace(change.FilePath) &&
                        !ManifestPermissions.IsFilePathAllowed(change.FilePath))
                    {
                        list.Add($"File path '{change.FilePath}' is outside the permitted allow-list.");
                    }
                    break;

                // service / powercfg / scheduled-task — no path-based restrictions
            }
        }

        violations = list;
        return list.Count == 0;
    }

    // ── IsApplied ─────────────────────────────────────────────────────────────

    public bool IsApplied(OptimizationManifest manifest)
    {
        foreach (var change in manifest.Changes)
        {
            if (!string.Equals(change.Type, "registry", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(change.Path) ||
                string.IsNullOrWhiteSpace(change.Value) ||
                string.IsNullOrWhiteSpace(change.Apply))
            {
                return false;
            }

            var (root, subKey) = SplitRegistryPath(change.Path);
            if (root == null) return false;

            var hive = RootToHive(root);
            if (hive == null) return false;

            try
            {
                using var key = hive.OpenSubKey(subKey);
                var current = key?.GetValue(change.Value!)?.ToString();
                if (!string.Equals(current, change.Apply, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    // ── ApplyAsync ────────────────────────────────────────────────────────────

    public async Task<ChangeResult> ApplyAsync(OptimizationManifest manifest)
    {
        if (!ValidatePermissions(manifest, out var violations))
        {
            var msg = $"Permission violations prevented execution: {string.Join("; ", violations)}";
            EngineLog.Write(msg);
            return new ChangeResult(false, msg);
        }

        var errors = new List<string>();

        foreach (var change in manifest.Changes)
        {
            try
            {
                switch (change.Type?.ToLowerInvariant())
                {
                    case "registry":
                        ApplyRegistryChange(change, manifest.Name);
                        break;

                    case "service":
                        ApplyServiceChange(change, manifest.Name);
                        break;

                    case "file":
                        ApplyFileChange(change);
                        break;

                    case "powercfg":
                        ApplyPowerCfgChange(change);
                        break;

                    case "scheduled-task":
                        ApplyScheduledTaskChange(change);
                        break;

                    default:
                        errors.Add($"Unknown change type '{change.Type}' — skipped.");
                        break;
                }
            }
            catch (Exception ex)
            {
                var err = $"Change type '{change.Type}' failed: {ex.Message}";
                errors.Add(err);
                EngineLog.Write($"[DeclarativeChangeExecutor] {err}");
            }
        }

        // Save undo log after all changes are recorded
        await _undoService.SaveAsync();

        if (errors.Count > 0)
        {
            var summary = $"Applied with {errors.Count} error(s): {string.Join("; ", errors)}";
            return new ChangeResult(false, summary);
        }

        return new ChangeResult(true, $"Successfully applied '{manifest.Name}'.");
    }

    // ── Per-type apply helpers ────────────────────────────────────────────────

    private void ApplyRegistryChange(ManifestChange change, string manifestName)
    {
        if (string.IsNullOrWhiteSpace(change.Path) ||
            string.IsNullOrWhiteSpace(change.Value) ||
            string.IsNullOrWhiteSpace(change.Apply))
        {
            throw new InvalidOperationException("Registry change is missing path, value, or apply.");
        }

        var (root, subKey) = SplitRegistryPath(change.Path);
        if (root == null)
            throw new InvalidOperationException($"Could not determine registry root from path '{change.Path}'.");

        // Capture current state for undo — uses the same IUndoService.CaptureRegistry
        // path as built-in optimizations, so the Undo UI works identically.
        _undoService.CaptureRegistry(root, subKey, change.Value,
            $"[Plugin: {manifestName}] {change.Path}\\{change.Value}");

        // Write the new value
        var hive = RootToHiveWritable(root)
            ?? throw new InvalidOperationException($"Unknown registry root '{root}'.");

        using var key = hive.CreateSubKey(subKey)
            ?? throw new InvalidOperationException($"Cannot open/create registry key '{subKey}'.");

        var kind = ParseValueKind(change.ValueType);
        object boxedValue = BoxRegistryValue(change.Apply, kind);
        key.SetValue(change.Value, boxedValue, kind);

        EngineLog.Write($"[Plugin] Set {root}\\{subKey}\\{change.Value} = {change.Apply} ({kind})");
    }

    private void ApplyServiceChange(ManifestChange change, string manifestName)
    {
        if (string.IsNullOrWhiteSpace(change.ServiceName))
            throw new InvalidOperationException("Service change is missing service_name.");

        // Capture current startup type via registry (HKLM\SYSTEM\CurrentControlSet\Services\<name>\Start)
        var startSubKey = $@"SYSTEM\CurrentControlSet\Services\{change.ServiceName}";
        _undoService.CaptureRegistry("HKLM", startSubKey, "Start",
            $"[Plugin: {manifestName}] Service startup: {change.ServiceName}");

        // Apply startup type
        if (!string.IsNullOrWhiteSpace(change.ApplyStartup))
        {
            var startValue = change.ApplyStartup.ToLowerInvariant() switch
            {
                "disabled" => 4,
                "manual" => 3,
                "automatic" => 2,
                _ => throw new InvalidOperationException($"Unknown startup type '{change.ApplyStartup}'.")
            };

            using var regKey = Registry.LocalMachine.CreateSubKey(startSubKey);
            regKey?.SetValue("Start", startValue, RegistryValueKind.DWord);
        }

        // Apply running state
        if (!string.IsNullOrWhiteSpace(change.ApplyState))
        {
            var target = change.ApplyState.ToLowerInvariant();
            try
            {
                using var sc = new System.ServiceProcess.ServiceController(change.ServiceName);
                if (target == "stopped" && sc.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
                {
                    sc.Stop();
                    sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped,
                        TimeSpan.FromSeconds(15));
                }
                else if (target == "running" && sc.Status != System.ServiceProcess.ServiceControllerStatus.Running)
                {
                    sc.Start();
                    sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running,
                        TimeSpan.FromSeconds(15));
                }
            }
            catch (Exception ex)
            {
                // Changing service running state is best-effort; startup type change above is the reliable part
                EngineLog.Write($"[Plugin] Service state change warning for '{change.ServiceName}': {ex.Message}");
            }
        }

        EngineLog.Write($"[Plugin] Service '{change.ServiceName}': startup={change.ApplyStartup ?? "unchanged"}, state={change.ApplyState ?? "unchanged"}");
    }

    private static void ApplyFileChange(ManifestChange change)
    {
        if (string.IsNullOrWhiteSpace(change.FilePath))
            throw new InvalidOperationException("File change is missing file_path.");

        // Canonicalize the path before acting on it.
        // Path.GetFullPath resolves '..' segments, eliminating traversal attacks like
        // %TEMP%\..\..\..\Windows\System32\...  — the permission check at ValidatePermissions
        // time uses the same canonicalization, so both agree on the resolved path.
        string canonicalized;
        try
        {
            canonicalized = Path.GetFullPath(Environment.ExpandEnvironmentVariables(change.FilePath));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"File path '{change.FilePath}' is invalid: {ex.Message}");
        }

        // Defense in depth: re-check permission on the canonicalized path immediately before
        // the file operation, in case the permission check at manifest-load time was bypassed.
        if (!ManifestPermissions.IsFilePathAllowed(canonicalized))
        {
            throw new InvalidOperationException(
                $"File path '{canonicalized}' is outside the permitted allow-list (post-canonicalization check).");
        }

        switch (change.FileAction?.ToLowerInvariant())
        {
            case "delete":
                if (File.Exists(canonicalized))
                {
                    File.Delete(canonicalized);
                    EngineLog.Write($"[Plugin] Deleted file: {canonicalized}");
                }
                else if (Directory.Exists(canonicalized))
                {
                    Directory.Delete(canonicalized, recursive: true);
                    EngineLog.Write($"[Plugin] Deleted directory: {canonicalized}");
                }
                break;

            case "clear":
                if (File.Exists(canonicalized))
                {
                    File.WriteAllBytes(canonicalized, Array.Empty<byte>());
                    EngineLog.Write($"[Plugin] Cleared file: {canonicalized}");
                }
                break;

            default:
                throw new InvalidOperationException($"Unknown file action '{change.FileAction}'.");
        }
        // NOTE: file changes are NOT reversible — no undo entry is registered.
    }

    private static void ApplyPowerCfgChange(ManifestChange change)
    {
        if (string.IsNullOrWhiteSpace(change.PowerCfgArgs))
            throw new InvalidOperationException("Powercfg change is missing power_cfg_args.");

        UndoService.RunPowerCfg(change.PowerCfgArgs);
        EngineLog.Write($"[Plugin] powercfg {change.PowerCfgArgs}");
        // NOTE: powercfg changes are non-trivially reversible; no undo entry is registered.
    }

    private static void ApplyScheduledTaskChange(ManifestChange change)
    {
        if (string.IsNullOrWhiteSpace(change.TaskName) || string.IsNullOrWhiteSpace(change.TaskAction))
            throw new InvalidOperationException("Scheduled-task change is missing task_name or task_action.");

        // Use ArgumentList (not a single argument string) so the runtime handles quoting/escaping.
        // This prevents argument injection via crafted task names containing '"' or other shell chars.
        // ManifestParser.Validate also rejects task names with '"' or control characters as defense in depth.
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        psi.ArgumentList.Add("/Change");

        // Add the enable/disable flag as a separate argument — safely escaped by the runtime
        var verbFlag = change.TaskAction.ToLowerInvariant() switch
        {
            "disable" => "/DISABLE",
            "enable"  => "/ENABLE",
            _ => throw new InvalidOperationException($"Unknown task action '{change.TaskAction}'.")
        };
        psi.ArgumentList.Add(verbFlag);

        psi.ArgumentList.Add("/TN");
        psi.ArgumentList.Add(change.TaskName);  // runtime escapes this; injection is not possible

        using var proc = Process.Start(psi);
        proc?.WaitForExit(10000);

        EngineLog.Write($"[Plugin] schtasks /Change {verbFlag} /TN {change.TaskName}");
    }

    // ── Registry path helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Splits a full registry path like "HKLM\SOFTWARE\Foo\Bar" into root "HKLM"
    /// and subkey "SOFTWARE\Foo\Bar".
    /// Also normalises HKEY_LOCAL_MACHINE → HKLM and HKEY_CURRENT_USER → HKCU.
    ///
    /// Returns <c>(null, original)</c> for any of:
    ///   • unrecognised root hive
    ///   • no backslash separator
    ///   • subkey containing '.' or '..' path-traversal segments
    /// A null root causes all downstream callers to reject the path — defence in depth
    /// alongside <see cref="ManifestPermissions.IsRegistryPathAllowed"/>.
    /// </summary>
    private static (string? root, string subKey) SplitRegistryPath(string path)
    {
        // Normalise long names
        path = path
            .Replace("HKEY_LOCAL_MACHINE", "HKLM", StringComparison.OrdinalIgnoreCase)
            .Replace("HKEY_CURRENT_USER", "HKCU", StringComparison.OrdinalIgnoreCase);

        var idx = path.IndexOf('\\', StringComparison.Ordinal);
        if (idx < 0) return (null, path);

        var root   = path[..idx].ToUpperInvariant();
        var subKey = path[(idx + 1)..];

        if (root is not ("HKLM" or "HKCU"))
            return (null, subKey);

        // Reject any '.' or '..' segment — prevents HKCU\Software\Foo\..\..\..\SYSTEM\...
        var segments = subKey.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s is "." or ".."))
            return (null, subKey);

        return (root, subKey);
    }

    private static RegistryKey? RootToHive(string root) => root switch
    {
        "HKLM" => Registry.LocalMachine,
        "HKCU" => Registry.CurrentUser,
        _ => null
    };

    private static RegistryKey? RootToHiveWritable(string root) => root switch
    {
        "HKLM" => Registry.LocalMachine,
        "HKCU" => Registry.CurrentUser,
        _ => null
    };

    private static RegistryValueKind ParseValueKind(string? valueType) =>
        valueType?.ToLowerInvariant() switch
        {
            "dword" => RegistryValueKind.DWord,
            "qword" => RegistryValueKind.QWord,
            "string" => RegistryValueKind.String,
            null or "" => RegistryValueKind.DWord,  // default to DWORD for optimization tweaks
            _ => RegistryValueKind.String
        };

    private static object BoxRegistryValue(string raw, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => Convert.ToInt32(raw),
        RegistryValueKind.QWord => Convert.ToInt64(raw),
        _ => raw
    };
}
