using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Diagnostics;

public sealed class DiskSpacePlugin : IDiagnosticPlugin
{
    public string Name => "Disk Space";
    public DiagnosticScanLevel SupportedLevels => DiagnosticScanLevel.Both;

    public Task<IReadOnlyList<DiagnosticFinding>> RunAsync(IProgress<string>? progress = null)
    {
        var findings = new List<DiagnosticFinding>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var usedPct = 100.0 * (drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize;
                if (usedPct > 90)
                {
                    findings.Add(new DiagnosticFinding
                    {
                        Id = $"disk-full-{drive.Name}",
                        Title = $"Drive {drive.Name} is nearly full",
                        Description = $"Only {drive.AvailableFreeSpace / 1_073_741_824L} GB free of {drive.TotalSize / 1_073_741_824L} GB.",
                        Recommendation = "Run cleanup or move files to free up space.",
                        Severity = usedPct > 95 ? FindingSeverity.Critical : FindingSeverity.Warning,
                        Category = FindingCategory.Storage
                    });
                }
            }
        }
        catch { }

        return Task.FromResult<IReadOnlyList<DiagnosticFinding>>(findings);
    }
}
