using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IDriverDiagnosticsService
{
    Task<IReadOnlyList<DriverIssue>> ScanAsync();
}
