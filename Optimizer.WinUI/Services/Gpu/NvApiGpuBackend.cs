using System.Runtime.InteropServices;
using Optimizer.WinUI.Models.Gpu;

namespace Optimizer.WinUI.Services.Gpu;

/// <summary>
/// NVIDIA GPU control backend via NVAPI (nvapi64.dll).
///
/// SAFETY NOTES:
/// - All native interactions are wrapped in try/catch. If the DLL cannot load,
///   init fails, or no NVIDIA GPU is found, IsAvailable=false and all operations
///   are safe no-ops.
/// - OC WRITE (TryApply) is intentionally NOT implemented in this build.
///   NVAPI's clock-offset and power-limit write functions require exact struct
///   versions that vary by driver generation. Issuing wrong structs can
///   silently misconfigure or destabilize a GPU. The write path is therefore
///   stubbed with a clear error message rather than guessing at signatures.
///   Telemetry (read-only) works through LibreHardwareMonitor, not NVAPI.
/// - Label as Experimental: see IGpuControlService.OcWriteAvailable.
/// </summary>
public sealed class NvApiGpuBackend : IGpuControlBackend, IDisposable
{
    // ── NVAPI DLL entry point ─────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    // NVAPI is accessed via a single export that returns function pointers by ID.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr NvAPI_QueryInterface_t(uint id);

    // NvAPI_Initialize function pointer delegate
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_Initialize_t();

    // NvAPI_EnumPhysicalGPUs: fills array of up to 64 GPU handles, returns count
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_EnumPhysicalGPUs_t(
        [MarshalAs(UnmanagedType.LPArray, SizeConst = 64)] IntPtr[] gpuHandles,
        out int gpuCount);

