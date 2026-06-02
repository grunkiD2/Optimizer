namespace Optimizer.WinUI.Models;

/// <summary>
/// Human-readable explanation of what an optimization does, surfaced in the UI
/// *before* the change is applied so the user can make an informed decision.
/// </summary>
public class OptimizationInfo
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Friendly title, e.g. "Disable window animations".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>One- or two-sentence plain-English summary of the effect.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>The concrete changes made (registry paths/values, commands, files).</summary>
    public List<string> Changes { get; set; } = new();

    /// <summary>Benefits of applying it.</summary>
    public List<string> Pros { get; set; } = new();

    /// <summary>Downsides, risks, or trade-offs.</summary>
    public List<string> Cons { get; set; } = new();

    /// <summary>Guidance on when/whether to use it and how to get the best result.</summary>
    public string Recommendation { get; set; } = string.Empty;

    /// <summary>Optional further/alternative implementation ideas.</summary>
    public string? SuggestedImplementation { get; set; }

    public bool RequiresAdmin { get; set; }

    public bool Reversible { get; set; } = true;

    /// <summary>True if a sign-out or restart is needed before the change fully takes effect.</summary>
    public bool RequiresRestart { get; set; }

    /// <summary>The optimization's author. Empty for built-in optimizations.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// True when this optimization was loaded from a community plugin manifest
    /// rather than compiled into the built-in handler set. Used by the UI to show a badge.
    /// </summary>
    public bool IsPlugin { get; set; }
}
