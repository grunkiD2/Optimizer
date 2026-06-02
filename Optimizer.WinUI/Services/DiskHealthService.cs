using System.Text.Json;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class DiskHealthService : IDiskHealthService
{
    private readonly IPowerShellRunner _psRunner;

    public DiskHealthService(IPowerShellRunner psRunner)
    {
        _psRunner = psRunner;
    }

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

        var output = await _psRunner.RunAsync(script);
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
                    Model = SafeGetString(el, "Model"),
                    SerialNumber = SafeGetString(el, "SerialNumber").Trim(),
                    BusType = ConvertBusType(el.TryGetProperty("BusType", out var p3) ? p3 : default),
                    MediaType = ConvertMediaType(el.TryGetProperty("MediaType", out var p4) ? p4 : default),
                    SizeBytes = SafeGetInt64(el, "Size") ?? 0,
                    HealthStatus = ConvertHealthStatus(el.TryGetProperty("HealthStatus", out var p6) ? p6 : default),
                    OperationalStatus = SafeGetString(el, "OperationalStatus"),
                    TemperatureCelsius = SafeGetInt32(el, "Temperature"),
                    WearPercentage = SafeGetInt32(el, "Wear"),
                    PowerOnHours = SafeGetInt64(el, "PowerOnHours"),
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

    // ── Null-safe JSON extractors ────────────────────────────────────────────
    // .NET 10's JsonElement.GetString() throws InvalidOperationException on
    // JsonValueKind.Null. These helpers check ValueKind first.

    private static string SafeGetString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var el)) return "";
        return el.ValueKind == JsonValueKind.String ? (el.GetString() ?? "") : "";
    }

    private static int? SafeGetInt32(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var el)) return null;
        return el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v) ? v : null;
    }

    private static long? SafeGetInt64(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var el)) return null;
        return el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var v) ? v : null;
    }
}
