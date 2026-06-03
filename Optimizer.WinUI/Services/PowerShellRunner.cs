using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Optimizer.WinUI.Services;

public class PowerShellRunner : IPowerShellRunner
{
    public async Task<string?> RunAsync(string script, int timeoutMs = 30_000)
    {
        try
        {
            // Write script to a temp .ps1 file to avoid quote-escaping issues
            var scriptPath = Path.Combine(Path.GetTempPath(), $"opt-{Guid.NewGuid():N}.ps1");
            await File.WriteAllTextAsync(scriptPath, script, Encoding.UTF8);

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File \"{scriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };

                using var proc = Process.Start(psi);
                if (proc == null) return null;

                using var cts = new CancellationTokenSource(timeoutMs);
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();

                try
                {
                    await proc.WaitForExitAsync(cts.Token);
                    var stdout = await stdoutTask;
                    return proc.ExitCode == 0 ? stdout : null;
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(5000);
                    }
                    catch { }
                    return null;
                }
            }
            finally
            {
                try { File.Delete(scriptPath); } catch { }
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error("PowerShell execution failed", ex);
            return null;
        }
    }

    public async Task<T?> RunJsonAsync<T>(string script, int timeoutMs = 30_000)
    {
        var output = await RunAsync(script, timeoutMs);
        if (string.IsNullOrWhiteSpace(output)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(output, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch { return default; }
    }
}
