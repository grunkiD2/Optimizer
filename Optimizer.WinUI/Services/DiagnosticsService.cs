using System.Diagnostics;
using System.Net.NetworkInformation;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class DiagnosticsService : IDiagnosticsService
{
    private readonly SystemMonitorService _monitor;
    private readonly IDiskHealthService _diskHealth;
    private readonly IPrivacyService _privacy;
    private readonly IHardwareInfoService _hardware;
    private readonly IBootAnalysisService _boot;
    private readonly IServiceManagerService _services;

    public DiagnosticsService(
        SystemMonitorService monitor,
        IDiskHealthService diskHealth,
        IPrivacyService privacy,
        IHardwareInfoService hardware,
        IBootAnalysisService boot,
        IServiceManagerService services)
    {
        _monitor = monitor;
        _diskHealth = diskHealth;
        _privacy = privacy;
        _hardware = hardware;
        _boot = boot;
        _services = services;
    }

    public async Task<IReadOnlyList<DiagnosticFinding>> RunQuickScanAsync()
    {
        var findings = new List<DiagnosticFinding>();

        // Check 1: Memory usage
        try
        {
            var snapshot = _monitor.CollectSnapshot();
            if (snapshot.TotalPhysicalMemory > 0)
            {
                var memUsedPct = 100.0 * (snapshot.TotalPhysicalMemory - snapshot.AvailablePhysicalMemory) / snapshot.TotalPhysicalMemory;
                if (memUsedPct > 90)
                {
                    findings.Add(new DiagnosticFinding
                    {
                        Id = "mem-high",
                        Title = "High memory usage",
                        Description = $"System memory is {memUsedPct:F0}% used.",
                        Recommendation = "Close unused applications or upgrade RAM.",
                        Severity = FindingSeverity.Warning,
                        Category = FindingCategory.Performance,
                        HasQuickFix = false
                    });
                }
            }
        }
        catch { }

        // Check 2: Disk health (SMART)
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

        // Check 3: Disk space
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var usedPct = 100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
                if (usedPct > 90)
                {
                    findings.Add(new DiagnosticFinding
                    {
                        Id = $"disk-full-{drive.Name}",
                        Title = $"Drive {drive.Name} is nearly full",
                        Description = $"Only {drive.AvailableFreeSpace / 1_073_741_824L} GB free of {drive.TotalSize / 1_073_741_824L} GB.",
                        Recommendation = "Run cleanup or move files to free up space.",
                        Severity = usedPct > 95 ? FindingSeverity.Critical : FindingSeverity.Warning,
                        Category = FindingCategory.Storage
                    });
                }
            }
        }
        catch { }

        // Check 4: Privacy score
        try
        {
            var privacy = await _privacy.GetAllAsync();
            if (privacy.Count > 0)
            {
                var enabledCount = privacy.Count(p => p.IsPrivacyFriendly);
                var score = (int)(100.0 * enabledCount / privacy.Count);
                if (score < 50)
                {
                    findings.Add(new DiagnosticFinding
                    {
                        Id = "privacy-low",
                        Title = "Privacy settings are weak",
                        Description = $"Your privacy score is {score}/100. Many telemetry/tracking options are enabled.",
                        Recommendation = "Visit System → Privacy Dashboard to enable more privacy-friendly settings.",
                        Severity = FindingSeverity.Warning,
                        Category = FindingCategory.Privacy
                    });
                }
            }
        }
        catch { }

        // Check 5: System uptime
        try
        {
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            if (uptime.TotalDays > 14)
            {
                findings.Add(new DiagnosticFinding
                {
                    Id = "uptime-long",
                    Title = "System hasn't been restarted in a while",
                    Description = $"Current uptime is {uptime.TotalDays:F0} days.",
                    Recommendation = "Restart Windows to apply pending updates and clear memory.",
                    Severity = FindingSeverity.Info,
                    Category = FindingCategory.Stability
                });
            }
        }
        catch { }

        // Check 6: Recent BSOD/crash events
        try
        {
            using var log = new EventLog("System");
            var bsodCount = log.Entries.Cast<EventLogEntry>()
                .Where(e => e.TimeWritten > DateTime.Now.AddDays(-7)
                         && e.Source.Contains("BugCheck"))
                .Take(20).Count();

            if (bsodCount > 0)
            {
                findings.Add(new DiagnosticFinding
                {
                    Id = "bsod-recent",
                    Title = "Recent system crashes detected",
                    Description = $"Found {bsodCount} BSOD event(s) in the last 7 days.",
                    Recommendation = "Update drivers, check memory with mdsched.exe, and run sfc /scannow.",
                    Severity = FindingSeverity.Critical,
                    Category = FindingCategory.Stability
                });
            }
        }
        catch { }

        // Check 7: Hibernation file
        try
        {
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var hiberFile = Path.Combine(winDir, "hiberfil.sys");
            if (File.Exists(hiberFile))
            {
                var size = new FileInfo(hiberFile).Length / 1_073_741_824L;
                if (size > 4)
                {
                    findings.Add(new DiagnosticFinding
                    {
                        Id = "hiber-large",
                        Title = "Hibernation file uses significant disk space",
                        Description = $"hiberfil.sys is {size} GB.",
                        Recommendation = "Disable hibernation to recover space if you don't use it.",
                        Severity = FindingSeverity.Info,
                        Category = FindingCategory.Storage
                    });
                }
            }
        }
        catch { }

        return findings;
    }

    public async Task<IReadOnlyList<DiagnosticFinding>> RunFullScanAsync(IProgress<string>? progress = null)
    {
        progress?.Report("Running quick scan...");
        var findings = (await RunQuickScanAsync()).ToList();

        progress?.Report("Analyzing boot performance...");
        try
        {
            var boots = await _boot.GetBootHistoryAsync(5);
            if (boots.Count > 0)
            {
                var slow = boots.Where(b => b.BootDuration.TotalSeconds > 60).ToList();
                if (slow.Count > 0)
                {
                    findings.Add(new DiagnosticFinding
                    {
                        Id = "boot-slow",
                        Title = "Slow boot time",
                        Description = $"Recent boots averaged {boots.Average(b => b.BootDuration.TotalSeconds):F1} seconds.",
                        Recommendation = "Review Startup page for high-impact items.",
                        Severity = FindingSeverity.Warning,
                        Category = FindingCategory.Performance
                    });
                }
            }
        }
        catch { }

        progress?.Report("Reviewing services...");
        try
        {
            var services = await _services.GetServicesAsync();
            var unnecessaryAutoCount = services.Count(s =>
                string.Equals(s.Recommendation, "Safe", StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.StartupType, "Automatic", StringComparison.OrdinalIgnoreCase));

            if (unnecessaryAutoCount > 5)
            {
                findings.Add(new DiagnosticFinding
                {
                    Id = "services-auto",
                    Title = $"{unnecessaryAutoCount} non-essential services run on startup",
                    Description = "Several services flagged as safe to disable still run automatically.",
                    Recommendation = "Visit Services page to review and disable as appropriate.",
                    Severity = FindingSeverity.Info,
                    Category = FindingCategory.Performance
                });
            }
        }
        catch { }

        progress?.Report("Checking hardware...");
        try
        {
            var hw = await _hardware.GetHardwareInfoAsync();
            if (hw.Memory.TotalBytes > 0 && hw.Memory.TotalBytes < 8L * 1_073_741_824L)
            {
                findings.Add(new DiagnosticFinding
                {
                    Id = "ram-low",
                    Title = "Low system memory",
                    Description = $"You have {hw.Memory.TotalBytes / 1_073_741_824L} GB of RAM.",
                    Recommendation = "8 GB is the recommended minimum for Windows 11. Consider an upgrade.",
                    Severity = FindingSeverity.Info,
                    Category = FindingCategory.Hardware
                });
            }

            if (!hw.Os.IsSecureBoot)
            {
                findings.Add(new DiagnosticFinding
                {
                    Id = "secureboot-off",
                    Title = "Secure Boot is disabled",
                    Description = "Secure Boot provides additional protection against boot-time malware.",
                    Recommendation = "Enable Secure Boot in BIOS/UEFI settings.",
                    Severity = FindingSeverity.Info,
                    Category = FindingCategory.Security
                });
            }
        }
        catch { }

        progress?.Report("Scan complete.");
        return findings;
    }

    // ── Network deep diagnostics ────────────────────────────────────────────

    public async Task<IReadOnlyList<NetworkDiagnostic>> RunNetworkDeepAsync(IProgress<string>? progress = null)
    {
        var results = new List<NetworkDiagnostic>();

        // 1. Traceroute to 1.1.1.1 (up to 30 hops)
        progress?.Report("Tracing route to 1.1.1.1...");
        try
        {
            using var ping = new Ping();
            var routeLines = new List<string>();
            for (int ttl = 1; ttl <= 30; ttl++)
            {
                var options = new PingOptions(ttl, true);
                try
                {
                    var reply = await ping.SendPingAsync("1.1.1.1", 2000, new byte[32], options);
                    var addr = reply.Address?.ToString() ?? "*";
                    routeLines.Add($"  {ttl,2}: {addr} ({reply.RoundtripTime} ms) [{reply.Status}]");
                    if (reply.Status == IPStatus.Success) break;
                }
                catch (Exception ex)
                {
                    routeLines.Add($"  {ttl,2}: [{ex.Message}]");
                    break;
                }
            }
            results.Add(new NetworkDiagnostic
            {
                Type = "Traceroute",
                Target = "1.1.1.1",
                Result = routeLines.Count > 0 ? string.Join("\n", routeLines) : "No hops recorded",
                Success = routeLines.Count > 0
            });
        }
        catch (Exception ex)
        {
            results.Add(new NetworkDiagnostic
            {
                Type = "Traceroute", Target = "1.1.1.1",
                Result = $"Failed: {ex.Message}", Success = false
            });
        }

        // 2. MTU discovery
        progress?.Report("Discovering MTU...");
        try
        {
            int optimalMtu = await FindMtuAsync();
            results.Add(new NetworkDiagnostic
            {
                Type = "MTU",
                Target = "1.1.1.1",
                Result = optimalMtu > 0 ? $"Maximum MTU: {optimalMtu} bytes" : "Could not determine MTU",
                Success = optimalMtu > 0
            });
        }
        catch (Exception ex)
        {
            results.Add(new NetworkDiagnostic
            {
                Type = "MTU", Target = "1.1.1.1",
                Result = $"Failed: {ex.Message}", Success = false
            });
        }

        // 3. Packet loss (100 pings)
        progress?.Report("Testing packet loss (100 pings)...");
        try
        {
            using var ping2 = new Ping();
            int successCount = 0;
            long totalTime = 0;
            for (int i = 0; i < 100; i++)
            {
                try
                {
                    var reply = await ping2.SendPingAsync("1.1.1.1", 1000);
                    if (reply.Status == IPStatus.Success)
                    {
                        successCount++;
                        totalTime += reply.RoundtripTime;
                    }
                }
                catch { }
            }
            var lossPct = 100 - successCount;
            var avgMs = successCount > 0 ? totalTime / successCount : 0;
            results.Add(new NetworkDiagnostic
            {
                Type = "Packet Loss",
                Target = "1.1.1.1",
                Result = $"{lossPct}% loss ({successCount}/100 successful), avg {avgMs} ms",
                Success = lossPct < 5
            });
        }
        catch (Exception ex)
        {
            results.Add(new NetworkDiagnostic
            {
                Type = "Packet Loss", Target = "1.1.1.1",
                Result = $"Failed: {ex.Message}", Success = false
            });
        }

        return results;
    }

    private static async Task<int> FindMtuAsync()
    {
        using var ping = new Ping();
        int low = 500, high = 1472, best = 0; // 1472 = 1500 - 28 (IP+ICMP headers)
        while (low <= high)
        {
            int mid = (low + high) / 2;
            var buffer = new byte[mid];
            var options = new PingOptions(64, true); // DontFragment = true
            try
            {
                var reply = await ping.SendPingAsync("1.1.1.1", 1500, buffer, options);
                if (reply.Status == IPStatus.Success)
                {
                    best = mid + 28;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }
            catch
            {
                high = mid - 1;
            }
        }
        return best;
    }
}
