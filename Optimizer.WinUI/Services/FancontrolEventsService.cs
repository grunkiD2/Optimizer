using System.Text.Json;

namespace Optimizer.WinUI.Services;

/// <summary>One line from the Fancontrol system's events.jsonl audit stream.</summary>
public sealed record FancontrolEvent(DateTimeOffset Ts, string Src, string Msg);

public interface IFancontrolEventsService
{
    bool IsConfigured { get; }

    /// <summary>Last <paramref name="count"/> events, newest first. Empty when unconfigured/unreadable.</summary>
    IReadOnlyList<FancontrolEvent> ReadTail(int count);
}

/// <summary>
/// Etape 1: read-only viewer over the engine's event stream (state\events.jsonl — every daemon
/// logs here; the brain is its only rotator, so the file stays bounded). Strictly read-only per
/// docs/MACHINE-OWNERSHIP.md; share-tolerant because writers append while we read.
/// </summary>
public class FancontrolEventsService(string stateDir) : IFancontrolEventsService
{
    private readonly string _path = (stateDir ?? "").Trim().Length == 0
        ? "" : Path.Combine(stateDir!.Trim(), "events.jsonl");

    public bool IsConfigured => _path.Length > 0;

    public IReadOnlyList<FancontrolEvent> ReadTail(int count)
    {
        if (!IsConfigured || count <= 0) return [];
        try
        {
            if (!File.Exists(_path)) return [];
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            // The brain's rotation keeps the file small (a few MB worst case) — a full read with
            // a tail-take is simpler and safer than seek-backwards over multi-byte UTF-8.
            var lines = new List<string>();
            while (sr.ReadLine() is { } line)
                if (line.Length > 0) lines.Add(line);

            var result = new List<FancontrolEvent>(Math.Min(count, lines.Count));
            for (var i = lines.Count - 1; i >= 0 && result.Count < count; i--)
                if (ParseLine(lines[i]) is { } ev) result.Add(ev);
            return result;
        }
        catch
        {
            return [];   // mid-rotation/mid-write — the next 5 s refresh wins
        }
    }

    /// <summary>Parses one events.jsonl line ({ts,src,msg}); null when malformed.</summary>
    public static FancontrolEvent? ParseLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (!r.TryGetProperty("ts", out var tsEl) || tsEl.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(tsEl.GetString(), out var ts))
                return null;
            var src = r.TryGetProperty("src", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "?" : "?";
            var msg = r.TryGetProperty("msg", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
            return new FancontrolEvent(ts, src, msg);
        }
        catch (JsonException) { return null; }
    }
}
