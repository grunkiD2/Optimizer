// IAppWebLookup.cs
namespace Optimizer.WinUI.Services.Intelligence;

/// <summary>External "~ ekstern" tier: a confidence-marked, URL-cited web lookup of app facts
/// (frame-cap, Reflex, HDR support, gotchas). Implemented in Task 8 via the Anthropic web_search tool.
/// Non-blocking + cached + fail-safe: returns an empty list if no key / offline. Measurement always wins.</summary>
public interface IAppWebLookup
{
    /// <summary>Cached external facts for an exe; never throws, may return empty.</summary>
    IReadOnlyList<EvidenceLine> CachedFor(string exe);
}
