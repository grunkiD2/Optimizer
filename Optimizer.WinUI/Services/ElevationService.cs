using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;

namespace Optimizer.WinUI.Services;

public class ElevationService : IElevationService
{
    private readonly bool _isElevated;

    public ElevationService()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            _isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            _isElevated = false;
        }
    }

    public bool IsElevated => _isElevated;

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