    // NvAPI_GPU_GetFullName: returns the GPU name string (max 64 chars)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_GPU_GetFullName_t(
        IntPtr hPhysicalGPU,
        [MarshalAs(UnmanagedType.LPArray, SizeConst = 64)] byte[] name);

    // NVAPI function IDs (documented in NVAPI SDK)
    private const uint ID_NvAPI_Initialize        = 0x0150E828;
    private const uint ID_NvAPI_EnumPhysicalGPUs  = 0xE5AC921F;
    private const uint ID_NvAPI_GPU_GetFullName    = 0xCEEE8E9F;

    // ── State ─────────────────────────────────────────────────────────────────

    private IntPtr _hModule = IntPtr.Zero;
    private bool _disposed;

    public GpuVendor Vendor => GpuVendor.Nvidia;
    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    // Names of detected NVIDIA GPUs (populated during init for diagnostics)
    public IReadOnlyList<string> DetectedGpuNames { get; private set; } = [];

    // ── Constructor: try to load and init NVAPI ───────────────────────────────

    public NvApiGpuBackend()
    {
        try
        {
            Initialize();
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = $"NVAPI init exception: {ex.Message}";
            EngineLog.Error("NvApiGpuBackend init failed", ex);
        }
    }

    private void Initialize()
    {
        // Step 1: Load nvapi64.dll
        _hModule = LoadLibrary("nvapi64.dll");
        if (_hModule == IntPtr.Zero)
        {
            IsAvailable = false;
            UnavailableReason = "nvapi64.dll not found — no NVIDIA driver installed.";
            return;
        }

        // Step 2: Get the single NVAPI entry point
        var queryPtr = GetProcAddress(_hModule, "nvapi_QueryInterface");
        if (queryPtr == IntPtr.Zero)
        {
            IsAvailable = false;
            UnavailableReason = "nvapi_QueryInterface not found in nvapi64.dll.";
            return;
        }

        var queryInterface = Marshal.GetDelegateForFunctionPointer<NvAPI_QueryInterface_t>(queryPtr);

        // Step 3: Resolve NvAPI_Initialize and call it
        var initFnPtr = queryInterface(ID_NvAPI_Initialize);
        if (initFnPtr == IntPtr.Zero)
        {
            IsAvailable = false;
            UnavailableReason = "NvAPI_Initialize function pointer is null.";
            return;
        }

        var initFn = Marshal.GetDelegateForFunctionPointer<NvAPI_Initialize_t>(initFnPtr);
        var initResult = initFn();
        // NVAPI_OK = 0
        if (initResult != 0)
        {
            IsAvailable = false;
            UnavailableReason = $"NvAPI_Initialize returned error code {initResult}.";
            return;
        }

        // Step 4: Enumerate physical GPUs to confirm at least one is present
        var enumPtr = queryInterface(ID_NvAPI_EnumPhysicalGPUs);
        if (enumPtr == IntPtr.Zero)
        {
            IsAvailable = false;
            UnavailableReason = "NvAPI_EnumPhysicalGPUs function pointer is null.";
            return;
        }

        var enumFn = Marshal.GetDelegateForFunctionPointer<NvAPI_EnumPhysicalGPUs_t>(enumPtr);
        var handles = new IntPtr[64];
        var enumResult = enumFn(handles, out int gpuCount);

        if (enumResult != 0 || gpuCount == 0)
        {
            IsAvailable = false;
            UnavailableReason = gpuCount == 0
                ? "No NVIDIA GPUs enumerated by NVAPI."
                : $"NvAPI_EnumPhysicalGPUs returned error code {enumResult}.";
            return;
        }

        // Step 5: Collect GPU names (best-effort; non-fatal if name retrieval fails)
        var names = new List<string>();
        var getNamePtr = queryInterface(ID_NvAPI_GPU_GetFullName);
        if (getNamePtr != IntPtr.Zero)
        {
            var getNameFn = Marshal.GetDelegateForFunctionPointer<NvAPI_GPU_GetFullName_t>(getNamePtr);
            for (int i = 0; i < gpuCount; i++)
            {
                try
                {
                    var nameBytes = new byte[64];
                    if (getNameFn(handles[i], nameBytes) == 0)
                    {
                        var name = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0').Trim();
                        if (!string.IsNullOrEmpty(name)) names.Add(name);
                    }
                }
                catch { /* non-fatal */ }
            }
        }

        if (names.Count == 0)
        {
            // Fallback name when retrieval failed
            for (int i = 0; i < gpuCount; i++) names.Add($"NVIDIA GPU #{i}");
        }

        DetectedGpuNames = names.AsReadOnly();
        IsAvailable = true;
        UnavailableReason = null;
        EngineLog.Write($"NvApiGpuBackend: found {gpuCount} GPU(s): {string.Join(", ", names)}");
    }

    // ── Capabilities ──────────────────────────────────────────────────────────

    public GpuControlCapabilities GetCapabilities()
    {
        if (!IsAvailable)
        {
            return new GpuControlCapabilities { CanReadTelemetry = false };
        }

        // Telemetry is always provided via LibreHardwareMonitor (SensorService),
        // not via NVAPI directly.
        // Write capabilities are reported as false because the OC write path
        // (NvAPI_GPU_SetPstates20 / clock-offset structs) is not implemented in
        // this build — the exact struct versions are driver-dependent and issuing
        // wrong data could destabilize the GPU.  A future build may enable these
        // once the structs are validated on a representative set of driver versions.
        return new GpuControlCapabilities
        {
            CanReadTelemetry   = true,
            CanSetCoreOffset   = false,    // not verified safe — see class doc
            CanSetMemoryOffset = false,
            CanSetPowerLimit   = false,
            CanSetTempLimit    = false,
            CanSetFan          = false,
            CoreOffsetRangeMhz        = (-200, 300),
            MemoryOffsetRangeMhz      = (-500, 1500),
            PowerLimitRangePercent    = (50, 120),
        };
    }

    // ── Apply ─────────────────────────────────────────────────────────────────

    public bool TryApply(GpuControlState clampedState, out string error)
    {
        // OC write via NVAPI is intentionally not implemented.
        // The NvAPI_GPU_SetPstates20 and power-limit structs are driver-version
        // dependent. Guessing at struct layouts could issue incorrect native calls
        // that misconfigure or destabilize the GPU.  This is a deliberate safety
        // decision: a backend that honestly reports it cannot write is safer than
        // one that issues wrong native calls.
        error = "NVAPI OC write is not implemented in this build — use a vendor tool " +
                "(MSI Afterburner, NVIDIA App) to apply GPU overclocking. " +
                "Telemetry read-back via LibreHardwareMonitor is still available.";
        return false;
    }

    public void ResetToDefault()
    {
        // No OC was written, so nothing to reset.
        // If write is implemented in a future build, this would call
        // NvAPI_GPU_SetPstates20 to restore clock offsets to 0 and
        // power limit to 100%.
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hModule != IntPtr.Zero)
        {
            try { FreeLibrary(_hModule); } catch { }
            _hModule = IntPtr.Zero;
        }
    }
}
