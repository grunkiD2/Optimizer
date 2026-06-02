namespace Optimizer.WinUI.Models;

public class ProcessPriorityInfo
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public string Priority { get; set; } = "Normal";
    public long MemoryBytes { get; set; }

    public string MemoryText => $"{MemoryBytes / 1024.0 / 1024.0:F0} MB";
    public string PidText => Pid.ToString();
}
