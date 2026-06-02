using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IServiceManagerService
{
    Task<IReadOnlyList<WindowsServiceInfo>> GetServicesAsync();
    Task<bool> StartServiceAsync(string serviceName);
    Task<bool> StopServiceAsync(string serviceName);
    Task<bool> SetStartupTypeAsync(string serviceName, string startupType);
}
