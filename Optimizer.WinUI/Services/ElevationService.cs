using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;

namespace Optimizer.WinUI.Services;

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
