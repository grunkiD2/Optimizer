using System.Diagnostics;

namespace Optimizer.WinUI.Services;

/// <summary>
/// Implements PnP device listing (via Win32_PnPEntity) and enable/disable
/// (via pnputil.exe /enable-device and /disable-device, available on Windows 10 2004+).
///
/// Safety rules enforced by this service (not just the UI):
///   - Any device whose PNP class is in <see cref="CriticalClasses"/> is flagged IsCritical.
///   - <see cref="SetEnabledAsync"/> refuses to disable a critical device and logs the attempt.
///   - pnputil arguments are passed via ArgumentList (never string-interpolated) to eliminate
///     command-injection risk.
/// </summary>
public class DeviceControlService : IDeviceControlService
{
    private readonly IWmiQueryService _wmi;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// PNP device class names (case-insensitive) that represent hardware essential to
    /// system operation. Devices in these classes are ALWAYS flagged critical — they can
    /// never be disabled through this service.
    /// </summary>
    public static readonly HashSet<string> CriticalClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Processor",        // CPU
        "System",           // HAL / ACPI system devices
        "Computer",         // Root computer node
        "ACPI",             // Power/ACPI
        "SecurityDevices",  // TPM
        "SCSIAdapter",      // SCSI / storage controller
        "HDC",              // IDE / storage host controller
        "DiskDrive",        // Physical disks
        "Volume",           // Logical volumes
        "StorageController",// NVMe / generic storage controllers
        "Display",          // GPUs / display adapters (never disable the display)
        "Monitor",          // Connected monitors
        "HIDClass",         // HID bus (umbrella for keyboards/mice via HID)
        "Keyboard",         // Keyboards
        "Mouse",            // Mice
    };

    public DeviceControlService(IWmiQueryService wmi)
    {
        _wmi = wmi;
    }

    // ── IsCritical classification ─────────────────────────────────────────────

    /// <summary>
    /// Pure static method: returns true when the device's PNP class indicates it
    /// is critical to system operation. Extracted for unit-test coverage.
    /// </summary>
    public static bool ClassifyCritical(string pnpClass, string name)
    {
        if (string.IsNullOrWhiteSpace(pnpClass)) return false;

        if (CriticalClasses.Contains(pnpClass)) return true;

        // Additional name-based heuristics for devices whose class is generic
        var n = name ?? "";
        if (n.Contains("boot", StringComparison.OrdinalIgnoreCase)) return true;
        if (n.Contains("system", StringComparison.OrdinalIgnoreCase) &&
            n.Contains("bus", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    // ── Status mapping ────────────────────────────────────────────────────────

    /// <summary>
    /// Maps Win32_PnPEntity.ConfigManagerErrorCode to a human-readable status string
    /// and the IsEnabled flag.
    /// Code 22 = device is disabled by the user.
    /// Code 0 = working normally (enabled).
    /// All other codes = some kind of error (still "enabled" in the sense that the OS
    /// hasn't intentionally disabled it, but something is wrong).
    /// </summary>
    public static (string status, bool isEnabled) MapStatus(int errorCode)
    {
        return errorCode switch
        {
            0  => ("OK", true),
            22 => ("Disabled", false),
            1  => ("Error: Not configured", true),
            10 => ("Error: Cannot start", true),
            28 => ("Error: Drivers not installed", true),
            43 => ("Error: Device failure", true),
            _  => ($"Error (code {errorCode})", true),
        };
    }

    // ── Listing ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PnpDevice>> ListDevicesAsync(string? classFilter = null)
    {
        try
        {
            var wql = string.IsNullOrWhiteSpace(classFilter)
                ? "SELECT * FROM Win32_PnPEntity WHERE PNPClass IS NOT NULL"
                : $"SELECT * FROM Win32_PnPEntity WHERE PNPClass='{EscapeWql(classFilter)}'";

            var rows = await _wmi.QueryAsync(
                wql,
                obj =>
                {
                    var name      = obj["Name"]?.ToString() ?? "(unknown)";
                    var pnpClass  = obj["PNPClass"]?.ToString() ?? "";
                    var errorCode = Convert.ToInt32(obj["ConfigManagerErrorCode"] ?? 0);
                    var instanceId = obj["PNPDeviceID"]?.ToString() ?? "";

                    var (status, isEnabled) = MapStatus(errorCode);
                    var isCritical = ClassifyCritical(pnpClass, name);

                    return new PnpDevice
                    {
                        InstanceId = instanceId,
                        Name       = name,
                        Class      = pnpClass,
                        Status     = status,
                        IsEnabled  = isEnabled,
                        IsCritical = isCritical,
                    };
                },
                cacheTtl: Ttl);

            return rows
                .Where(d => !string.IsNullOrWhiteSpace(d.InstanceId))
                .OrderBy(d => d.Class)
                .ThenBy(d => d.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            EngineLog.Error("DeviceControlService.ListDevicesAsync failed", ex);
            return [];
        }
    }

    // ── Enable / disable ──────────────────────────────────────────────────────

    public async Task<bool> SetEnabledAsync(string instanceId, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return false;

        // Re-check criticality server-side (don't trust UI-side flag alone)
        var devices = await ListDevicesAsync();
        var device  = devices.FirstOrDefault(d =>
            string.Equals(d.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));

        if (device == null)
        {
            EngineLog.Write($"DeviceControlService: device '{instanceId}' not found.");
            return false;
        }

        if (device.IsCritical && !enabled)
        {
            EngineLog.Write($"DeviceControlService: refused to disable critical device '{device.Name}'.");
            return false;
        }

        var verb = enabled ? "/enable-device" : "/disable-device";

        try
        {
            // Use ArgumentList — NEVER string-interpolated — to prevent injection.
            var psi = new ProcessStartInfo
            {
                FileName  = "pnputil.exe",
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            psi.ArgumentList.Add(verb);
            psi.ArgumentList.Add(instanceId);

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var ok = proc.ExitCode == 0;
            EngineLog.Write(ok
                ? $"pnputil {verb} '{instanceId}' succeeded."
                : $"pnputil {verb} '{instanceId}' exited {proc.ExitCode}: {stderr.Trim()}");

            // Invalidate the WMI cache so the next ListDevicesAsync returns fresh state
            if (ok) _wmi.InvalidateCache();

            return ok;
        }
        catch (Exception ex)
        {
            EngineLog.Error($"DeviceControlService.SetEnabledAsync failed for '{instanceId}'", ex);
            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Escapes a string for literal use in a WQL WHERE clause.</summary>
    private static string EscapeWql(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);
}
