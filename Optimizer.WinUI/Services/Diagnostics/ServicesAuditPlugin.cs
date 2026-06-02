using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Diagnostics;

/// <summary>Full-scan only: service enumeration is a slower operation.</summary>
public sealed class ServicesAuditPlugin : IDiagnosticPlugin
{
    private readonly IServiceManagerService _services;

    public ServicesAuditPlugin(IServiceManagerService services) => _services = services;

    public string Name => "Services Audit";
    public DiagnosticScanLevel SupportedLevels => DiagnosticScanLevel.Full;

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(IProgress<string>? progress = null)
    {
        var findings = new List<DiagnosticFinding>();
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

        return findings;
    }
}
