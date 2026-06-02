using Optimizer.WinUI.Helpers;

namespace Optimizer.WinUI.Models;

public class DiskHealthInfo
{
    public string Model { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string BusType { get; set; } = "";   // SATA, NVMe, USB
    public string MediaType { get; set; } = ""; // SSD, HDD, Unspecified
    public long SizeBytes { get; set; }
    public string HealthStatus { get; set; } = "";   // Healthy, Warning, Unhealthy
    public string OperationalStatus { get; set; } = "";
    public int? TemperatureCelsius { get; set; }
    public int? WearPercentage { get; set; }   // SSD wear level (life used %)
    public long? PowerOnHours { get; set; }
    public long? StartStopCount { get; set; }  // HDD
    public long? ReadErrorRate { get; set; }
    public long? WriteErrorRate { get; set; }
    public bool IsPredictedToFail { get; set; }

    // Display helpers
    public string SizeText => ByteFormatter.Format(SizeBytes);
    public string TemperatureText => TemperatureCelsius.HasValue ? $"{TemperatureCelsius}°C" : "—";
    public string WearText => WearPercentage.HasValue ? $"{100 - WearPercentage}% life remaining" : "—";
    public string PowerOnText => PowerOnHours.HasValue ? $"{PowerOnHours:N0} hours" : "—";

    // Score 0-100 (higher = healthier)
    public int HealthScore
    {
        get
        {
            int score = 100;
            if (HealthStatus.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase)) score -= 60;
            else if (HealthStatus.Equals("Warning", StringComparison.OrdinalIgnoreCase)) score -= 30;
            if (IsPredictedToFail) score -= 50;
            if (TemperatureCelsius > 60) score -= 10;
            if (TemperatureCelsius > 70) score -= 10;
            if (WearPercentage > 80) score -= 20;
            return Math.Max(0, score);
        }
    }

    public string HealthBadge => HealthScore switch
    {
        >= 80 => "Good",
        >= 50 => "Warning",
        _ => "Critical"
    };
}
