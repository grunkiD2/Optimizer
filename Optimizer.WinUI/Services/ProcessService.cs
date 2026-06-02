using System.Diagnostics;

using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class ProcessService : IProcessService
{
    // ── Priority manager helpers ─────────────────────────────────────────────

    public IReadOnlyList<ProcessPriorityInfo> GetUserProcesses()
    {
        var list = new List<ProcessPriorityInfo>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                // Only include processes that have a visible window or recognisable name
                if (p.MainWindowHandle == IntPtr.Zero && string.IsNullOrEmpty(p.MainWindowTitle))
                    continue;

                list.Add(new ProcessPriorityInfo
                {
                    Pid = p.Id,
                    Name = p.ProcessName,
                    Priority = p.PriorityClass.ToString(),
                    MemoryBytes = p.WorkingSet64
                });
            }
            catch
            {
                // process exited or access denied — skip
            }
            finally
            {
                p.Dispose();
            }
        }
        return list.OrderByDescending(x => x.MemoryBytes).Take(50).ToList();
    }

    public bool SetProcessPriority(int pid, ProcessPriorityClass priority)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.PriorityClass = priority;
            EngineLog.Write($"Set priority of {p.ProcessName} ({pid}) to {priority}.");
            return true;
        }
        catch (Exception ex)
        {
            EngineLog.Error($"Could not set priority for process {pid}", ex);
            return false;
        }
    }

    public bool SetProcessAffinity(int pid, bool allCores)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (allCores)
                p.ProcessorAffinity = (IntPtr)((1L << Environment.ProcessorCount) - 1);
            else
                p.ProcessorAffinity = (IntPtr)((1L << (Environment.ProcessorCount / 2)) - 1);
            EngineLog.Write($"Set affinity of {p.ProcessName} ({pid}) to {(allCores ? "all" : "half")} cores.");
            return true;
        }
        catch (Exception ex)
        {
            EngineLog.Error($"Could not set affinity for process {pid}", ex);
            return false;
        }
    }

    // ── Existing members ────────────────────────────────────────────────────

    public IReadOnlyList<ProcessInfo> GetTopProcesses(int count)
    {
        var list = new List<ProcessInfo>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                list.Add(new ProcessInfo { Pid = p.Id, Name = p.ProcessName, WorkingSetBytes = p.WorkingSet64 });
            }
            catch
            {
                // process exited or access denied — skip
            }
            finally
            {
                p.Dispose();
            }
        }

        return list
            .OrderByDescending(p => p.WorkingSetBytes)
            .Take(count)
            .ToList();
    }

    public bool KillProcess(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill();
            EngineLog.Write($"Killed process {p.ProcessName} ({pid}).");
            return true;
        }
        catch (Exception ex)
        {
            EngineLog.Error($"Could not kill process {pid}", ex);
            return false;
        }
    }
}
