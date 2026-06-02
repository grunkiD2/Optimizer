using System.Runtime.InteropServices;
using Optimizer.WinUI.Models.Gpu;

namespace Optimizer.WinUI.Services.Gpu;

/// <summary>
/// AMD GPU control backend via ADL (atiadlxx.dll).
///
/// SAFETY NOTES:
/// - All native interactions are wrapped in try/catch. If the DLL cannot be
///   loaded, init fails, or no AMD GPU is present, IsAvailable=false and all
///   operations are safe no-ops.
/// - OC WRITE (TryApply) is intentionally NOT implemented. ADL's
///   ADL2_Overdrive*_PerformanceLevels_Set family requires exact struct
///   versions that differ between ADL5, ADL6, Overdrive 7, and Overdrive N.
///   Issuing wrong structs can crash the driver or damage GPU settings.
///   The write path is stubbed with a clear error message.
///   Telemetry (read-only) works through LibreHardwareMonitor, not ADL.
/// </summary>
public sealed class AdlGpuBackend : IGpuControlBackend, IDisposable
{
    // ── P/Invoke helpers ──────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    // ADL2_Main_Control_Create: allocates the ADL context.
    // Signature: int ADL2_Main_Control_Create(ADL_MAIN_MALLOC_CALLBACK callback, int enumConnectedAdapters, ADL_CONTEXT_HANDLE *context)
    // We use a simplified delegate that matches the calling convention.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ADL2_Main_Control_Create_t(
        IntPtr mallocCallback,          // we pass IntPtr.Zero to let ADL use its default allocator
        int enumConnectedAdapters,
        out IntPtr context);

    // ADL2_Main_Control_Destroy: frees the context.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ADL2_Main_Control_Destroy_t(IntPtr context);

    // ADL2_Adapter_NumberOfAdapters_Get: returns total adapter count.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ADL2_Adapter_NumberOfAdapters_Get_t(IntPtr context, out int numAdapters);

    // ── State ─────────────────────────────────────────────────────────────────

    private IntPtr _hModule = IntPtr.Zero;
    private IntPtr _adlContext = IntPtr.Zero;
    private ADL2_Main_Control_Destroy_t? _destroyFn;
    private bool _disposed;

    public GpuVendor Vendor => GpuVendor.Amd;
    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    public int DetectedAdapterCount { get; private set; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public AdlGpuBackend()
    {
        try
        {
            Initialize();
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = $"ADL init exception: {ex.Message}";
            EngineLog.Error("AdlGpuBackend init failed", ex);
        }
    }

    private void Initialize()
    {
        // ADL ships as atiadlxx.dll (32-bit) or atiadlxy.dll (64-bit).
        // On 64-bit Windows the 64-bit variant is named atiadlxx.dll as well
        // (driver package places both and the loader picks the right one).
        _hModule = LoadLibrary("atiadlxx.dll");
        if (_hModule == IntPtr.Zero)
        {
            // Fallback: some older AMD driver versions use atiadlxy.dll for 64-bit
            _hModule = LoadLibrary("atiadlxy.dll");
        }

        if (_hModule == IntPtr.Zero)
        {
            IsAvailable = false;
            UnavailableReason = "atiadlxx.dll not found — no AMD driver installed.";
            return;
        }

        // Resolve ADL2_Main_Control_Create
        var createPtr = GetProcAddress(_hModule, "ADL2_Main_Control_Create");
        if (createPtr == IntPtr.Zero)
        {
            IsAvailable = false;
            UnavailableReason = "ADL2_Main_Control_Create not found in ADL DLL.";
            return;
        }

        var createFn = Marshal.GetDelegateForFunctionPointer<ADL2_Main_Control_Create_t>(createPtr);

        // Resolve ADL2_Main_Control_Destroy (needed for cleanup)
        var destroyPtr = GetProcAddress(_hModule, "ADL2_Main_Control_Destroy");
        if (destroyPtr != IntPtr.Zero)
            _destroyFn = Marshal.GetDelegateForFunctionPointer<ADL2_Main_Control_Destroy_t>(destroyPtr);

        // Call ADL2_Main_Control_Create.  Pass IntPtr.Zero for the malloc callback
        // so ADL uses its internal allocator (documented as valid usage).
        var createResult = createFn(IntPtr.Zero, 1, out _adlContext);
        // ADL_OK = 0; ADL_OK_WAIT = 4 (still success)
        if (createResult != 0 && createResult != 4)
        {
            IsAvailable = false;
            UnavailableReason = $"ADL2_Main_Control_Create returned error code {createResult}.";
            return;
        }

        // Enumerate adapters to confirm at least one AMD GPU is present
        var numAdaptersPtr = GetProcAddress(_hModule, "ADL2_Adapter_NumberOfAdapters_Get");
        if (numAdaptersPtr != IntPtr.Zero)
        {
            var numAdaptersFn = Marshal.GetDelegateForFunctionPointer<ADL2_Adapter_NumberOfAdapters_Get_t>(numAdaptersPtr);
            var enumResult = numAdaptersFn(_adlContext, out int numAdapters);
            if (enumResult == 0 && numAdapters > 0)
            {
                DetectedAdapterCount = numAdapters;
                IsAvailable = true;
                UnavailableReason = null;
                EngineLog.Write($"AdlGpuBackend: found {numAdapters} AMD adapter(s).");
                return;
            }

            if (numAdapters == 0)
            {
                IsAvailable = false;
                UnavailableReason = "No AMD adapters found by ADL.";
                return;
            }
        }

        // If we can't enumerate but init succeeded, still report available
        // (some old driver versions don't export ADL2_Adapter_NumberOfAdapters_Get).
        IsAvailable = true;
        DetectedAdapterCount = -1; // unknown
        UnavailableReason = null;
        EngineLog.Write("AdlGpuBackend: ADL init succeeded (adapter count unknown).");
    }

    // ── Capabilities ──────────────────────────────────────────────────────────

    public GpuControlCapabilities GetCapabilities()
    {
        if (!IsAvailable)
        {
            return new GpuControlCapabilities { CanReadTelemetry = false };
        }

        // Telemetry is provided by LibreHardwareMonitor (SensorService).
        // Write capabilities are false: the ADL Overdrive write functions
        // (ADL2_Overdrive5/6/N_PerformanceLevels_Set etc.) require exact
        // struct versions that differ between Overdrive5, Overdrive6,
        // OverdriveN, and Overdrive8.  Guessing wrong structs can corrupt
        // GPU state. A future build will implement this after validation.
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
        // ADL OC write not implemented in this build — see class-level doc.
        error = "ADL OC write is not implemented in this build — use a vendor tool " +
                "(AMD Software: Adrenalin Edition) to apply GPU overclocking. " +
                "Telemetry read-back via LibreHardwareMonitor is still available.";
        return false;
    }

    public void ResetToDefault()
    {
        // No OC was written, so nothing to reset.
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_adlContext != IntPtr.Zero && _destroyFn != null)
        {
            try { _destroyFn(_adlContext); } catch { }
            _adlContext = IntPtr.Zero;
        }

        if (_hModule != IntPtr.Zero)
        {
            try { FreeLibrary(_hModule); } catch { }
            _hModule = IntPtr.Zero;
        }
    }
}
