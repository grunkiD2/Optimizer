namespace Optimizer.WinUI.Models.Gpu;

public enum GpuVendor { Nvidia, Amd, Intel, Unknown }

public class GpuControlState
{
    public int CoreClockOffsetMhz { get; set; }     // 0 = stock
    public int MemoryClockOffsetMhz { get; set; }
    public int PowerLimitPercent { get; set; } = 100;
    public int TempLimitC { get; set; } = 83;
    public int? FanPercent { get; set; }              // null = auto fan curve
}

public class GpuControlCapabilities
{
    public bool CanReadTelemetry { get; set; }
    public bool CanSetCoreOffset { get; set; }
    public bool CanSetMemoryOffset { get; set; }
    public bool CanSetPowerLimit { get; set; }
    public bool CanSetTempLimit { get; set; }
    public bool CanSetFan { get; set; }
    public (int Min, int Max) CoreOffsetRangeMhz { get; set; } = (-200, 300);
    public (int Min, int Max) MemoryOffsetRangeMhz { get; set; } = (-500, 1500);
    public (int Min, int Max) PowerLimitRangePercent { get; set; } = (50, 120);
}

public class GpuTelemetrySnapshot
{
    public string Name { get; set; } = "";
    public GpuVendor Vendor { get; set; }
    public double? CoreClockMhz { get; set; }
    public double? MemoryClockMhz { get; set; }
    public double? TemperatureC { get; set; }
    public double? PowerWatts { get; set; }
    public double? FanRpm { get; set; }
    public double? FanPercent { get; set; }
    public double? LoadPercent { get; set; }
    public double? VramUsedMb { get; set; }
}
