using System.Management;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class HardwareInfoService : IHardwareInfoService
{
    public Task<HardwareInfo> GetHardwareInfoAsync()
    {
        return Task.Run(() => new HardwareInfo
        {
            Cpu = QueryCpu(),
            Gpus = QueryGpus(),
            Memory = QueryMemory(),
            Motherboard = QueryMotherboard(),
            Storage = QueryStorage(),
            NetworkAdapters = QueryNetworkAdapters(),
            Displays = QueryDisplays(),
            Os = QueryOs(),
        });
    }

    // ── CPU ───────────────────────────────────────────────────────────────

    private static CpuInfo QueryCpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            var obj = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
            if (obj == null) return new CpuInfo();
            return new CpuInfo
            {
                Name = obj["Name"]?.ToString()?.Trim() ?? "",
                Manufacturer = obj["Manufacturer"]?.ToString() ?? "",
                Cores = Convert.ToInt32(obj["NumberOfCores"] ?? 0),
                LogicalProcessors = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? 0),
                MaxClockSpeedMHz = Convert.ToInt32(obj["MaxClockSpeed"] ?? 0),
                L2CacheKB = Convert.ToInt32(obj["L2CacheSize"] ?? 0),
                L3CacheKB = Convert.ToInt32(obj["L3CacheSize"] ?? 0),
                Socket = obj["SocketDesignation"]?.ToString() ?? "",
            };
        }
        catch { return new CpuInfo(); }
    }

    // ── GPU ───────────────────────────────────────────────────────────────

    private static List<GpuInfo> QueryGpus()
    {
        var list = new List<GpuInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                list.Add(new GpuInfo
                {
                    Name = obj["Name"]?.ToString() ?? "",
                    Manufacturer = obj["AdapterCompatibility"]?.ToString() ?? "",
                    VramBytes = Convert.ToInt64(obj["AdapterRAM"] ?? 0L),
                    DriverVersion = obj["DriverVersion"]?.ToString() ?? "",
                });
            }
        }
        catch { /* return whatever we have */ }
        return list;
    }

    // ── Memory ────────────────────────────────────────────────────────────

    private static MemoryHardwareInfo QueryMemory()
    {
        var info = new MemoryHardwareInfo();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory");
            long total = 0;
            int count = 0;
            int maxSpeed = 0;
            string formFactor = "";
            foreach (ManagementObject obj in searcher.Get())
            {
                total += Convert.ToInt64(obj["Capacity"] ?? 0L);
                count++;
                var speed = Convert.ToInt32(obj["Speed"] ?? 0);
                if (speed > maxSpeed) maxSpeed = speed;

                var manufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "";
                var partNumber = obj["PartNumber"]?.ToString()?.Trim() ?? "";
                var part = $"{manufacturer} {partNumber}".Trim();
                if (!string.IsNullOrWhiteSpace(part))
                    info.ModuleParts.Add(part);

                var ff = Convert.ToInt32(obj["FormFactor"] ?? 0);
                formFactor = ff switch { 8 => "DIMM", 12 => "SODIMM", _ => "Other" };
            }
            info.TotalBytes = total;
            info.ModuleCount = count;
            info.SpeedMHz = maxSpeed;
            info.FormFactor = formFactor;
        }
        catch { /* return partial */ }
        return info;
    }

    // ── Motherboard ───────────────────────────────────────────────────────

    private static MotherboardInfo QueryMotherboard()
    {
        var info = new MotherboardInfo();
        try
        {
            using var boardSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard");
            var board = boardSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
            if (board != null)
            {
                info.Manufacturer = board["Manufacturer"]?.ToString() ?? "";
                info.Model = board["Product"]?.ToString() ?? "";
            }
        }
        catch { /* ignore */ }

        try
        {
            using var biosSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");
            var bios = biosSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
            if (bios != null)
            {
                info.BiosVendor = bios["Manufacturer"]?.ToString() ?? "";
                info.BiosVersion = bios["SMBIOSBIOSVersion"]?.ToString() ?? "";
                try
                {
                    var dateStr = bios["ReleaseDate"]?.ToString() ?? "";
                    if (dateStr.Length >= 8)
                    {
                        info.BiosDate = new DateTime(
                            int.Parse(dateStr.Substring(0, 4)),
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

    private static List<StorageInfo> QueryStorage()
    {
        var list = new List<StorageInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            foreach (ManagementObject obj in searcher.Get())
            {
                var model = obj["Model"]?.ToString()?.Trim() ?? "";
                var iface = obj["InterfaceType"]?.ToString() ?? "";
                var size = Convert.ToInt64(obj["Size"] ?? 0L);
                var media = model.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
                    ? "NVMe SSD"
                    : iface.Contains("USB", StringComparison.OrdinalIgnoreCase) ? "USB Drive"
                    : iface;
                list.Add(new StorageInfo
                {
                    Model = model,
                    InterfaceType = iface,
                    SizeBytes = size,
                    MediaType = media,
                    SerialNumber = obj["SerialNumber"]?.ToString()?.Trim() ?? "",
                });
            }
        }
        catch { /* return partial */ }
        return list;
    }

    // ── Network ───────────────────────────────────────────────────────────

    private static List<NetworkAdapterInfo> QueryNetworkAdapters()
    {
        var list = new List<NetworkAdapterInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapter WHERE PhysicalAdapter=true");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";
                if (name.Contains("Loopback", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new NetworkAdapterInfo
                {
                    Name = name,
                    MacAddress = obj["MACAddress"]?.ToString() ?? "",
                    LinkSpeedBps = Convert.ToInt64(obj["Speed"] ?? 0L),
                    Manufacturer = obj["Manufacturer"]?.ToString() ?? "",
                });
            }
        }
        catch { /* return partial */ }
        return list;
    }

    // ── Displays ──────────────────────────────────────────────────────────

    private static List<DisplayInfo> QueryDisplays()
    {
        var list = new List<DisplayInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DesktopMonitor");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "";
                var w = Convert.ToInt32(obj["ScreenWidth"] ?? 0);
                var h = Convert.ToInt32(obj["ScreenHeight"] ?? 0);
                if (string.IsNullOrWhiteSpace(name) && w == 0) continue;
                list.Add(new DisplayInfo
                {
                    Name = name,
                    WidthPx = w,
                    HeightPx = h,
                });
            }
        }
        catch { /* return partial */ }

        // If WMI gave us nothing, fall back to the primary screen info via SystemParameters
        if (list.Count == 0)
        {
            list.Add(new DisplayInfo
            {
                Name = "Primary Display",
                WidthPx = (int)Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea.Width,
                HeightPx = (int)Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea.Height,
            });
        }

        return list;
    }

    // ── OS ────────────────────────────────────────────────────────────────

    private static OsInfo QueryOs()
    {
        var info = new OsInfo();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
            var obj = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
            if (obj != null)
            {
                info.Name = obj["Caption"]?.ToString() ?? "";
                info.Version = obj["Version"]?.ToString() ?? "";
                info.Build = obj["BuildNumber"]?.ToString() ?? "";
                info.Architecture = obj["OSArchitecture"]?.ToString() ?? "";
                try
                {
                    var dateStr = obj["InstallDate"]?.ToString() ?? "";
                    if (dateStr.Length >= 8)
                    {
                        info.InstallDate = new DateTime(
                            int.Parse(dateStr.Substring(0, 4)),
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
