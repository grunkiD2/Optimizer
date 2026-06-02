using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class HardwareInfoService : IHardwareInfoService
{
    private readonly IWmiQueryService _wmi;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

    public HardwareInfoService(IWmiQueryService wmi)
    {
        _wmi = wmi;
    }

    public async Task<HardwareInfo> GetHardwareInfoAsync()
    {
        var cpuTask         = QueryCpuAsync();
        var gpusTask        = QueryGpusAsync();
        var memTask         = QueryMemoryAsync();
        var boardTask       = QueryMotherboardAsync();
        var storageTask     = QueryStorageAsync();
        var networkTask     = QueryNetworkAdaptersAsync();
        var displaysTask    = QueryDisplaysAsync();
        var osTask          = QueryOsAsync();

        await Task.WhenAll(cpuTask, gpusTask, memTask, boardTask, storageTask, networkTask, displaysTask, osTask);

        return new HardwareInfo
        {
            Cpu             = cpuTask.Result,
            Gpus            = gpusTask.Result,
            Memory          = memTask.Result,
            Motherboard     = boardTask.Result,
            Storage         = storageTask.Result,
            NetworkAdapters = networkTask.Result,
            Displays        = displaysTask.Result,
            Os              = osTask.Result,
        };
    }

    // ── CPU ───────────────────────────────────────────────────────────────

    private async Task<CpuInfo> QueryCpuAsync()
    {
        var cpus = await _wmi.QueryAsync(
            "SELECT * FROM Win32_Processor",
            obj => new CpuInfo
            {
                Name              = obj["Name"]?.ToString()?.Trim() ?? "",
                Manufacturer      = obj["Manufacturer"]?.ToString() ?? "",
                Cores             = Convert.ToInt32(obj["NumberOfCores"] ?? 0),
                LogicalProcessors = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? 0),
                MaxClockSpeedMHz  = Convert.ToInt32(obj["MaxClockSpeed"] ?? 0),
                L2CacheKB         = Convert.ToInt32(obj["L2CacheSize"] ?? 0),
                L3CacheKB         = Convert.ToInt32(obj["L3CacheSize"] ?? 0),
                Socket            = obj["SocketDesignation"]?.ToString() ?? "",
            },
            cacheTtl: Ttl);

        return cpus.FirstOrDefault() ?? new CpuInfo();
    }

    // ── GPU ───────────────────────────────────────────────────────────────

    private async Task<List<GpuInfo>> QueryGpusAsync()
    {
        var gpus = await _wmi.QueryAsync(
            "SELECT * FROM Win32_VideoController",
            obj => new GpuInfo
            {
                Name          = obj["Name"]?.ToString() ?? "",
                Manufacturer  = obj["AdapterCompatibility"]?.ToString() ?? "",
                VramBytes     = Convert.ToInt64(obj["AdapterRAM"] ?? 0L),
                DriverVersion = obj["DriverVersion"]?.ToString() ?? "",
            },
            cacheTtl: Ttl);

        return gpus.ToList();
    }

    // ── Memory ────────────────────────────────────────────────────────────

    private async Task<MemoryHardwareInfo> QueryMemoryAsync()
    {
        var info = new MemoryHardwareInfo();
        try
        {
            var modules = await _wmi.QueryAsync(
                "SELECT * FROM Win32_PhysicalMemory",
                obj => new
                {
                    Capacity              = Convert.ToInt64(obj["Capacity"] ?? 0L),
                    Speed                 = Convert.ToInt32(obj["Speed"] ?? 0),
                    ConfiguredClockSpeed  = Convert.ToInt32(obj["ConfiguredClockSpeed"] ?? 0),
                    ConfiguredVoltage     = Convert.ToInt32(obj["ConfiguredVoltage"] ?? 0),
                    Manufacturer          = obj["Manufacturer"]?.ToString()?.Trim() ?? "",
                    PartNumber            = obj["PartNumber"]?.ToString()?.Trim() ?? "",
                    FormFactor            = Convert.ToInt32(obj["FormFactor"] ?? 0),
                    BankLabel             = obj["BankLabel"]?.ToString()?.Trim() ?? "",
                    DeviceLocator         = obj["DeviceLocator"]?.ToString()?.Trim() ?? "",
                },
                cacheTtl: Ttl);

            long total = 0;
            int maxSpeed = 0;
            int maxConfiguredSpeed = 0;
            int maxConfiguredVoltage = 0;
            string formFactor = "";

            foreach (var m in modules)
            {
                total += m.Capacity;
                if (m.Speed > maxSpeed) maxSpeed = m.Speed;
                if (m.ConfiguredClockSpeed > maxConfiguredSpeed)
                    maxConfiguredSpeed = m.ConfiguredClockSpeed;
                if (m.ConfiguredVoltage > maxConfiguredVoltage)
                    maxConfiguredVoltage = m.ConfiguredVoltage;

                var part = $"{m.Manufacturer} {m.PartNumber}".Trim();
                if (!string.IsNullOrWhiteSpace(part))
                    info.ModuleParts.Add(part);

                formFactor = m.FormFactor switch { 8 => "DIMM", 12 => "SODIMM", _ => "Other" };

                info.Modules.Add(new MemoryModuleInfo
                {
                    BankLabel            = m.BankLabel,
                    DeviceLocator        = m.DeviceLocator,
                    CapacityBytes        = m.Capacity,
                    SpeedMhz             = m.Speed,
                    ConfiguredSpeedMhz   = m.ConfiguredClockSpeed,
                    ConfiguredVoltageMv  = m.ConfiguredVoltage,
                    Manufacturer         = m.Manufacturer,
                    PartNumber           = m.PartNumber,
                });
            }

            info.TotalBytes               = total;
            info.ModuleCount              = modules.Count;
            info.SpeedMHz                 = maxSpeed;
            info.FormFactor               = formFactor;
            info.ConfiguredClockSpeedMhz  = maxConfiguredSpeed;
            info.ConfiguredVoltageMv      = maxConfiguredVoltage;
        }
        catch { /* return partial */ }
        return info;
    }

    // ── Motherboard ───────────────────────────────────────────────────────

    private async Task<MotherboardInfo> QueryMotherboardAsync()
    {
        var info = new MotherboardInfo();
        try
        {
            var boards = await _wmi.QueryAsync(
                "SELECT * FROM Win32_BaseBoard",
                obj => (
                    Manufacturer: obj["Manufacturer"]?.ToString() ?? "",
                    Model: obj["Product"]?.ToString() ?? ""),
                cacheTtl: Ttl);

            var board = boards.FirstOrDefault();
            if (board != default)
            {
                info.Manufacturer = board.Manufacturer;
                info.Model        = board.Model;
            }
        }
        catch { /* ignore */ }

        try
        {
            var bioses = await _wmi.QueryAsync(
                "SELECT * FROM Win32_BIOS",
                obj => (
                    BiosVendor:  obj["Manufacturer"]?.ToString() ?? "",
                    BiosVersion: obj["SMBIOSBIOSVersion"]?.ToString() ?? "",
                    ReleaseDate: obj["ReleaseDate"]?.ToString() ?? ""),
                cacheTtl: Ttl);

            var bios = bioses.FirstOrDefault();
            if (bios != default)
            {
                info.BiosVendor  = bios.BiosVendor;
                info.BiosVersion = bios.BiosVersion;
                try
                {
                    var dateStr = bios.ReleaseDate;
                    if (dateStr.Length >= 8)
                    {
                        info.BiosDate = new DateTime(
                            int.Parse(dateStr[..4]),
                            int.Parse(dateStr.Substring(4, 2)),
                            int.Parse(dateStr.Substring(6, 2)));
                    }
                }
                catch { /* ignore malformed date */ }
            }
        }
        catch { /* ignore */ }
        return info;
    }

    // ── Storage ───────────────────────────────────────────────────────────

    private async Task<List<StorageInfo>> QueryStorageAsync()
    {
        var disks = await _wmi.QueryAsync(
            "SELECT * FROM Win32_DiskDrive",
            obj =>
            {
                var model = obj["Model"]?.ToString()?.Trim() ?? "";
                var iface = obj["InterfaceType"]?.ToString() ?? "";
                var size  = Convert.ToInt64(obj["Size"] ?? 0L);
                var media = model.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
                    ? "NVMe SSD"
                    : iface.Contains("USB", StringComparison.OrdinalIgnoreCase) ? "USB Drive" : iface;
                return new StorageInfo
                {
                    Model         = model,
                    InterfaceType = iface,
                    SizeBytes     = size,
                    MediaType     = media,
                    SerialNumber  = obj["SerialNumber"]?.ToString()?.Trim() ?? "",
                };
            },
            cacheTtl: Ttl);

        return disks.ToList();
    }

    // ── Network ───────────────────────────────────────────────────────────

    private async Task<List<NetworkAdapterInfo>> QueryNetworkAdaptersAsync()
    {
        var adapters = await _wmi.QueryAsync(
            "SELECT * FROM Win32_NetworkAdapter WHERE PhysicalAdapter=true",
            obj => new NetworkAdapterInfo
            {
                Name         = obj["Name"]?.ToString() ?? "",
                MacAddress   = obj["MACAddress"]?.ToString() ?? "",
                LinkSpeedBps = Convert.ToInt64(obj["Speed"] ?? 0L),
                Manufacturer = obj["Manufacturer"]?.ToString() ?? "",
            },
            cacheTtl: Ttl);

        return adapters
            .Where(a => !a.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // ── Displays ──────────────────────────────────────────────────────────

    private async Task<List<DisplayInfo>> QueryDisplaysAsync()
    {
        var monitors = await _wmi.QueryAsync(
            "SELECT * FROM Win32_DesktopMonitor",
            obj => new DisplayInfo
            {
                Name    = obj["Name"]?.ToString() ?? "",
                WidthPx = Convert.ToInt32(obj["ScreenWidth"] ?? 0),
                HeightPx = Convert.ToInt32(obj["ScreenHeight"] ?? 0),
            },
            cacheTtl: Ttl);

        var list = monitors
            .Where(d => !string.IsNullOrWhiteSpace(d.Name) || d.WidthPx > 0)
            .ToList();

        // If WMI gave us nothing, fall back to the primary screen info
        if (list.Count == 0)
        {
            list.Add(new DisplayInfo
            {
                Name     = "Primary Display",
                WidthPx  = (int)Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea.Width,
                HeightPx = (int)Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea.Height,
            });
        }

        return list;
    }

    // ── OS ────────────────────────────────────────────────────────────────

    private async Task<OsInfo> QueryOsAsync()
    {
        var info = new OsInfo();
        try
        {
            var osList = await _wmi.QueryAsync(
                "SELECT * FROM Win32_OperatingSystem",
                obj => (
                    Name:         obj["Caption"]?.ToString() ?? "",
                    Version:      obj["Version"]?.ToString() ?? "",
                    Build:        obj["BuildNumber"]?.ToString() ?? "",
                    Architecture: obj["OSArchitecture"]?.ToString() ?? "",
                    InstallDate:  obj["InstallDate"]?.ToString() ?? ""),
                cacheTtl: Ttl);

            var os = osList.FirstOrDefault();
            if (os != default)
            {
                info.Name         = os.Name;
                info.Version      = os.Version;
                info.Build        = os.Build;
                info.Architecture = os.Architecture;
                try
                {
                    var dateStr = os.InstallDate;
                    if (dateStr.Length >= 8)
                    {
                        info.InstallDate = new DateTime(
                            int.Parse(dateStr[..4]),
                            int.Parse(dateStr.Substring(4, 2)),
                            int.Parse(dateStr.Substring(6, 2)));
                    }
                }
                catch { /* ignore malformed date */ }
            }
        }
        catch { /* return partial */ }

        // UEFI detection
        info.IsUefi = Environment.OSVersion.Version.Major >= 10;

        // Secure Boot detection via registry
        try
        {
            var sb = Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\SecureBoot\State",
                "UEFISecureBootEnabled", 0);
            info.IsSecureBoot = Convert.ToInt32(sb ?? 0) == 1;
        }
        catch { /* registry key absent on non-UEFI systems */ }

        return info;
    }
}
