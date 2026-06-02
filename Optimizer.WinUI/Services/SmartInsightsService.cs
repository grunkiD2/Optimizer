using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class SmartInsightsService : ISmartInsightsService
{
    private readonly ISensorService _sensors;
    private readonly IHardwareInfoService _hardware;
    private readonly IServiceManagerService _services;
    private readonly ISystemMonitorService _monitor;
    private readonly IWmiQueryService _wmi;

    private static readonly TimeSpan BatteryTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan GpuDriverTtl = TimeSpan.FromMinutes(5);

    public SmartInsightsService(
        ISensorService sensors,
        IHardwareInfoService hardware,
        IServiceManagerService services,
        ISystemMonitorService monitor,
        IWmiQueryService wmi)
    {
        _sensors = sensors;
        _hardware = hardware;
        _services = services;
        _monitor = monitor;
        _wmi = wmi;
    }

    public async Task<IReadOnlyList<SmartInsight>> GenerateAsync()
    {
        var insights = new List<SmartInsight>();
        var now = DateTime.Now;

        // ── 1. Long uptime ────────────────────────────────────────────────────
        var uptimeDays = Environment.TickCount64 / 1000.0 / 86400.0;
        if (uptimeDays > 7)
        {
            insights.Add(new SmartInsight
            {
                Id = "long-uptime",
                Title = "PC has been running for a while",
                Body = $"Uptime is {uptimeDays:F0} days. Performance often improves after a restart due to " +
                       "memory cleanup and installation of pending updates.",
                SupportingDataText = $"{uptimeDays:F0} days uptime",
                Category = FindingCategory.Maintenance,
                GeneratedAt = now
            });
        }

        // ── 2. Battery health ─────────────────────────────────────────────────
        try
        {
            var batteries = await _wmi.QueryAsync(
                "SELECT * FROM Win32_Battery",
                obj => (
                    Designed: Convert.ToInt32(obj["DesignCapacity"] ?? 0),
                    Full: Convert.ToInt32(obj["FullChargeCapacity"] ?? 0)),
                cacheTtl: BatteryTtl);

            var battery = batteries.FirstOrDefault();
            if (battery != default && battery.Designed > 0 && battery.Full > 0)
            {
                var healthPct = 100.0 * battery.Full / battery.Designed;
                if (healthPct < 80)
                {
                    insights.Add(new SmartInsight
                    {
                        Id = "battery-health",
                        Title = "Battery is showing wear",
                        Body = $"Your battery's full-charge capacity is {healthPct:F0}% of its original design " +
                               "capacity. Consider replacement when it drops below 70%.",
                        SupportingDataText = $"{battery.Full} mWh / {battery.Designed} mWh designed ({healthPct:F0}%)",
                        Category = FindingCategory.Hardware,
                        GeneratedAt = now
                    });
                }
            }
        }
        catch { /* Desktop or unavailable — skip */ }

        // ── 3. No restore point in 30 days ────────────────────────────────────
        try
        {
            var restorePoints = (await _wmi.QueryAsync(
                "SELECT * FROM SystemRestore ORDER BY CreationTime DESC",
                obj => obj["CreationTime"]?.ToString() ?? "",
                cacheTtl: null,
                scope: @"root\default")).ToList();
            if (restorePoints.Count == 0)
            {
                insights.Add(new SmartInsight
                {
                    Id = "no-restore-point",
                    Title = "No system restore points found",
                    Body = "There are no System Restore points on this machine. Creating restore points before " +
                           "making system changes gives you an easy rollback option.",
                    SupportingDataText = "0 restore points",
                    Category = FindingCategory.Maintenance,
                    GeneratedAt = now
                });
            }
            else
            {
                // CreationTime is a string like "20240101120000.000000-000"
                var creationStr = restorePoints[0];
                if (creationStr.Length >= 14
                    && DateTime.TryParseExact(
                        creationStr[..14],
                        "yyyyMMddHHmmss",
                        null,
                        System.Globalization.DateTimeStyles.None,
                        out var created))
                {
                    var daysSince = (now - created).TotalDays;
                    if (daysSince > 30)
                    {
                        insights.Add(new SmartInsight
                        {
                            Id = "old-restore-point",
                            Title = "Restore point is over 30 days old",
                            Body = $"Your most recent System Restore point is {daysSince:F0} days old. " +
                                   "Consider creating a fresh one before making system changes.",
                            SupportingDataText = $"Last restore point: {created:MMM d, yyyy}",
                            Category = FindingCategory.Maintenance,
                            GeneratedAt = now
                        });
                    }
                }
            }
        }
        catch { /* Restore points may require elevation — skip */ }

        // ── 4. GPU driver age ─────────────────────────────────────────────────
        try
        {
            var gpus = await _wmi.QueryAsync(
                "SELECT * FROM Win32_VideoController",
                obj => (
                    Name:          obj["Name"]?.ToString() ?? "",
                    DriverVersion: obj["DriverVersion"]?.ToString() ?? "",
                    DriverDate:    obj["DriverDate"]?.ToString() ?? ""),
                cacheTtl: GpuDriverTtl);

            var gpu = gpus.FirstOrDefault(g =>
                !string.IsNullOrEmpty(g.Name) &&
                !g.Name.Contains("Microsoft", StringComparison.OrdinalIgnoreCase));

            if (gpu != default)
            {
                var driverDateStr = gpu.DriverDate;
                if (driverDateStr.Length >= 8
                    && DateTime.TryParseExact(
                        driverDateStr[..8],
                        "yyyyMMdd",
                        null,
                        System.Globalization.DateTimeStyles.None,
                        out var driverDate))
                {
                    var ageDays = (now - driverDate).TotalDays;
                    if (ageDays > 90)
                    {
                        var ageMonths = (int)(ageDays / 30);
                        insights.Add(new SmartInsight
                        {
                            Id = "old-gpu-driver",
                            Title = "GPU driver is over 3 months old",
                            Body = $"Your GPU driver (version {gpu.DriverVersion}) is approximately {ageMonths} months old. " +
                                   "Driver updates often include performance improvements and bug fixes for the latest games.",
                            SupportingDataText = $"Driver dated {driverDate:MMM d, yyyy} ({ageDays:F0} days ago)",
                            Category = FindingCategory.Performance,
                            GeneratedAt = now
                        });
                    }
                }
            }
        }
        catch { }

        // ── 5. CPU vs GPU bottleneck heuristic (live sensor data) ─────────────
        if (_sensors.IsAvailable)
        {
            try
            {
                var snap = _sensors.GetSnapshot();
                var cpuLoad = snap.CpuLoads.FirstOrDefault()?.Value ?? 0;
                var gpuLoad = snap.GpuLoads.FirstOrDefault()?.Value ?? 0;
                if (cpuLoad > 90 && gpuLoad < 60 && gpuLoad > 0)
                {
                    insights.Add(new SmartInsight
                    {
                        Id = "cpu-bottleneck",
                        Title = "CPU appears to be bottlenecking",
                        Body = $"Right now your CPU is at {cpuLoad:F0}% load while your GPU is only at {gpuLoad:F0}%. " +
                               "In gaming workloads this means your CPU is limiting frame rates. " +
                               "Consider reducing CPU-heavy background tasks or upgrading to a faster CPU.",
                        SupportingDataText = $"CPU {cpuLoad:F0}% / GPU {gpuLoad:F0}%",
                        Category = FindingCategory.Performance,
                        GeneratedAt = now
                    });
                }
            }
            catch { }
        }

        // ── 6. Memory pressure (current snapshot) ────────────────────────────
        try
        {
            var snap = _monitor.CollectSnapshot();
            if (snap.TotalPhysicalMemory > 0)
            {
                var usedBytes = snap.TotalPhysicalMemory - snap.AvailablePhysicalMemory;
                var usedPct = 100.0 * usedBytes / snap.TotalPhysicalMemory;
                if (usedPct > 85)
                {
                    var totalGb = snap.TotalPhysicalMemory / (1024.0 * 1024 * 1024);
                    insights.Add(new SmartInsight
                    {
                        Id = "memory-pressure",
                        Title = "Memory usage is critically high",
                        Body = $"You are currently using {usedPct:F0}% of your {totalGb:F1} GB RAM. " +
                               "High memory usage leads to increased disk paging and degraded performance. " +
                               "Consider closing unused applications or upgrading your RAM.",
                        SupportingDataText = $"{usedPct:F0}% used ({totalGb:F1} GB total)",
                        Category = FindingCategory.Performance,
                        GeneratedAt = now
                    });
                }
            }
        }
        catch { }

        // ── 7. Disk space check ───────────────────────────────────────────────
        try
        {
            var systemDrive = DriveInfo.GetDrives()
                .FirstOrDefault(d => d.IsReady && d.Name.StartsWith(
                    Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System))!,
                    StringComparison.OrdinalIgnoreCase));
            if (systemDrive != null)
            {
                var usedPct = 100.0 * (systemDrive.TotalSize - systemDrive.AvailableFreeSpace) / systemDrive.TotalSize;
                if (usedPct > 85)
                {
                    var freeGb = systemDrive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                    var totalGb = systemDrive.TotalSize / (1024.0 * 1024 * 1024);
                    insights.Add(new SmartInsight
                    {
                        Id = "disk-space-low",
                        Title = "System drive is nearly full",
                        Body = $"Your system drive ({systemDrive.Name}) has only {freeGb:F1} GB free of {totalGb:F1} GB. " +
                               "Low disk space can slow down Windows significantly and may cause issues with updates. " +
                               "Use the Cleanup page to reclaim space.",
                        SupportingDataText = $"{usedPct:F0}% used, {freeGb:F1} GB free",
                        Category = FindingCategory.Storage,
                        GeneratedAt = now
                    });
                }
            }
        }
        catch { }

        // ── 8. Service bloat ──────────────────────────────────────────────────
        try
        {
            var allServices = await _services.GetServicesAsync();
            var bloatCount = allServices.Count(s =>
                string.Equals(s.Recommendation, "Safe", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(s.StartupType, "Automatic", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.StartupType, "AutoDelayed", StringComparison.OrdinalIgnoreCase))
                && string.Equals(s.Status, "Running", StringComparison.OrdinalIgnoreCase));

            if (bloatCount > 10)
            {
                insights.Add(new SmartInsight
                {
                    Id = "service-bloat",
                    Title = "Many non-essential services are running",
                    Body = $"{bloatCount} services we identify as safe-to-disable are currently running on startup. " +
                           "Disabling them can speed up boot time and reduce background memory usage. " +
                           "Visit the Services page to review and disable them.",
                    SupportingDataText = $"{bloatCount} non-essential auto-start services",
                    Category = FindingCategory.Performance,
                    GeneratedAt = now
                });
            }
        }
        catch { }

        // ── 9. High idle CPU ──────────────────────────────────────────────────
        try
        {
            // Read from monitor history — look at the most recent 5 samples
            var recent = (await _monitor.GetResourceHistoryAsync(5)).ToList();
            if (recent.Count >= 3)
            {
                var avgCpu = recent.Average(r => r.CpuUsagePercentage);
                if (avgCpu > 20)
                {
                    insights.Add(new SmartInsight
                    {
                        Id = "high-idle-cpu",
                        Title = "Elevated CPU usage detected at idle",
                        Body = $"Your CPU is averaging {avgCpu:F0}% usage in recent samples even when the system appears idle. " +
                               "This may indicate background processes, malware, or a runaway service consuming resources.",
                        SupportingDataText = $"Avg CPU: {avgCpu:F0}% (last {recent.Count} samples)",
                        Category = FindingCategory.Performance,
                        GeneratedAt = now
                    });
                }
            }
        }
        catch { }

        return insights;
    }
}
