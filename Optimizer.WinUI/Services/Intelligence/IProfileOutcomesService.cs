using System.Threading.Tasks;

namespace Optimizer.WinUI.Services.Intelligence;

/// <summary>
/// One profile-interval rollup. <paramref name="GpuFps1Low"/> stays null until a PresentMon
/// join is layered on (a later concern) — the column is nullable, never fabricated.
/// </summary>
public sealed record OutcomeRecord(
    string ProfileName,
    string RecordedAtUtc,
    int DurationMinutes,
    int SampleCount,
    double? CoolantP95,
    double? GpuFps1Low);

public interface IProfileOutcomesService
{
    /// <summary>Window FancontrolTelemetry by [startTs,endTs) for a profile → coolant-p95 + sample count.
    /// Does NOT persist; returns the computed record (caller decides whether to store it).</summary>
    Task<OutcomeRecord?> RollupIntervalAsync(string profileName, string startTs, string endTs);

    /// <summary>Persist a rollup row into ProfileOutcomes.</summary>
    Task SaveAsync(OutcomeRecord rec);

    /// <summary>The two most recent stored outcomes for a profile (latest, previous) for the "sidst vs forrige" delta.</summary>
    Task<(OutcomeRecord? Latest, OutcomeRecord? Previous)> LastVsPreviousAsync(string profileName);
}
