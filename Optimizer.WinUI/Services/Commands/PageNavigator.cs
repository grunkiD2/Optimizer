namespace Optimizer.WinUI.Services.Commands;

/// <summary>
/// Resolves page tags to navigation. The actual navigation delegate + tag list are injected
/// by MainWindow at startup so this stays free of WinUI types (and testable).
/// </summary>
public sealed class PageNavigator : IPageNavigator
{
    private Func<string, bool> _navigate = _ => false;
    private IReadOnlyList<string> _pages = [];

    public IReadOnlyList<string> Pages => _pages;
    public bool NavigateTo(string tag) => _navigate(tag);

    /// <summary>Called once by MainWindow after the Frame is ready.</summary>
    public void Configure(IReadOnlyList<string> pages, Func<string, bool> navigate)
    {
        _pages = pages;
        _navigate = navigate;
    }
}
