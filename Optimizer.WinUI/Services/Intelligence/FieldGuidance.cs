// FieldGuidance.cs
namespace Optimizer.WinUI.Services.Intelligence;

/// <summary>
/// Per-editor-field plain-language guidance + the opposing cost (trade-off visibility is the
/// project's soul, §2). Keyed by the lag-2 field name used in BuildProfilePatch (mode/bright/hdr/
/// lyd/power/lys/optimizer). Static catalog — mirrors HdrModeCatalog. NOT measured data; this is the
/// "· afledt" explanation layer the ⓘ glyph shows. Measured per-app numbers come from
/// ProfileIntelligenceService, not here.
/// </summary>
public static class FieldGuidance
{
    public sealed record Guidance(string Field, string Hint, string Tradeoff);

    private static readonly IReadOnlyList<Guidance> All =
    [
        new("mode",  "Skærmens GameVisual-tilstand (FPS/Racing/Cinema/sRGB) — bestemmer farve/kontrast/respons.",
                     "FPS = hurtigst respons men fladere farver · Cinema/sRGB = rigere farver men blødere."),
        new("bright","Skærmens lysstyrke (0-100). Under HDR er feltet informativt — panelet ejer lysstyrken.",
                     "Højere = mere synligt i lyse rum · lavere = mindre OLED-slid og øjentræthed."),
        new("hdr",   "Tænder HDR via Windows (pålideligt). I HDR låser PG27UCDM sin OSD — type/lysstyrke sættes på skærmen.",
                     "HDR = højere kontrast/peak-lys · men DDC-felter (mode/lysstyrke) bliver informative."),
        new("lyd",   "Standard-lydenhed for denne profil (afspilning, evt. comms).",
                     "Headset = privat/lav latens · højttalere = delt lyd."),
        new("power", "Windows/Lasso strømplan. Lasso ejer skift-automatikken; her vises den read-only.",
                     "Højere ydelse = mere watt/varme/støj · balanceret = køligere og stillere."),
        new("lys",   "Razer Chroma tastatur/enheds-lys for profilen (farve + mode).",
                     "Statisk farve = rolig stemning · ambient = informativt men mere bevægelse."),
        new("optimizer","Optimizer-preset der auto-anvendes når profilen aktiveres (follower-laget).",
                     "Bundet preset = ét klik mere konsistent · men ekstra Windows-tweaks ved hvert skift."),
    ];

    public static Guidance? For(string field) => All.FirstOrDefault(g => g.Field == field);
}
