using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Diagnostics;

/// <summary>
/// A single diagnostic category that can run independently and produce a list of findings.
/// Implement this interface to extend the diagnostics engine without touching <see cref="DiagnosticsService"/>.
/// </summary>
public interface IDiagnosticPlugin
{
    /// <summary>Human-readable name shown in scan progress messages.</summary>
    string Name { get; }

    /// <summary>Which scan modes this plugin participates in.</summary>
    DiagnosticScanLevel SupportedLevels { get; }

    /// <summary>Runs the check and returns any findings. Should never throw — swallow exceptions internally.</summary>
    Task<IReadOnlyList<DiagnosticFinding>> RunAsync(IProgress<string>? progress = null);
}

[Flags]
public enum DiagnosticScanLevel
{
    Quick = 1,
    Full = 2,
    Both = Quick | Full
}
