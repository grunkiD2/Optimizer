using System.Diagnostics;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IProcessService
{
    /// <summary>Top processes by working-set memory.</summary>
    IReadOnlyList<ProcessInfo> GetTopProcesses(int count);

    /// <summary>Attempts to terminate a process by id. Returns true on success.</summary>
    bool KillProcess(int pid);

    // ── Priority + affinity ───────────────────────────────────────────────

    /// <summary>Returns all user-visible processes (sorted by memory).</summary>
    IReadOnlyList<ProcessPriorityInfo> GetUserProcesses();

    /// <summary>Sets the priority of a process. Returns true on success.</summary>
    bool SetProcessPriority(int pid, ProcessPriorityClass priority);

    /// <summary>Number of logical processors on this machine.</summary>
    int LogicalCoreCount { get; }

    /// <summary>
    /// Returns the current affinity bitmask for the given process,
    /// or null if the process has exited or access is denied.
    /// </summary>
    long? GetProcessAffinityMask(int pid);

    /// <summary>
    /// Sets an explicit per-core affinity mask.
    /// Returns false if mask is zero, exceeds logical core count, or the OS call fails.
    /// </summary>
    bool SetProcessAffinityMask(int pid, long mask);

    /// <summary>Back-compat: set all cores (true) or half cores (false).</summary>
    bool SetProcessAffinity(int pid, bool allCores);
}
