using System;

namespace WindowsOptimizer.Models;

public class SystemResource
{
    public DateTime Timestamp { get; set; }

    // CPU Metrics
    public double CpuUsagePercentage { get; set; }
    public int TotalProcessors { get; set; }
    public long CyclesPerSecond { get; set; }

    // Memory Metrics
    public long TotalPhysicalMemory { get; set; }
    public long AvailablePhysicalMemory { get; set; }
    public long TotalVirtualMemory { get; set; }
    public long AvailableVirtualMemory { get; set; }
    public long CommitCharge { get; set; }

    // Disk Metrics
    public double DiskReadSpeed { get; set; }
    public double DiskWriteSpeed { get; set; }
    public long DiskReadOperationsPerSecond { get; set; }
    public long DiskWriteOperationsPerSecond { get; set; }

    // Network Metrics
    public double NetworkInSpeed { get; set; }
    public double NetworkOutSpeed { get; set; }
    public long NetworkInBytesTotal { get; set; }
    public long NetworkOutBytesTotal { get; set; }

    // GPU Metrics
    public double GpuUsagePercentage { get; set; }
    public long GpuMemoryUsage { get; set; }

    // Temperature
    public double CpuTemperature { get; set; }
    public double GpuTemperature { get; set; }
}
