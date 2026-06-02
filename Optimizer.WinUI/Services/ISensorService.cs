using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface ISensorService : IDisposable
{
    HardwareSnapshot GetSnapshot();
    bool IsAvailable { get; }
    string? InitializationError { get; }
}
