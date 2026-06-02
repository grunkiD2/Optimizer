using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IUpdateService
{
    Task<IReadOnlyList<WindowsUpdateInfo>> GetRecentWindowsUpdatesAsync(int days = 60);
    Task<IReadOnlyList<AppUpdateInfo>> GetWingetUpdatesAsync();
    Task<bool> RunWindowsUpdateCheckAsync();
    Task<bool> UpgradeAppAsync(string appId);
    Task<bool> UpgradeAllAppsAsync();
    Task<string> GetBiosInfoAsync();
}
