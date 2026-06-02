using System.Diagnostics;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IBottleneckDetectorService
{
    Task<BottleneckReport> DetectAsync(IProgress<string>? progress = null);
}

public class BottleneckDetectorService : IBottleneckDetectorService
{
    public async Task<BottleneckReport> DetectAsync(IProgress<string>? progress = null)
    {
        var report = new BottleneckReport();

        progress?.Report("Sampling baseline (this takes ~30 seconds)...");
        var sample1 = SampleProcesses();

        await Task.Delay(30_000);

        progress?.Report("Analysing second sample...");
        var sample2 = SampleProcesses();

        // Memory leak: processes that grew by >50 MB
        foreach (var p2 in sample2)
        {
            var p1 = sample1.FirstOrDefault(x => x.Pid == p2.Pid);
            if (p1 == null) continue;

            var growthMb = (p2.WorkingSetBytes - p1.WorkingSetBytes) / (1024.0 * 1024.0);
            if (growthMb > 50)
            {
                report.TopOffenders.Add(new ProcessBottleneck
                {
                    Pid = p2.Pid,
                    ProcessName = p2.Name,
                    BottleneckType = "Memory Leak",
                    Value = growthMb,
                    ValueUnit = "MB grown",
                    Severity = growthMb > 200 ? "High" : "Medium"
                });
            }
        }

        // High CPU at second sample
        var highCpu = sample2
            .Where(p => p.CpuPercent > 25)
            .OrderByDescending(p => p.CpuPercent)
            .Take(5);

        foreach (var p in highCpu)
        {
            report.TopOffenders.Add(new ProcessBottleneck
            {
                Pid = p.Pid,
                ProcessName = p.Name,
                BottleneckType = "CPU",
                Value = p.CpuPercent,
                ValueUnit = "%",
                Severity = p.CpuPercent > 70 ? "High" : "Medium"
            });
        }

        // High memory absolute usage (top 5 > 500 MB)
        var highMem = sample2
            .Where(p => p.WorkingSetBytes > 500L * 1024 * 1024)
            .OrderByDescending(p => p.WorkingSetBytes)
            .Take(5);

        foreach (var p in highMem)
        {
            if (report.TopOffenders.Any(o => o.Pid == p.Pid && o.BottleneckType == "Memory Leak"))
                continue; // already captured

            var mb = p.WorkingSetBytes / (1024.0 * 1024.0);
            report.TopOffenders.Add(new ProcessBottleneck
            {
                Pid = p.Pid,
                ProcessName = p.Name,
                BottleneckType = "Memory",
                Value = mb,
                ValueUnit = "MB",
                Severity = mb > 2000 ? "High" : "Medium"
            });
        }

        report.Summary = report.TopOffenders.Count == 0
            ? "No significant bottlenecks detected."
            : $"Found {report.TopOffenders.Count} potential issue(s).";

        return report;
    }

    private sealed class Sample
    {
        public int Pid;
        public string Name = "";
        public long WorkingSetBytes;
        public double CpuPercent;
    }

    private List<Sample> SampleProcesses()
    {
        // Compute CPU % using a short 100 ms window for each process would be too slow
        // with many processes, so we use a two-pass approach via PerformanceCounter for
        // just the top few — here we record raw processor time for a lightweight first pass.
        var counters = new List<(Sample sample, PerformanceCounter counter)>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var s = new Sample
                {
                    Pid = p.Id,
                    Name = p.ProcessName,
                    WorkingSetBytes = p.WorkingSet64,
                };
                try
                {
                    var pc = new PerformanceCounter("Process", "% Processor Time", p.ProcessName, true);
                    pc.NextValue(); // prime
                    counters.Add((s, pc));
                }
                catch
                {
                    counters.Add((s, null!));
                }
            }
            catch { }
        }

        // Brief delay so counters can accumulate
        System.Threading.Thread.Sleep(200);

        foreach (var (s, pc) in counters)
        {
            if (pc == null) continue;
            try
            {
                s.CpuPercent = pc.NextValue() / Environment.ProcessorCount;
                pc.Dispose();
            }
            catch { }
        }

        return counters.Select(c => c.sample).ToList();
    }
}
