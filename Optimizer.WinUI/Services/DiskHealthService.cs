using System.Diagnostics;
using System.Text.Json;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class DiskHealthService : IDiskHealthService
{
    public async Task<IReadOnlyList<DiskHealthInfo>> GetDiskHealthAsync()
    {
        var script = @"
            Get-PhysicalDisk | ForEach-Object {
                $disk = $_
                $rel = $null
                try { $rel = $disk | Get-StorageReliabilityCounter -ErrorAction SilentlyContinue } catch {}
                [PSCustomObject]@{
                    Model = $disk.FriendlyName
                    SerialNumber = $disk.SerialNumber
                    BusType = $disk.BusType
                    MediaType = $disk.MediaType
                    Size = $disk.Size
                    HealthStatus = $disk.HealthStatus
                    OperationalStatus = ($disk.OperationalStatus -join ',')
                    Temperature = $rel.Temperature
                    Wear = $rel.Wear
                    PowerOnHours = $rel.PowerOnHours
                    StartStopCycleCount = $rel.StartStopCycleCount
                    ReadErrorsTotal = $rel.ReadErrorsTotal
                    WriteErrorsTotal = $rel.WriteErrorsTotal
                }
            } | ConvertTo-Json -Compress -Depth 3
        ";

        var output = await RunPowerShellAsync(script);
        if (string.IsNullOrWhiteSpace(output)) return [];

        try
        {
            // Handle both single object and array outputs
            var trimmed = output.Trim();
            if (!trimmed.StartsWith('[')) trimmed = "[" + trimmed + "]";

            using var doc = JsonDocument.Parse(trimmed);
            var list = new List<DiskHealthInfo>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var info = new DiskHealthInfo
                {
                    Model = el.TryGetProperty("Model", out var p1) ? (p1.GetString() ?? "") : "",
                    SerialNumber = el.TryGetProperty("SerialNumber", out var p2) ? (p2.GetString()?.Trim() ?? "") : "",
                    BusType = ConvertBusType(el.TryGetProperty("BusType", out var p3) ? p3 : default),
                    MediaType = ConvertMediaType(el.TryGetProperty("MediaType", out var p4) ? p4 : default),
                    SizeBytes = el.TryGetProperty("Size", out var p5) && p5.TryGetInt64(out var sz) ? sz : 0,
                    HealthStatus = ConvertHealthStatus(el.TryGetProperty("HealthStatus", out var p6) ? p6 : default),
                    OperationalStatus = el.TryGetProperty("OperationalStatus", out var p7) ? (p7.GetString() ?? "") : "",
                    TemperatureCelsius = el.TryGetProperty("Temperature", out var p8) && p8.TryGetInt32(out var t) ? t : null,
                    WearPercentage = el.TryGetProperty("Wear", out var p9) && p9.TryGetInt32(out var w) ? w : null,
                    PowerOnHours = el.TryGetProperty("PowerOnHours", out var p10) && p10.TryGetInt64(out var ph) ? ph : null,
                };
                info.IsPredictedToFail = info.HealthStatus.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase)
                                      || info.OperationalStatus.Contains("Predictive Failure", StringComparison.OrdinalIgnoreCase);
                list.Add(info);
            }
            return list;
        }
        catch (Exception ex)
        {
            EngineLog.Error("Failed to parse disk health JSON", ex);
            return [];
        }
    }

    // BusType is an enum int — 11 = SATA, 17 = NVMe, 7 = USB
    private static string ConvertBusType(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number => el.GetInt32() switch
        {
            11 => "SATA",
            17 => "NVMe",
            7 => "USB",
            8 => "RAID",
            1 => "SCSI",
            _ => $"Type{el.GetInt32()}"
        },
        JsonValueKind.String => el.GetString() ?? "Unknown",
        _ => "Unknown"
    };

    // MediaType: 3 = HDD, 4 = SSD, 5 = SCM
    private static string ConvertMediaType(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number => el.GetInt32() switch
        {
            3 => "HDD",
            4 => "SSD",
            5 => "SCM",
            _ => "Unknown"
        },
        JsonValueKind.String => el.GetString() ?? "Unknown",
        _ => "Unknown"
    };

    private static string ConvertHealthStatus(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number => el.GetInt32() switch
        {
            0 => "Healthy",
            1 => "Warning",
            2 => "Unhealthy",
            _ => "Unknown"
        },
        JsonValueKind.String => el.GetString() ?? "Unknown",
        _ => "Unknown"
    };

    private static async Task<string?> RunPowerShellAsync(string script)
    {
        try
        {
            // Write script to a temp file to avoid command-line escaping issues
            var tempFile = Path.GetTempFileName() + ".ps1";
            await File.WriteAllTextAsync(tempFile, script);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempFile}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                var stdout = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                return proc.ExitCode == 0 ? stdout : null;
            }
            finally
            {
                try { File.Delete(tempFile); } catch { /* best-effort cleanup */ }
            }
        }
        catch { return null; }
    }
}
