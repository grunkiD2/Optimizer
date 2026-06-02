using System.Diagnostics;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Diagnostics;

public sealed class BsodPlugin : IDiagnosticPlugin
{
    public string Name => "Recent Crashes (BSOD)";
    public DiagnosticScanLevel SupportedLevels => DiagnosticScanLevel.Both;

    public Task<IReadOnlyList<DiagnosticFinding>> RunAsync(IProgress<string>? progress = null)
    {
        var findings = new List<DiagnosticFinding>();
        try
        {
            using var log = new EventLog("System");
            var bsodCount = log.Entries.Cast<EventLogEntry>()
                .Where(e => e.TimeWritten > DateTime.Now.AddDays(-7)
                         && e.Source.Contains("BugCheck", StringComparison.OrdinalIgnoreCase))
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

        return Task.FromResult<IReadOnlyList<DiagnosticFinding>>(findings);
    }
}
