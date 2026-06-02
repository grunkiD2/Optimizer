using Optimizer.WinUI.Helpers;

namespace Optimizer.WinUI.Models;

public class HardwareInfo
{
    public CpuInfo Cpu { get; set; } = new();
    public List<GpuInfo> Gpus { get; set; } = [];
    public MemoryHardwareInfo Memory { get; set; } = new();
    public MotherboardInfo Motherboard { get; set; } = new();
    public List<StorageInfo> Storage { get; set; } = [];
    public List<NetworkAdapterInfo> NetworkAdapters { get; set; } = [];
    public List<DisplayInfo> Displays { get; set; } = [];
    public OsInfo Os { get; set; } = new();
}

public class CpuInfo
{
    public string Name { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public int Cores { get; set; }
    public int LogicalProcessors { get; set; }
    public int MaxClockSpeedMHz { get; set; }
    public int L2CacheKB { get; set; }
    public int L3CacheKB { get; set; }
    public string Socket { get; set; } = "";
}

public class GpuInfo
{
    public string Name { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public long VramBytes { get; set; }
    public string DriverVersion { get; set; } = "";

    // Display helpers
    public string VramText => VramBytes > 0 ? ByteFormatter.Format(VramBytes) : "—";
}

public class MemoryHardwareInfo
{
    public long TotalBytes { get; set; }
    public int ModuleCount { get; set; }
    public int SpeedMHz { get; set; }
    public string FormFactor { get; set; } = "";
    public List<string> ModuleParts { get; set; } = [];

    // Per-DIMM detail (D4b)
    public int ConfiguredClockSpeedMhz { get; set; }
    public int ConfiguredVoltageMv { get; set; }
    public List<MemoryModuleInfo> Modules { get; set; } = [];
}

/// <summary>
/// Per-DIMM slot information read from Win32_PhysicalMemory.
/// Full primary timings (CL-tRCD-tRP-tRAS) are NOT available via WMI
/// and require raw SMBIOS SPD reads — see the Hardware page note.
/// </summary>
public class MemoryModuleInfo
{
    public string BankLabel { get; set; } = "";
    public string DeviceLocator { get; set; } = "";
    public long CapacityBytes { get; set; }
    public int SpeedMhz { get; set; }
    public int ConfiguredSpeedMhz { get; set; }
    public int ConfiguredVoltageMv { get; set; }
    public string Manufacturer { get; set; } = "";
    public string PartNumber { get; set; } = "";

    // Display helpers
    public string CapacityText => CapacityBytes > 0 ? ByteFormatter.Format(CapacityBytes) : "—";
    public string SlotText => string.IsNullOrEmpty(DeviceLocator) ? BankLabel : DeviceLocator;
    public string SpeedText => ConfiguredSpeedMhz > 0
        ? $"{ConfiguredSpeedMhz} MHz (rated {SpeedMhz} MHz)"
        : SpeedMhz > 0 ? $"{SpeedMhz} MHz" : "—";
    public string VoltageText => ConfiguredVoltageMv > 0
        ? $"{(ConfiguredVoltageMv / 1000.0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} V"
        : "—";
}

public class MotherboardInfo
{
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string BiosVendor { get; set; } = "";
    public string BiosVersion { get; set; } = "";
    public DateTime BiosDate { get; set; }
}

public class StorageInfo
{
    public string Model { get; set; } = "";
    public string InterfaceType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string MediaType { get; set; } = "";
    public string SerialNumber { get; set; } = "";

    // Display helpers
    public string SizeText => SizeBytes > 0 ? ByteFormatter.Format(SizeBytes) : "—";
}

public class NetworkAdapterInfo
{
    public string Name { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public long LinkSpeedBps { get; set; }
    public string Manufacturer { get; set; } = "";

    // Display helpers
    public string LinkSpeedText => LinkSpeedBps > 0 ? $"{LinkSpeedBps / 1_000_000} Mbps" : "—";
}

public class DisplayInfo
{
    public string Name { get; set; } = "";
    public int WidthPx { get; set; }
    public int HeightPx { get; set; }
    public int RefreshRateHz { get; set; }

    // Display helpers
    public string ResolutionText => WidthPx > 0 && HeightPx > 0
        ? (RefreshRateHz > 0 ? $"{WidthPx}×{HeightPx} @ {RefreshRateHz} Hz" : $"{WidthPx}×{HeightPx}")
        : "—";
}

public class OsInfo
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Build { get; set; } = "";
    public DateTime InstallDate { get; set; }
    public string Architecture { get; set; } = "";
    public bool IsUefi { get; set; }
    public bool IsSecureBoot { get; set; }
}
