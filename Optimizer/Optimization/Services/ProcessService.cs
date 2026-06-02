using System.Diagnostics;

using WindowsOptimizer.Models;

namespace WindowsOptimizer.Services;

public interface IProcessService
{
    /// <summary>Top processes by working-set memory.</summary>
    IReadOnlyList<ProcessInfo> GetTopProcesses(int count);

    /// <summary>Attempts to terminate a process by id. Returns true on success.</summary>
    bool KillProcess(int pid);
}

public class ProcessService : IProcessService
{
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
