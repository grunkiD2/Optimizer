namespace Optimizer.WinUI.Services;

/// <summary>
/// Minimal logging seam for the engine. Defaults to writing to the debugger output; the host app
/// calls <see cref="Configure"/> at startup to also route messages to its real logger (Serilog).
/// Kept dependency-free so the engine doesn't take a hard reference on any logging framework.
/// </summary>
public static class EngineLog
{
    private static Action<string, Exception?>? _sink;

    public static void Configure(Action<string, Exception?> sink) => _sink = sink;

    public static void Write(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        _sink?.Invoke(message, null);
    }

    public static void Error(string message, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"{message}: {ex.Message}");
        _sink?.Invoke(message, ex);
    }
}
