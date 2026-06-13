// ConfidenceTier.cs
namespace Optimizer.WinUI.Services.Intelligence;

/// <summary>
/// Epistemic status of an intelligence line — first-class UI (Profil 2.0 redesign §2).
/// Measurement ALWAYS outranks external; external outranks derived. "Tydelig effekt" may only
/// be claimed about a Measured delta.
/// </summary>
public enum ConfidenceTier { Derived, External, Measured }

public static class ConfidenceTierExtensions
{
    /// <summary>Danish badge text shown next to every intelligence line.</summary>
    public static string Badge(this ConfidenceTier t) => t switch
    {
        ConfidenceTier.Measured => "✓ målt",
        ConfidenceTier.External => "~ ekstern",
        _ => "· afledt",
    };
}

/// <summary>One fact in the intelligence picture: value + where it came from + how much we trust it.
/// Optional <see cref="SourceUrl"/> backs the "ⓘ bevis" affordance for external (web) lines.</summary>
public sealed record EvidenceLine(string Label, string Value, string Source, ConfidenceTier Tier, string? SourceUrl = null);

/// <summary>A titled group of evidence lines (Identitet · Grafik/Display · Ydelse (målt) · Termik &amp; Støj (målt) · Kendt info).</summary>
public sealed record IntelGroup(string Title, IReadOnlyList<EvidenceLine> Lines);

/// <summary>The whole "Hvad ved vi om &lt;app&gt;" picture for one profile/app context.
/// <see cref="MaturityHave"/>/<see cref="MaturityTarget"/> drive the "endnu ingen måling — N sessioner til baseline" indicator.</summary>
public sealed record IntelligencePicture(
    string AppName,
    string ProfileName,
    IReadOnlyList<IntelGroup> Groups,
    int MaturityHave,
    int MaturityTarget);
