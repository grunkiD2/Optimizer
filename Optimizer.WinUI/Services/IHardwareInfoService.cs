using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IHardwareInfoService
{
    Task<HardwareInfo> GetHardwareInfoAsync();
}
