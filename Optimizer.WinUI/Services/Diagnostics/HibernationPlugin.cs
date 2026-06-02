using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Diagnostics;

public sealed class HibernationPlugin : IDiagnosticPlugin
{
    public string Name => "Hibernation File Size";
    public DiagnosticScanLevel SupportedLevels => DiagnosticScanLevel.Both;

    public Task<IReadOnlyList<DiagnosticFinding>> RunAsync(IProgress<string>? progress = null)
    {
        var findings = new List<DiagnosticFinding>();
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

        return Task.FromResult<IReadOnlyList<DiagnosticFinding>>(findings);
    }
}
