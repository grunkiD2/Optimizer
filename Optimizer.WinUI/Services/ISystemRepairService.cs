using System.Diagnostics;

namespace Optimizer.WinUI.Services;

public interface ISystemRepairService
{
    Task<bool> LaunchMemoryTestAsync();
    Task<bool> LaunchChkdskAsync(string drive);
    Task<bool> RunSfcScanAsync(IProgress<string>? progress = null);
    Task<bool> RunDismRepairAsync(IProgress<string>? progress = null);
}

public class SystemRepairService : ISystemRepairService
{
    public Task<bool> LaunchMemoryTestAsync()
    {
        try
        {
            // mdsched.exe shows a dialog asking to restart now or on next boot
            Process.Start(new ProcessStartInfo("mdsched.exe") { UseShellExecute = true });
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            EngineLog.Error("LaunchMemoryTestAsync failed", ex);
            return Task.FromResult(false);
        }
    }

    public async Task<bool> LaunchChkdskAsync(string drive)
    {
        // Schedule chkdsk on next boot using chkntfs /C
        // (chkdsk /f requires a reboot on system drives; chkntfs schedules it cleanly)
        var psi = new ProcessStartInfo("chkntfs.exe", $"/C {drive}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        try
        {
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                return proc.ExitCode == 0;
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error($"LaunchChkdskAsync({drive}) failed", ex);
        }
        return false;
    }

    public async Task<bool> RunSfcScanAsync(IProgress<string>? progress = null)
    {
        progress?.Report("Running sfc /scannow (this may take 5-10 minutes)...");
        try
        {
            var psi = new ProcessStartInfo("sfc.exe", "/scannow")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;

            while (!proc.HasExited)
            {
                if (proc.StandardOutput.Peek() >= 0)
                {
                    var line = await proc.StandardOutput.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line)) progress?.Report(line.Trim());
                }
                else
                {
                    await Task.Delay(500);
                }
            }

            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            EngineLog.Error("SFC scan failed", ex);
            return false;
        }
    }

    public async Task<bool> RunDismRepairAsync(IProgress<string>? progress = null)
    {
        progress?.Report("Running DISM /Online /Cleanup-Image /RestoreHealth...");
        try
        {
            var psi = new ProcessStartInfo("DISM.exe", "/Online /Cleanup-Image /RestoreHealth")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;

            while (!proc.HasExited)
            {
                if (proc.StandardOutput.Peek() >= 0)
                {
                    var line = await proc.StandardOutput.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line) &&
                        (line.Contains('%') || line.Contains("complete", StringComparison.OrdinalIgnoreCase)))
                        progress?.Report(line.Trim());
                }
                else
                {
                    await Task.Delay(500);
                }
            }

            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            EngineLog.Error("DISM repair failed", ex);
            return false;
        }
    }
}
