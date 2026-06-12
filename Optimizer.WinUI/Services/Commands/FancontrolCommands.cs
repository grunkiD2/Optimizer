using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

/// <summary>Read-only snapshot of the Fancontrol federation (fan brain / profiles / sentinel).</summary>
public sealed class GetFancontrolStatusCommand(IFancontrolStatusService status) : IAppCommand
{
    public string Id => "get_fancontrol_status";
    public string Description => "Get the Fancontrol cooling system's live status: brain mode, coolant temp, pump RPM, fan demands, CPU/GPU temps+watts, active display profile, and the hourly sentinel health verdict.";
    public JsonElement ParametersSchema => SchemaJson.Empty;
    public bool IsReadOnly => true;
    public bool RequiresConfirmation => false;

    public Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        if (!status.IsConfigured) return Task.FromResult(CommandResult.Fail("Fancontrol federation is not configured on this machine."));
        var s = status.GetStatus();
        if (s?.Brain is not { } b) return Task.FromResult(CommandResult.Fail("Fancontrol state not readable."));
        var summary = $"Mode {b.Mode}{(b.Alarm ? " ALARM" : "")}{(b.Stale ? " (STALE)" : "")} · coolant {b.Coolant:F1}°C · pump {b.PumpRpm} RPM · demands case {b.CaseDemand}/rad {b.RadDemand}% · CPU {b.CpuTemp:F0}°C {b.CpuWatts:F0} W · GPU {b.GpuTemp:F0}°C {b.GpuWatts:F0} W · profile {s.Profiles?.LastAppliedProfile ?? "?"} · sentinel {(s.Sentinel == null ? "?" : s.Sentinel.Pass && s.Sentinel.Issues.Count == 0 ? "PASS" : $"{s.Sentinel.Issues.Count} issue(s)")}";
        return Task.FromResult(CommandResult.Ok(summary, s));
    }
}

/// <summary>Apply a Fancontrol display/power profile via the federation's ctl.ps1 contract.</summary>
public sealed class FancontrolApplyProfileCommand(IFancontrolCommandService fancontrol) : IAppCommand
{
    public string Id => "fancontrol_apply_profile";
    public string Description => "Apply a Fancontrol display/power profile by NAME (e.g. Desktop, Night, Competitive, AAA-SDR, AAA-HDR, Film). This drives monitor DDC settings, HDR, the power plan and fan pre-boost through the Fancontrol system's own command channel. Use get_fancontrol_status first to see the current profile.";
    public JsonElement ParametersSchema { get; } = SchemaJson.Parse("""
        {"type":"object",
         "properties":{"profile":{"type":"string","description":"Profile name, optionally with +module suffix (e.g. 'Game+lyd')."}},
         "required":["profile"]}
        """);
    public bool IsReadOnly => false;
    public bool RequiresConfirmation => true;

    public async Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var profile = args.TryGetProperty("profile", out var p) ? p.GetString() ?? "" : "";
        var r = await fancontrol.ApplyProfileAsync(profile, ct);
        return r.Success ? CommandResult.Ok($"Applied Fancontrol profile '{profile}'. {r.Output}") : CommandResult.Fail(r.Output);
    }
}

/// <summary>Force or release the fan brain's night mode.</summary>
public sealed class FancontrolNightCommand(IFancontrolCommandService fancontrol) : IAppCommand
{
    public string Id => "fancontrol_night";
    public string Description => "Force the Fancontrol fan brain's quiet NIGHT mode on/off, or 'auto' to return control to its schedule. Night mode lowers fan floors for silence.";
    public JsonElement ParametersSchema { get; } = SchemaJson.Parse("""
        {"type":"object",
         "properties":{"mode":{"type":"string","enum":["on","off","auto"],"description":"on=force night, off=force day, auto=follow schedule."}},
         "required":["mode"]}
        """);
    public bool IsReadOnly => false;
    public bool RequiresConfirmation => true;

    public async Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var mode = args.TryGetProperty("mode", out var m) ? m.GetString() ?? "" : "";
        var r = await fancontrol.SetNightAsync(mode, ct);
        return r.Success ? CommandResult.Ok($"Night mode set to '{mode}'. {r.Output}") : CommandResult.Fail(r.Output);
    }
}

/// <summary>Acknowledge Fancontrol sentinel alerts (they regenerate if the cause persists).</summary>
public sealed class FancontrolAckAlertsCommand(IFancontrolCommandService fancontrol) : IAppCommand
{
    public string Id => "fancontrol_ack_alerts";
    public string Description => "Acknowledge (clear) the Fancontrol sentinel's current alerts, optionally with a note. Safe: the sentinel re-raises alerts within the hour if the cause persists.";
    public JsonElement ParametersSchema { get; } = SchemaJson.Parse("""
        {"type":"object",
         "properties":{"note":{"type":"string","description":"Optional attribution note stored in the event log."}}}
        """);
    public bool IsReadOnly => false;
    public bool RequiresConfirmation => false; // mild + self-healing: sentinel regenerates within the hour

    public async Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var note = args.TryGetProperty("note", out var n) ? n.GetString() : null;
        var r = await fancontrol.AckAlertsAsync(note, ct);
        return r.Success ? CommandResult.Ok($"Alerts acknowledged. {r.Output}") : CommandResult.Fail(r.Output);
    }
}
