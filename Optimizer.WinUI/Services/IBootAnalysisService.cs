using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IBootAnalysisService
{
    /// <summary>Returns boot metrics from the Diagnostics-Performance event log (newest first).</summary>
    Task<IReadOnlyList<BootMetrics>> GetBootHistoryAsync(int count = 10);

    /// <summary>Returns per-program startup approval data from the registry.</summary>
    Task<IReadOnlyList<StartupImpactInfo>> GetStartupImpactAsync();
}
