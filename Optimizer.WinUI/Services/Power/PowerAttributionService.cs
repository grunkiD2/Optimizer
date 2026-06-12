using System.Diagnostics;

namespace Optimizer.WinUI.Services.Power;

public record ProcessPowerReading(string Name, int InstanceCount, double CpuShare, double EstimatedWatts, double CpuSeconds);

public record PowerAttributionSnapshot(
    DateTimeOffset Timestamp,
    double WindowSeconds,
    double? PackageWatts,
    double AttributedShare,
    IReadOnlyList<ProcessPowerReading> Processes);

public interface IPowerAttributionService
{
    /// <summary>
    /// Sample per-process CPU time since the previous call and attribute the measured CPU
    /// package watts proportionally. First call only primes the counters and returns null.
    /// </summary>
    PowerAttributionSnapshot? Sample();
}

/// <summary>
/// Per-process power attribution — the PPI fallback model from docs/POWER-INSIGHTS.md §4
/// promoted to primary: on a desktop without a battery the Energy-Estimation-Engine ETW
/// provider produces no per-process energy, and ETW sessions would require elevation.
/// Instead: CPU-time share per process × the MEASURED package watts (ISensorService — on
/// the federated machine that is live LHM data, not a TDP guess). Clearly labeled
/// "estimated"; GPU power is not attributed. STRICTLY read-only: enumerates processes and
/// reads clocks, never touches priorities, affinities, registry or process lifetime.
/// </summary>
public class PowerAttributionService : IPowerAttributionService
{
    private readonly ISensorService _sensors;
    private readonly Dictionary<(int Pid, long StartTicks), (string Name, TimeSpan Cpu)> _last = new();
    private DateTimeOffset _lastSampleAt;

    public PowerAttributionService(ISensorService sensors) => _sensors = sensors;

    public PowerAttributionSnapshot? Sample()
    {
        var now = DateTimeOffset.Now;
        var current = new Dictionary<(int, long), (string, TimeSpan)>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                // Pid + StartTime = identity within a session (PID reuse). Inaccessible
                // processes (protected/system) are skipped — their share stays unattributed.
                current[(p.Id, p.StartTime.Ticks)] = (p.ProcessName, p.TotalProcessorTime);
            }
            catch { /* access denied / exited mid-enumeration */ }
            finally { p.Dispose(); }
        }

        var window = (now - _lastSampleAt).TotalSeconds;
        var primed = _lastSampleAt != default && window > 0.5 && window < 600;
        var previous = primed ? new Dictionary<(int, long), (string, TimeSpan)>(_last) : null;

        _last.Clear();
        foreach (var kv in current) _last[kv.Key] = kv.Value;
        _lastSampleAt = now;
        if (previous == null) return null;

        double? packageWatts = null;
        try { packageWatts = _sensors.IsAvailable ? _sensors.GetSnapshot().CpuPowerWatts : null; } catch { }

        return Attribute(now, window, packageWatts, previous, current, Environment.ProcessorCount);
    }

    /// <summary>Pure attribution math — internal static for unit tests.</summary>
    internal static PowerAttributionSnapshot Attribute(
        DateTimeOffset now, double windowSeconds, double? packageWatts,
        IReadOnlyDictionary<(int Pid, long StartTicks), (string Name, TimeSpan Cpu)> previous,
        IReadOnlyDictionary<(int Pid, long StartTicks), (string Name, TimeSpan Cpu)> current,
        int processorCount)
    {
        var capacitySeconds = windowSeconds * processorCount;
        var byName = new Dictionary<string, (int Count, double CpuSeconds)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, (name, cpu)) in current)
        {
            // Processes first seen THIS round have no previous reading — they are counted
            // from the next round (attributing their full lifetime CPU here would spike).
            if (!previous.TryGetValue(key, out var prev)) continue;
            var delta = (cpu - prev.Cpu).TotalSeconds;
            if (delta <= 0) continue;
            var entry = byName.TryGetValue(name, out var e) ? e : (0, 0.0);
            byName[name] = (entry.Item1 + 1, entry.Item2 + delta);
        }

        var totalCpuSeconds = byName.Values.Sum(v => v.CpuSeconds);
        var attributedShare = capacitySeconds > 0 ? Math.Min(1.0, totalCpuSeconds / capacitySeconds) : 0;

        var readings = byName
            .Select(kv =>
            {
                var share = capacitySeconds > 0 ? Math.Min(1.0, kv.Value.CpuSeconds / capacitySeconds) : 0;
                return new ProcessPowerReading(
                    Name: kv.Key,
                    InstanceCount: kv.Value.Count,
                    CpuShare: share,
                    // Proportional split of the measured package power over CPU-busy time.
                    // Σ(process watts) = packageWatts × attributedShare ≤ packageWatts — the
                    // remainder is idle/uncore/inaccessible, reported via AttributedShare.
                    EstimatedWatts: packageWatts is { } w && totalCpuSeconds > 0
                        ? w * attributedShare * (kv.Value.CpuSeconds / totalCpuSeconds)
                        : 0,
                    CpuSeconds: kv.Value.CpuSeconds);
            })
            .OrderByDescending(r => r.CpuSeconds)
            .ToList();

        return new PowerAttributionSnapshot(now, windowSeconds, packageWatts, attributedShare, readings);
    }
}
