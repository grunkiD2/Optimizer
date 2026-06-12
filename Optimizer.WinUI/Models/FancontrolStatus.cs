namespace Optimizer.WinUI.Models;

/// <summary>
/// Read-only view of the Fancontrol machine-control system's state contracts
/// (L:\Users\Fancontrol\state\*.json — see docs/MACHINE-OWNERSHIP.md). Optimizer
/// only ever OBSERVES these; all control goes through Fancontrol's own ctl.ps1.
/// Sections are null when their state file is missing/unreadable.
/// </summary>
public class FancontrolStatus
{
    public FancontrolBrainStatus? Brain { get; set; }
    public FancontrolProfileStatus? Profiles { get; set; }
    public FancontrolSentinelStatus? Sentinel { get; set; }
}

/// <summary>fan_brain.ps1 heartbeat (brain_state.json, ~5 s cadence).</summary>
public class FancontrolBrainStatus
{
    /// <summary>R2 contract version ("v" in the state file); null = pre-R2 producer.</summary>
    public int? SchemaVersion { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public bool Stale { get; set; }
    public string Mode { get; set; } = "";
    public bool Game { get; set; }
    public bool Night { get; set; }
    public bool Alarm { get; set; }
    public bool LhmOk { get; set; }
    public double? CpuTemp { get; set; }
    public double? CpuWatts { get; set; }
    public double? CpuLoad { get; set; }
    public double? GpuTemp { get; set; }
    public double? GpuWatts { get; set; }
    public double? GpuLoad { get; set; }
    public double? Coolant { get; set; }
    public int? PumpRpm { get; set; }
    public int? CaseDemand { get; set; }
    public int? RadDemand { get; set; }
    public string? ActiveApp { get; set; }
    public List<string> RunningApps { get; set; } = [];
}

/// <summary>foreground_watch.ps1 auto-profiler state (fgwatch_state.json, ~3 s cadence).</summary>
public class FancontrolProfileStatus
{
    /// <summary>R2 contract version ("v" in the state file); null = pre-R2 producer.</summary>
    public int? SchemaVersion { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public bool Stale { get; set; }
    public string? LastAppliedProfile { get; set; }
    public bool Enabled { get; set; }
    public int MappedPrograms { get; set; }
    public string? ForegroundExe { get; set; }
    public List<string> VetoApps { get; set; } = [];
}

/// <summary>Hourly sentinel health verdict (sentinel_state.json).</summary>
public class FancontrolSentinelStatus
{
    /// <summary>R2 contract version ("v" in the state file); null = pre-R2 producer.</summary>
    public int? SchemaVersion { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public bool Stale { get; set; }
    public bool Pass { get; set; }
    public List<string> Issues { get; set; } = [];
    public double? CoolantAvg { get; set; }
    public double? CoolantMax { get; set; }
}
