namespace Optimizer.WinUI.Services;

public interface IPowerShellRunner
{
    /// <summary>Run a PowerShell script and return stdout. Returns null on failure.</summary>
    Task<string?> RunAsync(string script, int timeoutMs = 30_000);

    /// <summary>Run a script that produces JSON output and deserialize it. Returns null on parse failure.</summary>
    Task<T?> RunJsonAsync<T>(string script, int timeoutMs = 30_000);
}
