using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Diagnostics;

/// <summary>Full-scan only: WMI hardware queries can be slow.</summary>
public sealed class HardwareSpecsPlugin : IDiagnosticPlugin
{
    private readonly IHardwareInfoService _hardware;

    public HardwareSpecsPlugin(IHardwareInfoService hardware) => _hardware = hardware;

    public string Name => "Hardware Specs";
    public DiagnosticScanLevel SupportedLevels => DiagnosticScanLevel.Full;

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(IProgress<string>? progress = null)
    {
        var findings = new List<DiagnosticFinding>();
        try
        {
            var hw = await _hardware.GetHardwareInfoAsync();

            if (hw.Memory.TotalBytes > 0 && hw.Memory.TotalBytes < 8L * 1_073_741_824L)
            {
                findings.Add(new DiagnosticFinding
                {
                    Id = "ram-low",
                    Title = "Low system memory",
                    Description = $"You have {hw.Memory.TotalBytes / 1_073_741_824L} GB of RAM.",
                    Recommendation = "8 GB is the recommended minimum for Windows 11. Consider an upgrade.",
                    Severity = FindingSeverity.Info,
                    Category = FindingCategory.Hardware
                });
            }

            if (!hw.Os.IsSecureBoot)
            {
                findings.Add(new DiagnosticFinding
                {
                    Id = "secureboot-off",
                    Title = "Secure Boot is disabled",
                    Description = "Secure Boot provides additional protection against boot-time malware.",
                    Recommendation = "Enable Secure Boot in BIOS/UEFI settings.",
                    Severity = FindingSeverity.Info,
                    Category = FindingCategory.Security
                });
            }
        }
        catch { }

        return findings;
    }
}
