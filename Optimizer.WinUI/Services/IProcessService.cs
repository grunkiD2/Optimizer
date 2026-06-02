using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IProcessService
{
    /// <summary>Top processes by working-set memory.</summary>
    IReadOnlyList<ProcessInfo> GetTopProcesses(int count);

    /// <summary>Attempts to terminate a process by id. Returns true on success.</summary>
    bool KillProcess(int pid);
}
