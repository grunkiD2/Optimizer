namespace Optimizer.WinUI.Models;

public enum SensorKind { Temperature, Clock, Voltage, Fan, Load, Power, Data }

public class SensorReading
{
    public string Name { get; set; } = "";
    public string HardwareName { get; set; } = "";
    public SensorKind Kind { get; set; }
    public double? Value { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public string Unit { get; set; } = "";

    public string DisplayValue => Value.HasValue ? $"{Value:F1} {Unit}" : "—";
}

public class HardwareSnapshot
{
    public List<SensorReading> CpuTemperatures { get; set; } = [];
    public List<SensorReading> CpuClocks { get; set; } = [];
    public List<SensorReading> CpuLoads { get; set; } = [];
    public List<SensorReading> CpuPowers { get; set; } = [];
    public List<SensorReading> GpuTemperatures { get; set; } = [];
    public List<SensorReading> GpuClocks { get; set; } = [];
    public List<SensorReading> GpuLoads { get; set; } = [];
    public List<SensorReading> GpuPowers { get; set; } = [];
    public List<SensorReading> GpuMemory { get; set; } = [];
    public List<SensorReading> FanSpeeds { get; set; } = [];
    public List<SensorReading> Voltages { get; set; } = [];
    public List<SensorReading> StorageTemperatures { get; set; } = [];

    public double? CpuPackageTemperatureC =>
        CpuTemperatures.FirstOrDefault(s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))?.Value
        ?? CpuTemperatures.FirstOrDefault()?.Value;

    public double? GpuTemperatureC => GpuTemperatures.FirstOrDefault()?.Value;

    public double? GpuCoreMhz =>
        GpuClocks.FirstOrDefault(s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))?.Value;

    public double? GpuMemoryMhz =>
        GpuClocks.FirstOrDefault(s => s.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase))?.Value;

    public double? GpuPowerWatts => GpuPowers.FirstOrDefault()?.Value;

    public double? CpuPowerWatts =>
        CpuPowers.FirstOrDefault(s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))?.Value;
}
