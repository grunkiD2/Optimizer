namespace Optimizer.WinUI.Services;

/// <summary>
/// PG27UCDM HDR OSD modes (research: docs\research-kb\pg27ucdm-hdr-modes.md). These are OSD-LOCKED —
/// NOT programmatically settable (3-way proven). The editor offers them as a named choice + an OSD
/// recipe; selecting one never drives the panel. HDR on/off itself IS programmatic (display.hdr).
/// </summary>
public static class HdrModeCatalog
{
    public record HdrMode(string Key, string Group, string Label, string WhatItDoes);

    public static readonly IReadOnlyList<HdrMode> All =
    [
        new("trueblack", "HDR10", "DisplayHDR 400 True Black", "Mest præcis, lavere peak (~450 nit) — film/SDR-korrekthed."),
        new("gaming",    "HDR10", "Gaming HDR",                "Lysere skygger, tidlig roll-off — punchy gaming."),
        new("cinema",    "HDR10", "Cinema HDR",                "Senere roll-off, lysere mid-grey — balanceret film."),
        new("console",   "HDR10", "Console HDR (+D.B.B.)",     "Højeste peak ~1000 nit ved små highlights (ABL); mindre præcis."),
        new("dv-bright", "Dolby Vision", "Dolby Vision Bright", "DV-tonemapping, lyst rum."),
        new("dv-dark",   "Dolby Vision", "Dolby Vision Dark",   "DV-tonemapping, mørkt rum — film-præcist."),
        new("dv-game",   "Dolby Vision", "Dolby Vision Game",   "DV med lav latency — DV-spil."),
    ];

    public static string Label(string key) => All.FirstOrDefault(m => m.Key == key)?.Label ?? "";

    /// <summary>Manual OSD steps to set this mode on the monitor (it is not automatable).</summary>
    public static string Recipe(string key)
    {
        var m = All.FirstOrDefault(x => x.Key == key);
        if (m is null) return "";
        var fmt = m.Group == "Dolby Vision" ? "Dolby Vision" : "HDR10";
        return $"Sæt skærmen: Image ▸ HDR Format ▸ {fmt} ▸ {m.Label}";
    }
}
