using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class VendorTool
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public bool IsInstalled => !string.IsNullOrEmpty(ExecutablePath) && File.Exists(ExecutablePath);
}

public class GpuClockInfo
{
    public string Name { get; set; } = "";
    public int? CurrentCoreMhz { get; set; }
    public int? CurrentMemoryMhz { get; set; }
    public int? TemperatureCelsius { get; set; }
    public int? PowerWatts { get; set; }
    public int? FanRpm { get; set; }
}

public class RamInfo
{
    public int FrequencyMhz { get; set; }
    public string Timings { get; set; } = "";   // "CL-tRCD-tRP-tRAS"
    public string Voltage { get; set; } = "";   // unknown without BIOS access
}

public interface ITuningService
{
    Task<CpuTuning> GetCurrentCpuTuningAsync();
    Task<bool> ApplyCpuTuningAsync(CpuTuning tuning);
    Task<IReadOnlyList<GpuClockInfo>> GetGpuClocksAsync();
    Task<RamInfo> GetRamInfoAsync();
    Task<bool> ApplyPresetAsync(TuningPreset preset);
    IReadOnlyList<TuningPreset> GetPresets();
    Task<bool> RevertToDefaultsAsync();
    Task<IReadOnlyList<VendorTool>> DetectGpuToolsAsync();
    Task<bool> LaunchToolAsync(VendorTool tool);
}
