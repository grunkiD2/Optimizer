using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class DriverDiagnosticsService : IDriverDiagnosticsService
{
    private readonly IWmiQueryService _wmi;
    private static readonly TimeSpan DriverTtl = TimeSpan.FromMinutes(5);

    public DriverDiagnosticsService(IWmiQueryService wmi)
    {
        _wmi = wmi;
    }

    public async Task<IReadOnlyList<DriverIssue>> ScanAsync()
    {
        var issues = new List<DriverIssue>();
        try
        {
            // ── Pass 1: devices with non-zero error codes ─────────────────────
            var pnpEntities = await _wmi.QueryAsync(
                "SELECT * FROM Win32_PnPEntity",
                obj => new
                {
                    Name         = obj["Name"]?.ToString() ?? "",
                    ClassName    = obj["PNPClass"]?.ToString() ?? "",
                    Manufacturer = obj["Manufacturer"]?.ToString() ?? "",
                    Status       = obj["Status"]?.ToString() ?? "",
                    Code         = Convert.ToInt32(obj["ConfigManagerErrorCode"] ?? 0),
                },
                cacheTtl: DriverTtl);

            foreach (var e in pnpEntities)
            {
                if (string.IsNullOrEmpty(e.ClassName)) continue;
                if (e.Code != 0 || e.Status == "Error" || e.Status == "Degraded")
                {
                    issues.Add(new DriverIssue
                    {
                        DeviceName             = e.Name,
                        DeviceClass            = e.ClassName,
                        Manufacturer           = e.Manufacturer,
                        Status                 = e.Status,
                        ConfigManagerErrorCode = e.Code,
                        ErrorMessage           = MapErrorCode(e.Code),
                        IsConflict             = e.Code == 12 || e.Code == 18,
                    });
                }
            }

            // ── Pass 2: outdated drivers in key device classes ────────────────
            var signedDrivers = await _wmi.QueryAsync(
                "SELECT * FROM Win32_PnPSignedDriver WHERE DeviceClass IN ('DISPLAY','NET','SYSTEM','PROCESSOR')",
                obj =>
                {
                    var driverDateStr = obj["DriverDate"]?.ToString() ?? "";
                    DateTime? parsedDate = null;
                    if (driverDateStr.Length >= 8)
                    {
                        try
                        {
                            parsedDate = new DateTime(
                                int.Parse(driverDateStr[..4]),
                                int.Parse(driverDateStr.Substring(4, 2)),
                                int.Parse(driverDateStr.Substring(6, 2)));
                        }
                        catch { }
                    }
                    return new
                    {
                        DeviceName    = obj["DeviceName"]?.ToString() ?? "",
                        DeviceClass   = obj["DeviceClass"]?.ToString() ?? "",
                        Manufacturer  = obj["Manufacturer"]?.ToString() ?? "",
                        DriverVersion = obj["DriverVersion"]?.ToString() ?? "",
                        DriverDate    = parsedDate,
                    };
                },
                cacheTtl: DriverTtl);

            foreach (var d in signedDrivers)
            {
                var isOutdated = d.DriverDate.HasValue && d.DriverDate.Value < DateTime.Now.AddDays(-180);
                if (isOutdated && !string.IsNullOrEmpty(d.DeviceName)
                    && !issues.Any(i => i.DeviceName == d.DeviceName))
                {
                    issues.Add(new DriverIssue
                    {
                        DeviceName    = d.DeviceName,
                        DeviceClass   = d.DeviceClass,
                        Manufacturer  = d.Manufacturer,
                        DriverVersion = d.DriverVersion,
                        DriverDate    = d.DriverDate,
                        IsOutdated    = true,
                        ErrorMessage  = $"Driver is {(DateTime.Now - d.DriverDate!.Value).Days} days old"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error("Driver scan failed", ex);
        }

        return issues;
    }

    private static string MapErrorCode(int code) => code switch
    {
        0  => "Working properly",
        1  => "Device is not configured correctly",
        10 => "Device cannot start",
        12 => "Insufficient resources",
        18 => "Reinstall driver required",
        22 => "Device is disabled",
        28 => "Drivers not installed",
        37 => "Cannot initialize",
        39 => "Driver is corrupted",
        _  => $"Error code {code}"
    };
}
