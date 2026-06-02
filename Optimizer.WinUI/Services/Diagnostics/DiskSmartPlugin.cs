using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Diagnostics;

public sealed class DiskSmartPlugin : IDiagnosticPlugin
{
    private readonly IDiskHealthService _diskHealth;

    public DiskSmartPlugin(IDiskHealthService diskHealth) => _diskHealth = diskHealth;

    public string Name => "Disk Health (SMART)";
    public DiagnosticScanLevel SupportedLevels => DiagnosticScanLevel.Both;

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(IProgress<string>? progress = null)
    {
        var findings = new List<DiagnosticFinding>();
        try
        {
            var disks = await _diskHealth.GetDiskHealthAsync();
            foreach (var disk in disks)
            {
                if (disk.IsPredictedToFail)
                {
                    findings.Add(new DiagnosticFinding
                    {
                        Id = $"disk-fail-{disk.SerialNumber}",
                        Title = $"Drive failure predicted: {disk.Model}",
                        Description = "Disk health status reports the drive is unhealthy. SMART warnings indicate imminent failure.",
                        Recommendation = "Back up data immediately and plan to replace this drive.",
                        Severity = FindingSeverity.Critical,
                        Category = FindingCategory.Hardware
                    });
                }
                else if (disk.TemperatureCelsius > 60)
                {
                    findings.Add(new DiagnosticFinding
                    {
                        Id = $"disk-temp-{disk.SerialNumber}",
                        Title = $"Drive temperature high: {disk.Model}",
                        Description = $"Drive temperature is {disk.TemperatureCelsius}°C.",
                        Recommendation = "Improve airflow or check cooling. Drives above 60°C have shorter lifespans.",
                        Severity = disk.TemperatureCelsius > 70 ? FindingSeverity.Critical : FindingSeverity.Warning,
                        Category = FindingCategory.Hardware
                    });
                }

                if (disk.WearPercentage > 80)
                {
                    findings.Add(new DiagnosticFinding
                    {
                        Id = $"disk-wear-{disk.SerialNumber}",
                        Title = $"SSD nearing end of life: {disk.Model}",
                        Description = $"Drive has used {disk.WearPercentage}% of its write endurance.",
                        Recommendation = "Plan to replace this SSD soon.",
                        Severity = FindingSeverity.Warning,
                        Category = FindingCategory.Hardware
                    });
                }
            }
        }
        catch { }

        return findings;
    }
}
