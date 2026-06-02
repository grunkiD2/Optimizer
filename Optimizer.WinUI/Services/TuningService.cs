using System.Diagnostics;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class TuningService : ITuningService
{
    private readonly IWmiQueryService _wmi;
    private static readonly TimeSpan TuningTtl = TimeSpan.FromMinutes(1);

    public TuningService(IWmiQueryService wmi)
    {
        _wmi = wmi;
    }

    // Processor power management subgroup GUID
    private const string ProcessorSubGroup = "54533251-82be-4824-96c1-47b60b740d00";

    // Individual setting GUIDs
    private const string MinStateGuid    = "893dee8e-2bef-41e0-89c6-b55d0929964c";
    private const string MaxStateGuid    = "bc5038f7-23e0-4960-96da-33abaf5935ec";
    private const string BoostModeGuid   = "be337238-0d82-4146-a960-4f3749d470c7";
    private const string BoostPolicyGuid = "45bcc044-d885-43e2-8605-ee0ec6e96b59";

    // ── Read current CPU tuning ───────────────────────────────────────────────

    public async Task<CpuTuning> GetCurrentCpuTuningAsync()
    {
        var output = await RunPowerCfgAsync($"/query SCHEME_CURRENT {ProcessorSubGroup}");
        if (output == null) return new CpuTuning();

        return new CpuTuning
        {
            MinProcessorState = ParsePowerCfgValue(output, MinStateGuid,    "AC") ?? 5,
            MaxProcessorState = ParsePowerCfgValue(output, MaxStateGuid,    "AC") ?? 100,
            BoostMode         = (BoostMode)(ParsePowerCfgValue(output, BoostModeGuid,   "AC") ?? 1),
            BoostPolicy       = ParsePowerCfgValue(output, BoostPolicyGuid, "AC") ?? 60,
        };
    }

    // ── Apply CPU tuning ──────────────────────────────────────────────────────

    public async Task<bool> ApplyCpuTuningAsync(CpuTuning tuning)
    {
        // Clamp values to safe ranges
        tuning.MinProcessorState = Math.Clamp(tuning.MinProcessorState, 0, 100);
        tuning.MaxProcessorState = Math.Clamp(tuning.MaxProcessorState, 20, 100);
        tuning.BoostPolicy       = Math.Clamp(tuning.BoostPolicy, 0, 100);

        var commands = new[]
        {
            $"/setacvalueindex SCHEME_CURRENT {ProcessorSubGroup} {MinStateGuid} {tuning.MinProcessorState}",
            $"/setdcvalueindex SCHEME_CURRENT {ProcessorSubGroup} {MinStateGuid} {tuning.MinProcessorState}",
            $"/setacvalueindex SCHEME_CURRENT {ProcessorSubGroup} {MaxStateGuid} {tuning.MaxProcessorState}",
            $"/setdcvalueindex SCHEME_CURRENT {ProcessorSubGroup} {MaxStateGuid} {tuning.MaxProcessorState}",
            $"/setacvalueindex SCHEME_CURRENT {ProcessorSubGroup} {BoostModeGuid} {(int)tuning.BoostMode}",
            $"/setacvalueindex SCHEME_CURRENT {ProcessorSubGroup} {BoostPolicyGuid} {tuning.BoostPolicy}",
            "/setactive SCHEME_CURRENT"   // commit changes
        };

        foreach (var cmd in commands)
        {
            if (await RunPowerCfgAsync(cmd) == null) return false;
        }
        return true;
    }

    // ── GPU info (WMI read-only; vendor clocks require NVAPI/ADL) ────────────

    public async Task<IReadOnlyList<GpuClockInfo>> GetGpuClocksAsync()
    {
        try
        {
            var gpus = await _wmi.QueryAsync(
                "SELECT Name, CurrentRefreshRate FROM Win32_VideoController",
                obj => new GpuClockInfo
                {
                    Name             = obj["Name"]?.ToString() ?? "Unknown GPU",
                    // Win32_VideoController does not expose core/memory clock — requires NVAPI/ADL
                    CurrentCoreMhz   = null,
                    CurrentMemoryMhz = null,
                },
                cacheTtl: TuningTtl);
            return gpus;
        }
        catch (Exception ex)
        {
            EngineLog.Error("Failed to query GPU info via WMI", ex);
            return Array.Empty<GpuClockInfo>();
        }
    }

    // ── RAM info (WMI read-only; timings/voltage require BIOS) ───────────────

    public async Task<RamInfo> GetRamInfoAsync()
    {
        try
        {
            var modules = await _wmi.QueryAsync(
                "SELECT Speed FROM Win32_PhysicalMemory",
                obj => Convert.ToInt32(obj["Speed"] ?? 0),
                cacheTtl: TuningTtl);

            var speed = modules.FirstOrDefault();
            if (speed > 0)
            {
                return new RamInfo
                {
                    FrequencyMhz = speed,
                    Timings      = "Unknown — requires BIOS access",
                    Voltage      = "Unknown — requires BIOS access"
                };
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error("Failed to query RAM info via WMI", ex);
        }
        return new RamInfo();
    }

    // ── Presets ───────────────────────────────────────────────────────────────

    public IReadOnlyList<TuningPreset> GetPresets() =>
    [
        // ── Universal presets (Any vendor) ──────────────────────────────────
        new TuningPreset
        {
            Id          = "stock",
            Name        = "Stock",
            Description = "Windows default — balanced performance and power.",
            Risk        = "Low",
            CpuVendor   = "Any",
            Cpu         = new CpuTuning { MinProcessorState = 5, MaxProcessorState = 100, BoostMode = BoostMode.Enabled, BoostPolicy = 60 }
        },
        new TuningPreset
        {
            Id                   = "quiet",
            Name                 = "Quiet / Cool",
            Description          = "Lower max state, boost disabled — reduces heat and fan noise at the cost of performance.",
            Risk                 = "Low",
            CpuVendor            = "Any",
            RecommendedForLaptop = true,
            Cpu                  = new CpuTuning { MinProcessorState = 5, MaxProcessorState = 80, BoostMode = BoostMode.Disabled, BoostPolicy = 40 }
        },
        new TuningPreset
        {
            Id          = "mild",
            Name        = "Mild Tune",
            Description = "Slightly more aggressive boost — better responsiveness with minimal heat increase.",
            Risk        = "Low",
            CpuVendor   = "Any",
            Cpu         = new CpuTuning { MinProcessorState = 10, MaxProcessorState = 100, BoostMode = BoostMode.Aggressive, BoostPolicy = 80 }
        },
        new TuningPreset
        {
            Id          = "moderate",
            Name        = "Moderate Tune",
            Description = "Higher minimum state + aggressive boost — more sustained performance, more heat.",
            Risk        = "Medium",
            CpuVendor   = "Any",
            Cpu         = new CpuTuning { MinProcessorState = 25, MaxProcessorState = 100, BoostMode = BoostMode.Aggressive, BoostPolicy = 100 }
        },

        // ── Intel-specific presets ───────────────────────────────────────────
        new TuningPreset
        {
            Id             = "intel-max-perf",
            Name           = "Intel: Maximum Performance",
            Description    = "100% min state, all-core max boost, unlimited PL1/PL2 hint — maximum heat and power draw.",
            Risk           = "High",
            CpuVendor      = "Intel",
            PowerLimitWatts = null,   // uncapped
            Cpu            = new CpuTuning { MinProcessorState = 100, MaxProcessorState = 100, BoostMode = BoostMode.AggressiveAtGuaranteed, BoostPolicy = 100 }
        },
        new TuningPreset
        {
            Id          = "intel-avx",
            Name        = "Intel: AVX-Heavy Workload",
            Description = "Aggressive boost kept during AVX vector math — optimal for ML/rendering loads.",
            Risk        = "Medium",
            CpuVendor   = "Intel",
            Cpu         = new CpuTuning { MinProcessorState = 50, MaxProcessorState = 100, BoostMode = BoostMode.EfficientAggressive, BoostPolicy = 100 }
        },

        // ── AMD-specific presets ─────────────────────────────────────────────
        new TuningPreset
        {
            Id          = "amd-pbo",
            Name        = "AMD: PBO Aggressive",
            Description = "Maximum boost frequency + Aggressive boost mode — unlocks peak single-core performance.",
            Risk        = "High",
            CpuVendor   = "AMD",
            Cpu         = new CpuTuning { MinProcessorState = 10, MaxProcessorState = 100, BoostMode = BoostMode.Aggressive, BoostPolicy = 100 }
        },
        new TuningPreset
        {
            Id                   = "amd-eco",
            Name                 = "AMD: ECO Mode",
            Description          = "Lower max state with boost disabled — reduces TDP for quieter, cooler operation (laptop-friendly).",
            Risk                 = "Low",
            CpuVendor            = "AMD",
            RecommendedForLaptop = true,
            Cpu                  = new CpuTuning { MinProcessorState = 5, MaxProcessorState = 75, BoostMode = BoostMode.Disabled, BoostPolicy = 30 }
        },
    ];

    public async Task<bool> ApplyPresetAsync(TuningPreset preset)
        => await ApplyCpuTuningAsync(preset.Cpu);

    public async Task<bool> RevertToDefaultsAsync()
        => await ApplyPresetAsync(GetPresets().First(p => p.Id == "stock"));

    // ── GPU vendor tool detection ─────────────────────────────────────────────

    public async Task<IReadOnlyList<VendorTool>> DetectGpuToolsAsync()
    {
        var tools = new List<VendorTool>();

        var pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        // MSI Afterburner
        var afterburnerPaths = new[]
        {
            Path.Combine(pf86, "MSI Afterburner", "MSIAfterburner.exe"),
            Path.Combine(pf64, "MSI Afterburner", "MSIAfterburner.exe"),
        };
        tools.Add(new VendorTool
        {
            Name = "MSI Afterburner",
            Description = "Industry-standard GPU overclocking and monitoring",
            ExecutablePath = afterburnerPaths.FirstOrDefault(File.Exists) ?? "",
            DownloadUrl = "https://www.msi.com/Landing/afterburner/graphics-cards"
        });

        // EVGA Precision X1
        var precisionPaths = new[]
        {
            Path.Combine(pf64, "EVGA", "Precision X1", "PrecisionX1.exe"),
            Path.Combine(pf86, "EVGA", "Precision X1", "PrecisionX1.exe"),
        };
        tools.Add(new VendorTool
        {
            Name = "EVGA Precision X1",
            Description = "NVIDIA GPU overclocking with RGB control",
            ExecutablePath = precisionPaths.FirstOrDefault(File.Exists) ?? "",
            DownloadUrl = "https://www.evga.com/precisionx1/"
        });

        // NVIDIA App
        var nvAppPaths = new[]
        {
            Path.Combine(pf64, "NVIDIA Corporation", "NVIDIA App", "CEF", "NVIDIA app.exe"),
            Path.Combine(pf86, "NVIDIA Corporation", "NVIDIA App", "CEF", "NVIDIA app.exe"),
        };
        tools.Add(new VendorTool
        {
            Name = "NVIDIA App",
            Description = "Official NVIDIA software with performance tuning",
            ExecutablePath = nvAppPaths.FirstOrDefault(File.Exists) ?? "",
            DownloadUrl = "https://www.nvidia.com/en-us/software/nvidia-app/"
        });

        // AMD Software: Adrenalin Edition
        var amdPaths = new[]
        {
            Path.Combine(pf64, "AMD", "CNext", "CNext", "RadeonSoftware.exe"),
            Path.Combine(pf86, "AMD", "CNext", "CNext", "RadeonSoftware.exe"),
        };
        tools.Add(new VendorTool
        {
            Name = "AMD Software: Adrenalin Edition",
            Description = "Official AMD GPU control with tuning",
            ExecutablePath = amdPaths.FirstOrDefault(File.Exists) ?? "",
            DownloadUrl = "https://www.amd.com/en/support"
        });

        // Intel Arc Control
        var intelPaths = new[]
        {
            Path.Combine(pf64, "Intel", "Arc Control", "ArcControl.exe"),
        };
        tools.Add(new VendorTool
        {
            Name = "Intel Arc Control",
            Description = "Intel GPU control panel and tuning",
            ExecutablePath = intelPaths.FirstOrDefault(File.Exists) ?? "",
            DownloadUrl = "https://www.intel.com/content/www/us/en/download-center/home.html"
        });

        return await Task.FromResult<IReadOnlyList<VendorTool>>(tools);
    }

    public Task<bool> LaunchToolAsync(VendorTool tool)
    {
        try
        {
            if (tool.IsInstalled)
            {
                Process.Start(new ProcessStartInfo(tool.ExecutablePath) { UseShellExecute = true });
            }
            else if (!string.IsNullOrEmpty(tool.DownloadUrl))
            {
                Process.Start(new ProcessStartInfo(tool.DownloadUrl) { UseShellExecute = true });
            }
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            EngineLog.Error("LaunchToolAsync failed", ex);
            return Task.FromResult(false);
        }
    }

    // ── Batch 35: CPU vendor detection ───────────────────────────────────────

    public async Task<string> GetCpuVendorAsync()
    {
        try
        {
            var vendors = await _wmi.QueryAsync(
                "SELECT Manufacturer FROM Win32_Processor",
                obj => obj["Manufacturer"]?.ToString() ?? "",
                cacheTtl: TuningTtl);
            return vendors.FirstOrDefault() ?? "";
        }
        catch (Exception ex)
        {
            EngineLog.Error("GetCpuVendorAsync failed", ex);
            return "";
        }
    }

    // ── Batch 35: Power limits (WMI best-effort) ──────────────────────────────

    public async Task<(int? Pl1Watts, int? Pl2Watts)> GetPowerLimitsAsync()
    {
        // Win32_Processor does not expose PL1/PL2 directly; the values live in
        // MSR 0x610 (RAPL) which requires a kernel driver or BIOS WMI OEM namespace.
        // We return null/null to indicate "not available via standard WMI" so the
        // UI can show an appropriate placeholder.
        return await Task.FromResult<(int?, int?)>((null, null));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<string?> RunPowerCfgAsync(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg.exe", args)
            {
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0 ? stdout : null;
        }
        catch (Exception ex)
        {
            EngineLog.Error($"powercfg {args} failed", ex);
            return null;
        }
    }

    private static int? ParsePowerCfgValue(string output, string settingGuid, string acOrDc)
    {
        // powercfg /query output structure (relevant excerpt):
        //   Power Setting GUID: 893dee8e-...  (Minimum processor state)
        //     Current AC Power Setting Index: 0x00000005
        //     Current DC Power Setting Index: 0x00000005

        var idx = output.IndexOf(settingGuid, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var section = output.Substring(idx, Math.Min(800, output.Length - idx));
        var label   = acOrDc == "AC"
            ? "Current AC Power Setting Index:"
            : "Current DC Power Setting Index:";

        var ix = section.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (ix < 0) return null;

        var tail  = section.Substring(ix + label.Length, Math.Min(40, section.Length - ix - label.Length));
        var match = System.Text.RegularExpressions.Regex.Match(tail, @"0x([0-9a-fA-F]+)");
        if (match.Success &&
            int.TryParse(match.Groups[1].Value,
                System.Globalization.NumberStyles.HexNumber, null, out var v))
            return v;

        return null;
    }
}
