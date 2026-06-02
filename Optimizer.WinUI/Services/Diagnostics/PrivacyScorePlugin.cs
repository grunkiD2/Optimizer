using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Diagnostics;

public sealed class PrivacyScorePlugin : IDiagnosticPlugin
{
    private readonly IPrivacyService _privacy;

    public PrivacyScorePlugin(IPrivacyService privacy) => _privacy = privacy;

    public string Name => "Privacy Score";
    public DiagnosticScanLevel SupportedLevels => DiagnosticScanLevel.Both;

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(IProgress<string>? progress = null)
    {
        var findings = new List<DiagnosticFinding>();
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

        return findings;
    }
}
