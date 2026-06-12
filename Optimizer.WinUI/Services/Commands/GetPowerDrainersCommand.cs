using System.Text.Json;
using Optimizer.WinUI.Services.Power;

namespace Optimizer.WinUI.Services.Commands;

/// <summary>Read-only assistant tool: top per-process power drainers + drift state
/// (POWER-INSIGHTS use case 7 — concrete, citable triage input).</summary>
public sealed class GetPowerDrainersCommand(IPowerInsightsService ppi) : IAppCommand
{
    public string Id => "get_power_drainers";
    public string Description => "Get the top per-process power drainers right now (estimated: CPU-time share × measured CPU package watts) with each process's drift state vs its learned per-context baseline. Use when the user asks why the system is slow, hot, loud or power-hungry.";
    public JsonElement ParametersSchema => SchemaJson.Empty;
    public bool IsReadOnly => true;
    public bool RequiresConfirmation => false;

    public Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        if (!ppi.Enabled) return Task.FromResult(CommandResult.Fail("Power Insights is disabled (Settings → PpiEnabled)."));
        var snap = ppi.LatestSnapshot;
        if (snap == null) return Task.FromResult(CommandResult.Fail("No attribution sample yet — the sampler warms up ~90 s after launch."));
        var top = ppi.GetTopDrainers(8);
        var lines = top.Select(p =>
            $"{p.Name}{(p.InstanceCount > 1 ? $" (×{p.InstanceCount})" : "")}: {p.EstimatedWatts:F1} W" +
            (p.BaselineW is { } b ? $" (baseline {b:F1} W, z={p.ZScore:F1}, {p.Drift})" : $" ({p.Drift})"));
        var summary = $"Context {ppi.LatestContext} · package {snap.PackageWatts:F0} W · {snap.AttributedShare:P0} attributed to processes · top drainers: {string.Join(" · ", lines)}";
        return Task.FromResult(CommandResult.Ok(summary, new { snap.PackageWatts, ppi.LatestContext, top }));
    }
}
