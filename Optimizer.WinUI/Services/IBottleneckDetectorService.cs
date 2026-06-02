using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IBottleneckDetectorService
{
    Task<BottleneckReport> DetectAsync(IProgress<string>? progress = null);
}
