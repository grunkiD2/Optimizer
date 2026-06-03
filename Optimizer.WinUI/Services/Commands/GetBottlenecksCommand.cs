using System.Text;
using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class GetBottlenecksCommand(IBottleneckDetectorService detector) : IAppCommand
{
    public string Id => "get_bottlenecks";
    public string Description => "Find the processes or subsystems currently bottlenecking this PC (what's eating CPU/RAM/disk).";
    public JsonElement ParametersSchema => SchemaJson.Empty;
    public bool IsReadOnly => true;
    public bool RequiresConfirmation => false;

    public async Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var report = await detector.DetectAsync();
        if (report.TopOffenders.Count == 0) return CommandResult.Ok("No significant bottlenecks detected.");
        var sb = new StringBuilder(report.Summary);
        foreach (var o in report.TopOffenders)
            sb.Append($"\n- {o.ProcessName} (pid {o.Pid}): {o.BottleneckType} {o.DisplayValue} [{o.Severity}]");
        return CommandResult.Ok(sb.ToString());
    }
}
