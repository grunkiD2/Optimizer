using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;

namespace WindowsOptimizer.Services;

/// <summary>
/// Detects and requests administrator elevation. Several system-wide tweaks
/// (HKLM registry, power schemes) require an elevated process to take effect.
/// </summary>
public interface IElevationService
{
    /// <summary>True if the current process is running with administrator rights.</summary>
    bool IsElevated { get; }

    /// <summary>
    /// Attempts to relaunch the current executable elevated (UAC prompt).
    /// Returns true if a new elevated process was started (the caller should then exit).
    /// </summary>
    bool TryRelaunchElevated();
}

public class ElevationService : IElevationService
{
    public bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public bool TryRelaunchElevated()
    {
        if (IsElevated)
        {
            return false;
        }

        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName
                          ?? Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(exePath))
            {
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas" // triggers the UAC elevation prompt
            };

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            // User declined the UAC prompt or relaunch failed.
            return false;
        }
    }
}
