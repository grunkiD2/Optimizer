namespace Optimizer.WinUI.Services.Commands;

/// <summary>Abstraction over shell navigation so commands can navigate without depending on the Frame.</summary>
public interface IPageNavigator
{
    /// <summary>The user-facing page tags that can be navigated to (e.g. "Dashboard", "Diagnostics").</summary>
    IReadOnlyList<string> Pages { get; }

    /// <summary>Navigate to the page with the given tag (case-insensitive). Returns false if unknown.</summary>
    bool NavigateTo(string tag);
}
