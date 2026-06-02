using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IDiagnosticsService
{
    Task<IReadOnlyList<DiagnosticFinding>> RunQuickScanAsync();
    Task<IReadOnlyList<DiagnosticFinding>> RunFullScanAsync(IProgress<string>? progress = null);
}
