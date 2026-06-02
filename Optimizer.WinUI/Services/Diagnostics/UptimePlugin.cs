using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Diagnostics;

public sealed class UptimePlugin : IDiagnosticPlugin
{
    public string Name => "System Uptime";
    public DiagnosticScanLevel SupportedLevels => DiagnosticScanLevel.Both;

    public Task<IReadOnlyList<DiagnosticFinding>> RunAsync(IProgress<string>? progress = null)
    {
        var findings = new List<DiagnosticFinding>();
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

        return Task.FromResult<IReadOnlyList<DiagnosticFinding>>(findings);
    }
}
