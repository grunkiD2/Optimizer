using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Diagnostics;

public sealed class MemoryUsagePlugin : IDiagnosticPlugin
{
    private readonly ISystemMonitorService _monitor;

    public MemoryUsagePlugin(ISystemMonitorService monitor) => _monitor = monitor;

    public string Name => "Memory Usage";
    public DiagnosticScanLevel SupportedLevels => DiagnosticScanLevel.Both;

    public Task<IReadOnlyList<DiagnosticFinding>> RunAsync(IProgress<string>? progress = null)
    {
        var findings = new List<DiagnosticFinding>();
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

        return Task.FromResult<IReadOnlyList<DiagnosticFinding>>(findings);
    }
}
