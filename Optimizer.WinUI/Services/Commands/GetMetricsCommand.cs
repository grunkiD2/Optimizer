using System.Globalization;
using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class GetMetricsCommand(ISystemMonitorService monitor) : IAppCommand
{
    public string Id => "get_metrics";
    public string Description => "Get current CPU, memory, and GPU usage for this PC.";
    public JsonElement ParametersSchema => SchemaJson.Empty;
    public bool IsReadOnly => true;
    public bool RequiresConfirmation => false;

    public Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var s = monitor.CollectSnapshot();
        long usedMb = (s.TotalPhysicalMemory - s.AvailablePhysicalMemory) / (1024 * 1024);
        long totalMb = s.TotalPhysicalMemory / (1024 * 1024);
        var summary = string.Create(CultureInfo.InvariantCulture,
            $"CPU {s.CpuUsagePercentage:F0}%, GPU {s.GpuUsagePercentage:F0}%, memory {usedMb}/{totalMb} MB used.");
        return Task.FromResult(CommandResult.Ok(summary, new { cpu = s.CpuUsagePercentage, gpu = s.GpuUsagePercentage, usedMb, totalMb }));
    }
}
