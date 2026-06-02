using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface ISystemMonitorService
{
    TimeSpan SampleInterval { get; set; }
    int CurrentHistorySize { get; }
    Task StartMonitoringAsync(int sampleDurationSeconds = 3600);
    void StopMonitoring();
    Task<IEnumerable<SystemResource>> GetResourceHistoryAsync(int sampleCount);
    SystemResource CollectSnapshot();
    IReadOnlyList<double> GetPerCoreUsage();
    void SaveHistory();
}
