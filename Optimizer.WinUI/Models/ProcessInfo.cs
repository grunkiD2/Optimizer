namespace Optimizer.WinUI.Models;

/// <summary>A snapshot of a running process for the dashboard's top-processes panel.</summary>
public class ProcessInfo
{
    public int Pid { get; set; }
    public string Name { get; set; } = string.Empty;
    public long WorkingSetBytes { get; set; }

    public string MemoryText => $"{WorkingSetBytes / 1024.0 / 1024.0:F0} MB";
}
