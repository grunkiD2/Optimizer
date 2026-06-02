using System.Management;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class DriverDiagnosticsService : IDriverDiagnosticsService
{
    public Task<IReadOnlyList<DriverIssue>> ScanAsync()
    {
        return Task.Run(() =>
        {
            var issues = new List<DriverIssue>();
            try
            {
                using var s = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity");
                foreach (ManagementObject obj in s.Get())
                {
                    var code = Convert.ToInt32(obj["ConfigManagerErrorCode"] ?? 0);
                    var name = obj["Name"]?.ToString() ?? "";
                    var className = obj["PNPClass"]?.ToString() ?? "";
                    var manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                    var status = obj["Status"]?.ToString() ?? "";

                    if (string.IsNullOrEmpty(className)) continue;

                    if (code != 0 || status == "Error" || status == "Degraded")
                    {
                        issues.Add(new DriverIssue
                        {
                            DeviceName = name,
                            DeviceClass = className,
                            Manufacturer = manufacturer,
                            Status = status,
                            ConfigManagerErrorCode = code,
                            ErrorMessage = MapErrorCode(code),
                            IsConflict = code == 12 || code == 18,
                        });
                    }
                }

                // Check for outdated drivers in key device classes
                using var d = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPSignedDriver WHERE DeviceClass IN ('DISPLAY','NET','SYSTEM','PROCESSOR')");
                foreach (ManagementObject obj in d.Get())
                {
                    var driverDate = obj["DriverDate"]?.ToString() ?? "";
                    DateTime? parsedDate = null;
                    if (driverDate.Length >= 8)
                    {
                        try
                        {
                            parsedDate = new DateTime(
                                int.Parse(driverDate.Substring(0, 4)),
                                int.Parse(driverDate.Substring(4, 2)),
                                int.Parse(driverDate.Substring(6, 2)));
                        }
                        catch { }
                    }

                    var isOutdated = parsedDate.HasValue && parsedDate.Value < DateTime.Now.AddDays(-180);
                    if (isOutdated)
                    {
                        var deviceName = obj["DeviceName"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(deviceName) && !issues.Any(i => i.DeviceName == deviceName))
                        {
                            issues.Add(new DriverIssue
                            {
                                DeviceName = deviceName,
                                DeviceClass = obj["DeviceClass"]?.ToString() ?? "",
                                Manufacturer = obj["Manufacturer"]?.ToString() ?? "",
                                DriverVersion = obj["DriverVersion"]?.ToString() ?? "",
                                DriverDate = parsedDate,
                                IsOutdated = true,
                                ErrorMessage = $"Driver is {(DateTime.Now - parsedDate!.Value).Days} days old"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                EngineLog.Error("Driver scan failed", ex);
            }

            return (IReadOnlyList<DriverIssue>)issues;
        });
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
