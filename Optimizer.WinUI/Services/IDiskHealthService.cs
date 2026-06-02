using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IDiskHealthService
{
    Task<IReadOnlyList<DiskHealthInfo>> GetDiskHealthAsync();
}
