using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IComplianceService
{
    List<string> AvailableFrameworks { get; }
    Task<IReadOnlyList<ComplianceCheck>> RunFrameworkAsync(string framework);
    Task<string> ExportAuditReportAsync(IReadOnlyList<ComplianceCheck> checks);
}
