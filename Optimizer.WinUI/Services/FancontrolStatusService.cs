using System.Text.Json;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IFancontrolStatusService
{
    /// <summary>True when AppSettings.FancontrolStateDir points at a state directory.</summary>
    bool IsConfigured { get; }

    /// <summary>Current federation status, or null when not configured.</summary>
    FancontrolStatus? GetStatus();
}

/// <summary>
/// Reads the Fancontrol system's state-file contracts (brain_state.json /
/// fgwatch_state.json / sentinel_state.json) — strictly read-only, see
/// docs/MACHINE-OWNERSHIP.md. The writers replace these files every few seconds;
/// a read can catch a file mid-write, so each section keeps a last-good value
/// that is served (with refreshed staleness) until the next clean read.
/// </summary>
public class FancontrolStatusService : IFancontrolStatusService
{
    // brain ticks every 5 s, fgwatch every 3 s, sentinel hourly.
    private static readonly TimeSpan BrainStaleAfter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProfileStaleAfter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SentinelStaleAfter = TimeSpan.FromHours(2);

    private readonly string _stateDir;
    private FancontrolBrainStatus? _lastBrain;
    private FancontrolProfileStatus? _lastProfiles;
    private FancontrolSentinelStatus? _lastSentinel;

    public FancontrolStatusService(string stateDir) => _stateDir = stateDir?.Trim() ?? "";

    public bool IsConfigured => _stateDir.Length > 0;

    public FancontrolStatus? GetStatus()
    {
        if (!IsConfigured) return null;
        var now = DateTimeOffset.Now;

        _lastBrain = ReadSection("brain_state.json", json => ParseBrain(json), _lastBrain);
        _lastProfiles = ReadSection("fgwatch_state.json", json => ParseProfiles(json), _lastProfiles);
        _lastSentinel = ReadSection("sentinel_state.json", json => ParseSentinel(json), _lastSentinel);

        if (_lastBrain != null) _lastBrain.Stale = now - _lastBrain.Timestamp > BrainStaleAfter;
        if (_lastProfiles != null) _lastProfiles.Stale = now - _lastProfiles.Timestamp > ProfileStaleAfter;
        if (_lastSentinel != null) _lastSentinel.Stale = now - _lastSentinel.Timestamp > SentinelStaleAfter;

        return new FancontrolStatus { Brain = _lastBrain, Profiles = _lastProfiles, Sentinel = _lastSentinel };
    }

    private T? ReadSection<T>(string fileName, Func<string, T> parse, T? lastGood) where T : class
    {
        try
        {
            var path = Path.Combine(_stateDir, fileName);
            if (!File.Exists(path)) return lastGood;
            return parse(File.ReadAllText(path));
        }
        catch
        {
            // Mid-write/truncated JSON or transient share violation — serve the last good value.
            return lastGood;
        }
    }

    public static FancontrolBrainStatus ParseBrain(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var status = new FancontrolBrainStatus
        {
            Timestamp = GetTimestamp(r),
            Mode = GetString(r, "mode") ?? "",
            Game = GetBool(r, "game"),
            Night = GetBool(r, "night"),
            Alarm = GetBool(r, "alarm"),
            LhmOk = GetBool(r, "lhmOk"),
            Coolant = GetDouble(r, "cool"),
            PumpRpm = (int?)GetDouble(r, "pumpRpm"),
            ActiveApp = GetString(r, "app"),
        };
        if (r.TryGetProperty("cpu", out var cpu) && cpu.ValueKind == JsonValueKind.Object)
        {
            status.CpuLoad = GetDouble(cpu, "l");
            status.CpuTemp = GetDouble(cpu, "t");
            status.CpuWatts = GetDouble(cpu, "w");
        }
        if (r.TryGetProperty("gpu", out var gpu) && gpu.ValueKind == JsonValueKind.Object)
        {
            status.GpuLoad = GetDouble(gpu, "l");
            status.GpuTemp = GetDouble(gpu, "t");
            status.GpuWatts = GetDouble(gpu, "w");
        }
        if (r.TryGetProperty("demands", out var demands) && demands.ValueKind == JsonValueKind.Object)
        {
            status.CaseDemand = (int?)GetDouble(demands, "case");
            status.RadDemand = (int?)GetDouble(demands, "rad");
        }
        if (r.TryGetProperty("apps", out var apps) && apps.ValueKind == JsonValueKind.Array)
            foreach (var a in apps.EnumerateArray())
                if (a.ValueKind == JsonValueKind.String) status.RunningApps.Add(a.GetString()!);
        return status;
    }

    public static FancontrolProfileStatus ParseProfiles(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var status = new FancontrolProfileStatus
        {
            Timestamp = GetTimestamp(r),
            LastAppliedProfile = GetString(r, "lastApplied"),
            Enabled = GetBool(r, "enabled"),
            MappedPrograms = (int?)GetDouble(r, "mapped") ?? 0,
            ForegroundExe = GetString(r, "fg"),
        };
        if (r.TryGetProperty("veto", out var veto) && veto.ValueKind == JsonValueKind.Array)
            foreach (var v in veto.EnumerateArray())
                if (v.ValueKind == JsonValueKind.String) status.VetoApps.Add(v.GetString()!);
        return status;
    }

    public static FancontrolSentinelStatus ParseSentinel(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var status = new FancontrolSentinelStatus
        {
            Timestamp = GetTimestamp(r),
            Pass = GetBool(r, "pass"),
        };
        if (r.TryGetProperty("coolant", out var cool) && cool.ValueKind == JsonValueKind.Object)
        {
            status.CoolantAvg = GetDouble(cool, "avg");
            status.CoolantMax = GetDouble(cool, "max");
        }
        if (r.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array)
            foreach (var i in issues.EnumerateArray())
                if (i.ValueKind == JsonValueKind.String) status.Issues.Add(i.GetString()!);
        return status;
    }

    private static DateTimeOffset GetTimestamp(JsonElement r)
        => GetString(r, "ts") is string ts && DateTimeOffset.TryParse(ts, out var parsed)
            ? parsed : DateTimeOffset.MinValue;

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool GetBool(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static double? GetDouble(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;
}
