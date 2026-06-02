using System.Diagnostics;
using System.Management;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class TuningService : ITuningService
{
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
        return await Task.Run(() =>
        {
            var list = new List<GpuClockInfo>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, CurrentRefreshRate FROM Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                    list.Add(new GpuClockInfo
                    {
                        Name           = obj["Name"]?.ToString() ?? "Unknown GPU",
                        // Win32_VideoController does not expose core/memory clock — requires NVAPI/ADL
                        CurrentCoreMhz = null,
                        CurrentMemoryMhz = null,
                    });
                }
            }
            catch (Exception ex)
            {
                EngineLog.Error("Failed to query GPU info via WMI", ex);
            }
            return (IReadOnlyList<GpuClockInfo>)list;
        });
    }

    // ── RAM info (WMI read-only; timings/voltage require BIOS) ───────────────

    public async Task<RamInfo> GetRamInfoAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Speed FROM Win32_PhysicalMemory");
                var obj = searcher.Get().Cast<ManagementObject>().FirstOrDefault();
                if (obj != null)
                {
                    return new RamInfo
                    {
                        FrequencyMhz = Convert.ToInt32(obj["Speed"] ?? 0),
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
        });
    }

    // ── Presets ───────────────────────────────────────────────────────────────

    public IReadOnlyList<TuningPreset> GetPresets() =>
    [
        new TuningPreset
        {
            Id          = "stock",
            Name        = "Stock",
            Description = "Windows default — balanced performance and power.",
            Risk        = "Low",
            Cpu         = new CpuTuning { MinProcessorState = 5, MaxProcessorState = 100, BoostMode = BoostMode.Enabled, BoostPolicy = 60 }
        },
        new TuningPreset
        {
            Id          = "mild",
            Name        = "Mild Tune",
            Description = "Slightly more aggressive boost — better responsiveness with minimal heat increase.",
            Risk        = "Low",
            Cpu         = new CpuTuning { MinProcessorState = 10, MaxProcessorState = 100, BoostMode = BoostMode.Aggressive, BoostPolicy = 80 }
        },
        new TuningPreset
        {
            Id          = "moderate",
            Name        = "Moderate Tune",
            Description = "Higher minimum state + aggressive boost — more sustained performance, more heat.",
            Risk        = "Medium",
            Cpu         = new CpuTuning { MinProcessorState = 25, MaxProcessorState = 100, BoostMode = BoostMode.Aggressive, BoostPolicy = 100 }
        },
        new TuningPreset
        {
            Id          = "max-perf",
            Name        = "Maximum Performance",
            Description = "100% min state — no power saving, maximum heat and power draw.",
            Risk        = "High",
            Cpu         = new CpuTuning { MinProcessorState = 100, MaxProcessorState = 100, BoostMode = BoostMode.AggressiveAtGuaranteed, BoostPolicy = 100 }
        },
        new TuningPreset
        {
            Id          = "quiet",
            Name        = "Quiet / Cool",
            Description = "Lower max state, boost disabled — reduces heat and fan noise at the cost of performance.",
            Risk        = "Low",
            Cpu         = new CpuTuning { MinProcessorState = 5, MaxProcessorState = 80, BoostMode = BoostMode.Disabled, BoostPolicy = 40 }
        },
    ];

    public async Task<bool> ApplyPresetAsync(TuningPreset preset)
        => await ApplyCpuTuningAsync(preset.Cpu);

    public async Task<bool> RevertToDefaultsAsync()
        => await ApplyPresetAsync(GetPresets().First(p => p.Id == "stock"));

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
