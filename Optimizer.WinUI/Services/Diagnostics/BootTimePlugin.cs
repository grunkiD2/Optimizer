using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Diagnostics;

/// <summary>Full-scan only: boot history analysis takes more time than a quick scan budget allows.</summary>
public sealed class BootTimePlugin : IDiagnosticPlugin
{
    private readonly IBootAnalysisService _boot;

    public BootTimePlugin(IBootAnalysisService boot) => _boot = boot;

    public string Name => "Boot Time";
    public DiagnosticScanLevel SupportedLevels => DiagnosticScanLevel.Full;

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(IProgress<string>? progress = null)
    {
        var findings = new List<DiagnosticFinding>();
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

        return findings;
    }
}
