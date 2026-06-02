using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class BootAnalysisService : IBootAnalysisService
{
    public async Task<IReadOnlyList<BootMetrics>> GetBootHistoryAsync(int count = 10)
    {
        return await Task.Run(() =>
        {
            var list = new List<BootMetrics>();
            try
            {
                var query = new EventLogQuery(
                    "Microsoft-Windows-Diagnostics-Performance/Operational",
                    PathType.LogName,
                    "*[System[(EventID=100)]]");

                using var reader = new EventLogReader(query);

                EventRecord? record;
                while ((record = reader.ReadEvent()) != null && list.Count < count)
                {
                    try
                    {
                        var bootMs = ExtractEventData(record, "BootTime");
                        var mainPathMs = ExtractEventData(record, "MainPathBootTime");
                        var postBootMs = ExtractEventData(record, "BootPostBootTime");
                        var kernelMs = ExtractEventData(record, "BootKernelInitTime");
                        var servicesMs = ExtractEventData(record, "BootServicesInitStartTime");

                        if (bootMs.HasValue)
                        {
                            list.Add(new BootMetrics
                            {
                                BootTime = record.TimeCreated ?? DateTime.MinValue,
                                BootDuration = TimeSpan.FromMilliseconds(bootMs.Value),
                                MainPathDuration = mainPathMs.HasValue
                                    ? TimeSpan.FromMilliseconds(mainPathMs.Value) : null,
                                BootPostBootDuration = postBootMs.HasValue
                                    ? TimeSpan.FromMilliseconds(postBootMs.Value) : null,
                                KernelBootTime = kernelMs.HasValue
                                    ? TimeSpan.FromMilliseconds(kernelMs.Value) : null,
                                ServicesBootTime = servicesMs.HasValue
                                    ? TimeSpan.FromMilliseconds(servicesMs.Value) : null,
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        EngineLog.Error("Error parsing boot event record", ex);
                    }
                    finally
                    {
                        record.Dispose();
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Not elevated — fall back to current session uptime as a single entry
                list.Add(BuildUptimeFallback());
            }
            catch (EventLogNotFoundException)
            {
                // Log channel absent on this SKU — use uptime fallback
                list.Add(BuildUptimeFallback());
            }
            catch (Exception ex)
            {
                EngineLog.Error("Failed to read boot performance events", ex);
                list.Add(BuildUptimeFallback());
            }

            return (IReadOnlyList<BootMetrics>)list;
        });
    }

    public Task<IReadOnlyList<StartupImpactInfo>> GetStartupImpactAsync()
    {
        return Task.Run(() =>
        {
            var list = new List<StartupImpactInfo>();

            ReadStartupApproved(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
                Microsoft.Win32.RegistryHive.CurrentUser, list);
            ReadStartupApproved(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
                Microsoft.Win32.RegistryHive.CurrentUser, list);
            ReadStartupApproved(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
                Microsoft.Win32.RegistryHive.LocalMachine, list);
            ReadStartupApproved(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
                Microsoft.Win32.RegistryHive.LocalMachine, list);

            return (IReadOnlyList<StartupImpactInfo>)list
                .OrderByDescending(x => x.DelayMs ?? TimeSpan.Zero)
                .ToList();
        });
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static long? ExtractEventData(EventRecord record, string name)
    {
        try
        {
            var xml = record.ToXml();
            var match = Regex.Match(xml, $@"<Data Name=""{name}"">(\d+)</Data>");
            if (match.Success && long.TryParse(match.Groups[1].Value, out var v))
                return v;
        }
        catch { }
        return null;
    }

    private static BootMetrics BuildUptimeFallback()
    {
        // Environment.TickCount64 gives ms since last boot
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return new BootMetrics
        {
            BootTime = DateTime.Now - uptime,
            BootDuration = TimeSpan.Zero,   // actual boot duration unknown without event log
        };
    }

    private static void ReadStartupApproved(
        string path,
        Microsoft.Win32.RegistryHive hive,
        List<StartupImpactInfo> list)
    {
        try
        {
            using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(
                hive, Microsoft.Win32.RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path);
            if (key == null) return;

            foreach (var name in key.GetValueNames())
            {
                var value = key.GetValue(name) as byte[];
                if (value == null) continue;

                // Byte 0: 0x02 = enabled, 0x03 = disabled (low nibble)
                var enabled = value.Length > 0 && (value[0] & 0x01) == 0;

                list.Add(new StartupImpactInfo
                {
                    Name = name,
                    Enabled = enabled,
                    Impact = enabled ? "Unknown" : "Disabled"
                });
            }
        }
        catch { }
    }
}
