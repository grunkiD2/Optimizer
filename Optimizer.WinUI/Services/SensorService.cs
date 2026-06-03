using LibreHardwareMonitor.Hardware;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class SensorService : ISensorService
{
    private Computer? _computer;
    private bool _disposed;

    public bool IsAvailable { get; private set; }
    public string? InitializationError { get; private set; }

    public SensorService()
    {
        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsControllerEnabled = false,
                IsNetworkEnabled = false,
            };
            _computer.Open();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            InitializationError = ex.Message;
            IsAvailable = false;
            EngineLog.Error("LibreHardwareMonitor init failed", ex);
        }
    }

    public HardwareSnapshot GetSnapshot()
    {
        var snapshot = new HardwareSnapshot();
        if (_computer == null || !IsAvailable) return snapshot;

        try
        {
            foreach (var hardware in _computer.Hardware)
            {
                hardware.Update();
                foreach (var subHardware in hardware.SubHardware)
                {
                    subHardware.Update();
                    CollectSensors(subHardware, hardware.HardwareType, snapshot);
                }
                CollectSensors(hardware, hardware.HardwareType, snapshot);
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error("Sensor snapshot failed", ex);
        }

        return snapshot;
    }

    private static void CollectSensors(IHardware hardware, HardwareType type, HardwareSnapshot snapshot)
    {
        foreach (var sensor in hardware.Sensors)
        {
            var reading = new SensorReading
            {
                Name = sensor.Name,
                HardwareName = hardware.Name,
                Value = (double?)sensor.Value,
                Min = (double?)sensor.Min,
                Max = (double?)sensor.Max,
            };

            (reading.Kind, reading.Unit) = sensor.SensorType switch
            {
                SensorType.Temperature => (SensorKind.Temperature, "°C"),
                SensorType.Clock      => (SensorKind.Clock, "MHz"),
                SensorType.Voltage    => (SensorKind.Voltage, "V"),
                SensorType.Fan        => (SensorKind.Fan, "RPM"),
                SensorType.Load       => (SensorKind.Load, "%"),
                SensorType.Power      => (SensorKind.Power, "W"),
                SensorType.Data       => (SensorKind.Data, "GB"),
                SensorType.SmallData  => (SensorKind.Data, "MB"), // LHM reports SmallData in MB (e.g. GPU memory)
                _                     => (SensorKind.Data, "")
            };

            // Bucket by hardware type
            if (type == HardwareType.Cpu)
            {
                switch (sensor.SensorType)
                {
                    case SensorType.Temperature: snapshot.CpuTemperatures.Add(reading); break;
                    case SensorType.Clock:       snapshot.CpuClocks.Add(reading); break;
                    case SensorType.Load:        snapshot.CpuLoads.Add(reading); break;
                    case SensorType.Power:       snapshot.CpuPowers.Add(reading); break;
                    case SensorType.Voltage:     snapshot.Voltages.Add(reading); break;
                }
            }
            else if (type == HardwareType.GpuNvidia || type == HardwareType.GpuAmd || type == HardwareType.GpuIntel)
            {
                switch (sensor.SensorType)
                {
                    case SensorType.Temperature: snapshot.GpuTemperatures.Add(reading); break;
                    case SensorType.Clock:       snapshot.GpuClocks.Add(reading); break;
                    case SensorType.Load:        snapshot.GpuLoads.Add(reading); break;
                    case SensorType.Power:       snapshot.GpuPowers.Add(reading); break;
                    // GPU memory is reported as SmallData (MB) on most GPUs, occasionally Data (GB).
                    case SensorType.Data:
                    case SensorType.SmallData:   snapshot.GpuMemory.Add(reading); break;
                    case SensorType.Fan:         snapshot.FanSpeeds.Add(reading); break;
                }
            }
            else if (type == HardwareType.Storage)
            {
                if (sensor.SensorType == SensorType.Temperature)
                    snapshot.StorageTemperatures.Add(reading);
            }
            else if (sensor.SensorType == SensorType.Fan)
            {
                snapshot.FanSpeeds.Add(reading);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _computer?.Close(); } catch { }
        _computer = null;
        GC.SuppressFinalize(this);
    }
}
