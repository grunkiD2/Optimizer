using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface ISystemDataBus
{
    event Action<SystemResource>? MetricsUpdated;
    event Action<HardwareSnapshot>? SensorsUpdated;
    event Action<double>? LatencyUpdated;

    SystemResource? LatestMetrics { get; }
    HardwareSnapshot? LatestSensors { get; }

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();

    /// <summary>Pause/resume sensor polling (e.g., when no page needs them).</summary>
    void SetSensorsActive(bool active);
    void SetLatencyActive(bool active);
}
