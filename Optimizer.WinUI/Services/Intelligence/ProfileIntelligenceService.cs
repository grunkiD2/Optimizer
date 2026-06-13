// ProfileIntelligenceService.cs
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.Services.Intelligence;

/// <summary>
/// Assembles the intelligence picture from MEASURED on-machine data (GetMappedPrograms learned stats +
/// PresentMon summaries) plus the optional EXTERNAL web tier. Measurement always outranks external (§2).
/// UI-free → unit-testable. Read-only toward the engine.
/// </summary>
public sealed class ProfileIntelligenceService : IProfileIntelligenceService
{
    private const int BaselineTarget = 3; // "3 sessioner til første baseline" (spec §3)

    private readonly IFancontrolCommandService _fc;
    private readonly PresentMonSummaryReader _presentmon;
    private readonly IAppWebLookup? _web;

    public ProfileIntelligenceService(IFancontrolCommandService fc, PresentMonSummaryReader presentmon, IAppWebLookup? webLookup)
    {
        _fc = fc;
        _presentmon = presentmon;
        _web = webLookup;
    }

    public IntelligencePicture Build(string profileName, string? foregroundExe)
    {
        var programs = SafeMapped();
        var profile = SafeProfiles().FirstOrDefault(p => p.Name == profileName);

        // App-konteksten: foreground hvis mappet til profilen, ellers første program mappet til profilen.
        var prog = (foregroundExe is not null
                        ? programs.FirstOrDefault(p => string.Equals(p.Exe, foregroundExe, StringComparison.OrdinalIgnoreCase))
                        : null)
                   ?? programs.FirstOrDefault(p => p.Profile == profileName);
        var appExe = prog?.Exe ?? foregroundExe ?? "(ingen app i fokus)";

        var groups = new List<IntelGroup>();

        // ── Identitet ──────────────────────────────────────────────────────────────
        var identity = new List<EvidenceLine>
        {
            new("Profil", profileName, "Profil 2.0-datamodel", ConfidenceTier.Measured),
            new("App", prog?.Name ?? appExe, prog is null ? "ingen mapping endnu" : "mappet i programs.json", ConfidenceTier.Measured),
        };
        if (profile is not null)
            identity.Add(new EvidenceLine("Gaming-klassifikation",
                profile.GamingClass ? "gaming (lag-1)" : "ikke-gaming",
                "afledt af systemets HINT_FLOORS-regler", ConfidenceTier.Derived));
        groups.Add(new IntelGroup("Identitet", identity));

        // ── Ydelse (målt) ──────────────────────────────────────────────────────────
        var perf = new List<EvidenceLine>();
        var summary = _presentmon.LatestForApp(appExe);
        int pmCount = _presentmon.CountForApp(appExe);
        if (summary is not null)
        {
            perf.Add(new EvidenceLine("FPS (gns.)", summary.FpsAvg.ToString("0"), "PresentMon, seneste session", ConfidenceTier.Measured));
            perf.Add(new EvidenceLine("1%-low", summary.Fps1Low.ToString("0"), "PresentMon, seneste session", ConfidenceTier.Measured));
            perf.Add(new EvidenceLine("Frametime p95", summary.FtP95.ToString("0.0") + " ms", "PresentMon, seneste session", ConfidenceTier.Measured));
        }
        else
        {
            perf.Add(new EvidenceLine("FPS", $"endnu ingen måling — {BaselineTarget} sessioner til første baseline", "PresentMon", ConfidenceTier.Measured));
        }
        if (prog?.LearnedGpuP95 is double gp95)
            perf.Add(new EvidenceLine("GPU p95 (lært)", gp95.ToString("0.0") + " °C", $"hjernens telemetri ({prog.LearnedSamples ?? 0} samples)", ConfidenceTier.Measured));
        if (prog?.LearnedGpuWatts is double gw)
            perf.Add(new EvidenceLine("GPU watt (lært, gns.)", gw.ToString("0") + " W", "hjernens telemetri", ConfidenceTier.Measured));
        groups.Add(new IntelGroup("Ydelse (målt)", perf));

        // ── Termik & Støj (målt) ─────────────────────────────────────────────────────
        var thermal = new List<EvidenceLine>();
        if (prog?.CaseFloor is int cf) thermal.Add(new EvidenceLine("Case-gulv (autotunet)", cf.ToString(), "lag-1 lært-gulv", ConfidenceTier.Measured));
        if (prog?.RadFloor is int rf) thermal.Add(new EvidenceLine("Rad-gulv", rf.ToString(), "lag-1 lært-gulv", ConfidenceTier.Measured));
        if (thermal.Count == 0) thermal.Add(new EvidenceLine("Gulve", "endnu ingen lærte gulve", "autotune", ConfidenceTier.Measured));
        groups.Add(new IntelGroup("Termik & Støj (målt)", thermal));

        // ── Kendt info / gotchas (ekstern) — kun hvis web-tier'en findes ──────────────
        if (_web is not null)
        {
            var ext = _web.CachedFor(appExe);
            if (ext.Count > 0)
                groups.Add(new IntelGroup("Kendt info (ekstern)", ext));
        }

        int have = (summary is not null ? 1 : 0) + (prog?.LearnedSamples is > 0 ? 1 : 0);
        int maturityHave = System.Math.Min(System.Math.Max(have, pmCount > 0 ? 1 : 0), BaselineTarget);
        return new IntelligencePicture(appExe, profileName, groups, maturityHave, BaselineTarget);
    }

    private IReadOnlyList<FancontrolProgramInfo> SafeMapped()
    {
        try { return _fc.GetMappedPrograms(); } catch { return []; }
    }
    private IReadOnlyList<FancontrolProfile> SafeProfiles()
    {
        try { return _fc.GetProfiles(); } catch { return []; }
    }
}
