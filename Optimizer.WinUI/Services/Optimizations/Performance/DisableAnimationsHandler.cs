using Microsoft.Win32;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Optimizations.Performance;

public sealed class DisableAnimationsHandler : OptimizationHandlerBase
{
    public override string Id => OptimizationIds.DisableAnimations;

    public override OptimizationInfo Info { get; } = new()
    {
        Id = "DisableAnimations",
        Title = "Disable window & taskbar animations",
        Summary = "Turns off window minimize/maximize and taskbar animations for snappier UI.",
        Changes =
        {
            @"Sets HKCU\Control Panel\Desktop\WindowMetrics\MinAnimate = 0",
            @"Sets HKCU\…\Explorer\Advanced\TaskbarAnimations = 0 (DWORD)"
        },
        Pros = { "UI feels faster and more responsive", "Helps on older GPUs / remote desktop sessions" },
        Cons = { "Transitions are abrupt rather than smooth (cosmetic)" },
        Recommendation = "Great low-risk win for perceived speed. Sign out/in for a fully consistent effect.",
        RequiresAdmin = false,
        Reversible = true,
        RequiresRestart = true
    };

    public override bool? IsApplied()
        => ReadHkcu(@"Control Panel\Desktop\WindowMetrics", "MinAnimate") == "0";

    public override Task<OptimizationResult> ApplyAsync(IUndoService undoService, IElevationService elevationService)
    {
        SetRegistryValue(undoService,
            "HKCU", @"Control Panel\Desktop\WindowMetrics",
            "MinAnimate", "0", RegistryValueKind.String, "Disable window animations");
        SetRegistryValue(undoService,
            "HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            "TaskbarAnimations", 0, RegistryValueKind.DWord, "Disable taskbar animations");

        return Task.FromResult(new OptimizationResult
        {
            Success = true,
            Message = "Window and taskbar animations disabled."
        });
    }
}
