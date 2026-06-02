using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface ISecurityService
{
    Task<DefenderStatus> GetDefenderStatusAsync();
    Task<FirewallStatus> GetFirewallStatusAsync();
    Task<IReadOnlyList<BitLockerVolume>> GetBitLockerStatusAsync();
    Task<int> GetSecurityScoreAsync();
    Task<bool> RunQuickScanAsync();
}
