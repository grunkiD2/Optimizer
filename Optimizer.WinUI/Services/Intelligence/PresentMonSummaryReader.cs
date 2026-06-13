// PresentMonSummaryReader.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Optimizer.WinUI.Services.Intelligence;

/// <summary>One PresentMon session summary written by the engine (engine\presentmon_watch.ps1
/// → state\presentmon\summary-*.json). Read-only consumer; the engine is the sole writer.</summary>
public sealed record PresentMonSummary(
    [property: JsonPropertyName("app")] string App,
    [property: JsonPropertyName("fpsAvg")] double FpsAvg,
    [property: JsonPropertyName("fps1Low")] double Fps1Low,
    [property: JsonPropertyName("ftP95")] double FtP95,
    [property: JsonPropertyName("frames")] long Frames,
    [property: JsonPropertyName("start")] string Start,
    [property: JsonPropertyName("end")] string End);

/// <summary>Reads engine-owned PresentMon summaries from <c>{state dir}\presentmon\summary-*.json</c>.
/// Uses FileShare.ReadWrite so a mid-write by the engine daemon never throws (analyze.ps1 lesson).
/// Path-injectable for tests; the "measured perf" tier of the intelligence picture.</summary>
public sealed class PresentMonSummaryReader
{
    private readonly string _dir;
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public PresentMonSummaryReader(string stateDir)
        => _dir = Path.Combine(stateDir, "presentmon");

    private IEnumerable<PresentMonSummary> ReadAll(string exe)
    {
        if (!Directory.Exists(_dir)) yield break;
        foreach (var path in Directory.EnumerateFiles(_dir, "summary-*.json"))
        {
            PresentMonSummary? s = null;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                s = JsonSerializer.Deserialize<PresentMonSummary>(fs, Opts);
            }
            catch { /* mid-write or malformed → skip this file, never throw */ }
            if (s is not null && string.Equals(s.App, exe, StringComparison.OrdinalIgnoreCase))
                yield return s;
        }
    }

    /// <summary>Newest summary for an exe by capture end timestamp, or null if none.</summary>
    // Ordinal sort assumes timestamps share a consistent local offset (ordinal == chronological within a play period).
    public PresentMonSummary? LatestForApp(string exe)
        => ReadAll(exe).OrderByDescending(s => s.End, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>How many captured sessions exist for this exe (drives the maturity indicator).</summary>
    public int CountForApp(string exe) => ReadAll(exe).Count();
}
