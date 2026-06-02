using System.ServiceProcess;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class ServiceManagerService : IServiceManagerService
{
    private static readonly Dictionary<string, (string Rec, string Reason)> Recommendations =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // Safe-to-disable services for most users
        ["DiagTrack"]          = ("Safe",    "Telemetry — safe to disable"),
        ["dmwappushservice"]   = ("Safe",    "WAP push routing — rarely needed"),
        ["RetailDemo"]         = ("Safe",    "Retail demo mode — not needed"),
        ["MapsBroker"]         = ("Safe",    "Downloads map data — disable if not using Maps app"),
        ["WerSvc"]             = ("Safe",    "Windows Error Reporting — safe to disable"),
        ["Fax"]                = ("Safe",    "Fax service — rarely needed"),
        ["WMPNetworkSvc"]      = ("Safe",    "Windows Media Player sharing — disable if not using"),
        ["TabletInputService"] = ("Safe",    "Touch keyboard — disable on desktops"),

        // Caution — may impact comfort/performance
        ["WSearch"]            = ("Caution", "Disables Windows Search indexing"),
        ["SysMain"]            = ("Caution", "SuperFetch — may slow apps on HDD but useful on SSD"),

        // Critical — never disable
        ["AudioSrv"]           = ("Critical", "Audio system — DO NOT DISABLE"),
        ["BFE"]                = ("Critical", "Base Filtering Engine — required for firewall"),
        ["EventLog"]           = ("Critical", "Event Log — required by many services"),
        ["RpcSs"]              = ("Critical", "RPC — core system service, DO NOT DISABLE"),
        ["LSM"]                = ("Critical", "Local Session Manager — required for login"),
        ["Themes"]             = ("Critical", "Theme service — required for visuals"),
    };

    public Task<IReadOnlyList<WindowsServiceInfo>> GetServicesAsync()
    {
        return Task.Run(() =>
        {
            var list = new List<WindowsServiceInfo>();
            try
            {
                var services = ServiceController.GetServices();
                foreach (var svc in services)
                {
                    var info = new WindowsServiceInfo
                    {
                        ServiceName  = svc.ServiceName,
                        DisplayName  = svc.DisplayName,
                        Status       = svc.Status.ToString(),
                        CanStop      = svc.CanStop,
                        StartupType  = GetStartupType(svc.ServiceName),
                    };

                    if (Recommendations.TryGetValue(svc.ServiceName, out var rec))
                    {
                        info.Recommendation       = rec.Rec;
                        info.RecommendationReason = rec.Reason;
                    }

                    list.Add(info);
                }
            }
            catch (Exception ex)
            {
                EngineLog.Error("Failed to enumerate services", ex);
            }

            return (IReadOnlyList<WindowsServiceInfo>)list
                .OrderBy(s => s.DisplayName)
                .ToList();
        });
    }

    private static string GetStartupType(string serviceName)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            if (key == null) return "Unknown";
            var start   = key.GetValue("Start");
            var delayed = key.GetValue("DelayedAutostart");
            return Convert.ToInt32(start ?? 0) switch
            {
                2 => Convert.ToInt32(delayed ?? 0) == 1 ? "AutoDelayed" : "Automatic",
                3 => "Manual",
                4 => "Disabled",
                _ => "Unknown"
            };
        }
        catch { return "Unknown"; }
    }

    public Task<bool> StartServiceAsync(string serviceName)
    {
        return Task.Run(() =>
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                }
                return true;
            }
            catch (Exception ex)
            {
                EngineLog.Error($"Failed to start {serviceName}", ex);
                return false;
            }
        });
    }

    public Task<bool> StopServiceAsync(string serviceName)
    {
        return Task.Run(() =>
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Running && sc.CanStop)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                }
                return true;
            }
            catch (Exception ex)
            {
                EngineLog.Error($"Failed to stop {serviceName}", ex);
                return false;
            }
        });
    }

    public Task<bool> SetStartupTypeAsync(string serviceName, string startupType)
    {
        return Task.Run(() =>
        {
            try
            {
                var (start, delayed) = startupType switch
                {
                    "Automatic"   => (2, 0),
                    "AutoDelayed" => (2, 1),
                    "Manual"      => (3, 0),
                    "Disabled"    => (4, 0),
                    _             => (3, 0)
                };
                using var key = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: true);
                if (key == null) return false;
                key.SetValue("Start", start);
                key.SetValue("DelayedAutostart", delayed);
                return true;
            }
            catch (Exception ex)
            {
                EngineLog.Error($"Failed to set startup for {serviceName}", ex);
                return false;
            }
        });
    }
}
