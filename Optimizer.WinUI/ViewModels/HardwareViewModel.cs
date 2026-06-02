using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class HardwareViewModel : ObservableObject
{
    private readonly IHardwareInfoService _hardwareService;
    private readonly ISensorService _sensorService;
    private readonly ISystemDataBus _dataBus;

    private DispatcherQueue? _dispatcherQueue;
    private bool _sensorActive;

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuClockText))]
    [NotifyPropertyChangedFor(nameof(CpuCoresText))]
    [NotifyPropertyChangedFor(nameof(CpuCacheText))]
    [NotifyPropertyChangedFor(nameof(MemoryTotalText))]
    [NotifyPropertyChangedFor(nameof(MemoryModulesText))]
    [NotifyPropertyChangedFor(nameof(MemoryModulePartsText))]
    [NotifyPropertyChangedFor(nameof(MemoryConfiguredSpeedText))]
    [NotifyPropertyChangedFor(nameof(MemoryConfiguredVoltageText))]
    [NotifyPropertyChangedFor(nameof(MemoryModuleList))]
    [NotifyPropertyChangedFor(nameof(BiosVersionText))]
    [NotifyPropertyChangedFor(nameof(BiosDateText))]
    [NotifyPropertyChangedFor(nameof(OsVersionText))]
    [NotifyPropertyChangedFor(nameof(OsInstallDateText))]
    [NotifyPropertyChangedFor(nameof(SecureBootText))]
    [NotifyPropertyChangedFor(nameof(UefiText))]
    private HardwareInfo? _hardware;

    [ObservableProperty] private HardwareSnapshot? _sensors;
    [ObservableProperty] private bool _sensorsAvailable;
    [ObservableProperty] private string _sensorUnavailableReason = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    partial void OnStatusMessageChanged(string value)
        => OnPropertyChanged(nameof(HasStatusMessage));

    public string CategoryName => "Hardware";
    public string CategoryIcon => ""; // HardDrive Segoe MDL2 glyph

    public HardwareViewModel(
        IHardwareInfoService hardwareService,
        ISensorService sensorService,
        ISystemDataBus dataBus)
    {
        _hardwareService = hardwareService;
        _sensorService   = sensorService;
        _dataBus         = dataBus;
        SensorsAvailable = sensorService.IsAvailable;
        SensorUnavailableReason = sensorService.InitializationError ?? string.Empty;

        // Apply cached snapshot immediately if bus already has one
        if (dataBus.LatestSensors is { } snap)
            Sensors = snap;
    }

    // ── Sensor lifecycle (called from page code-behind) ───────────────────

    public void StartSensorTimer()
    {
        if (_sensorActive || !_sensorService.IsAvailable) return;
        _sensorActive = true;
        _dispatcherQueue ??= DispatcherQueue.GetForCurrentThread();
        _dataBus.SensorsUpdated += OnSensorsUpdated;
        _dataBus.SetSensorsActive(true);

        // Show last known snapshot right away
        if (_dataBus.LatestSensors is { } latest)
            _dispatcherQueue?.TryEnqueue(() => Sensors = latest);
    }

    public void StopSensorTimer()
    {
        if (!_sensorActive) return;
        _sensorActive = false;
        _dataBus.SensorsUpdated -= OnSensorsUpdated;
        _dataBus.SetSensorsActive(false);
    }

    private void OnSensorsUpdated(HardwareSnapshot snap)
    {
        _dispatcherQueue?.TryEnqueue(() => Sensors = snap);
    }

    // ── Commands ─────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    public void ExportToFile()
    {
        if (Hardware == null) return;
        try
        {
            var text   = BuildReport(Hardware);
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var path   = Path.Combine(folder, $"Optimizer-Hardware-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, text);
            StatusMessage = $"Report saved to {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    // ── Loading ───────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        IsLoading     = true;
        StatusMessage = string.Empty;
        try
        {
            Hardware = await _hardwareService.GetHardwareInfoAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading hardware info: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Formatted display helpers (used by XAML) ──────────────────────────

    public string CpuClockText =>
        Hardware?.Cpu.MaxClockSpeedMHz is int mhz and > 0
            ? $"{mhz / 1000.0:F2} GHz"
            : "—";

    public string CpuCoresText =>
        Hardware?.Cpu is CpuInfo cpu
            ? $"{cpu.Cores} physical / {cpu.LogicalProcessors} threads"
            : "—";

    public string CpuCacheText =>
        Hardware?.Cpu is CpuInfo c
            ? $"L2: {c.L2CacheKB} KB  /  L3: {c.L3CacheKB} KB"
            : "—";

    public string MemoryTotalText =>
        Hardware?.Memory.TotalBytes is long b and > 0
            ? ByteFormatter.Format(b)
            : "—";

    public string MemoryModulesText =>
        Hardware?.Memory is MemoryHardwareInfo m
            ? $"{m.ModuleCount} × {m.SpeedMHz} MHz ({m.FormFactor})"
            : "—";

    public string MemoryModulePartsText =>
        Hardware?.Memory?.ModuleParts is List<string> parts && parts.Count > 0
            ? string.Join("\n", parts)
            : "—";

    public string MemoryConfiguredSpeedText =>
        Hardware?.Memory?.ConfiguredClockSpeedMhz is int cs and > 0
            ? $"{cs} MHz"
            : "—";

    public string MemoryConfiguredVoltageText =>
        Hardware?.Memory?.ConfiguredVoltageMv is int mv and > 0
            ? $"{(mv / 1000.0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} V"
            : "—";

    public IReadOnlyList<Models.MemoryModuleInfo> MemoryModuleList =>
        Hardware?.Memory?.Modules ?? [];

    public string BiosVersionText =>
        Hardware?.Motherboard is MotherboardInfo mb
            ? $"{mb.BiosVendor} {mb.BiosVersion}".Trim()
            : "—";

    public string BiosDateText =>
        Hardware?.Motherboard.BiosDate is DateTime d && d != default
            ? d.ToString("yyyy-MM-dd")
            : "—";

    public string OsVersionText =>
        Hardware?.Os is OsInfo os
            ? $"{os.Version}  (Build {os.Build})"
            : "—";

    public string OsInstallDateText =>
        Hardware?.Os.InstallDate is DateTime id && id != default
            ? id.ToString("yyyy-MM-dd")
            : "—";

    public string SecureBootText =>
        Hardware?.Os is OsInfo osi ? (osi.IsSecureBoot ? "Enabled" : "Disabled") : "—";

    public string UefiText =>
        Hardware?.Os is OsInfo osu ? (osu.IsUefi ? "UEFI" : "Legacy BIOS") : "—";

    // ── Report builder ────────────────────────────────────────────────────

    private static string BuildReport(HardwareInfo h)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== HARDWARE REPORT ===");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("--- CPU ---");
        sb.AppendLine($"  Name:       {h.Cpu.Name}");
        sb.AppendLine($"  Cores:      {h.Cpu.Cores} physical / {h.Cpu.LogicalProcessors} threads");
        sb.AppendLine($"  Max Clock:  {h.Cpu.MaxClockSpeedMHz / 1000.0:F2} GHz");
        sb.AppendLine($"  L2 Cache:   {h.Cpu.L2CacheKB} KB");
        sb.AppendLine($"  L3 Cache:   {h.Cpu.L3CacheKB} KB");
        sb.AppendLine($"  Socket:     {h.Cpu.Socket}");
        sb.AppendLine();
        sb.AppendLine("--- Memory ---");
        sb.AppendLine($"  Total:   {ByteFormatter.Format(h.Memory.TotalBytes)}");
        sb.AppendLine($"  Modules: {h.Memory.ModuleCount} × {h.Memory.SpeedMHz} MHz ({h.Memory.FormFactor})");
        foreach (var mod in h.Memory.ModuleParts)
            sb.AppendLine($"    - {mod}");
        sb.AppendLine();
        sb.AppendLine("--- GPU(s) ---");
        foreach (var g in h.Gpus)
        {
            sb.AppendLine($"  {g.Name}");
            sb.AppendLine($"    VRAM:   {ByteFormatter.Format(g.VramBytes)}");
            sb.AppendLine($"    Driver: {g.DriverVersion}");
        }
        sb.AppendLine();
        sb.AppendLine("--- Motherboard ---");
        sb.AppendLine($"  Board: {h.Motherboard.Manufacturer} {h.Motherboard.Model}");
        sb.AppendLine($"  BIOS:  {h.Motherboard.BiosVendor} {h.Motherboard.BiosVersion}");
        if (h.Motherboard.BiosDate != default)
            sb.AppendLine($"  BIOS Date: {h.Motherboard.BiosDate:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("--- Storage ---");
        foreach (var s in h.Storage)
        {
            sb.AppendLine($"  {s.Model}");
            sb.AppendLine($"    Size:      {ByteFormatter.Format(s.SizeBytes)}");
            sb.AppendLine($"    Interface: {s.InterfaceType}");
            sb.AppendLine($"    Type:      {s.MediaType}");
            if (!string.IsNullOrEmpty(s.SerialNumber))
                sb.AppendLine($"    Serial:    {s.SerialNumber}");
        }
        sb.AppendLine();
        sb.AppendLine("--- Network Adapters ---");
        foreach (var n in h.NetworkAdapters)
        {
            sb.AppendLine($"  {n.Name}");
            sb.AppendLine($"    MAC: {n.MacAddress}");
            if (n.LinkSpeedBps > 0)
                sb.AppendLine($"    Speed: {n.LinkSpeedBps / 1_000_000} Mbps");
        }
        sb.AppendLine();
        sb.AppendLine("--- Displays ---");
        foreach (var d in h.Displays)
            sb.AppendLine($"  {d.Name} — {d.WidthPx}×{d.HeightPx}");
        sb.AppendLine();
        sb.AppendLine("--- Operating System ---");
        sb.AppendLine($"  {h.Os.Name}");
        sb.AppendLine($"  Version:      {h.Os.Version}  (Build {h.Os.Build})");
        sb.AppendLine($"  Architecture: {h.Os.Architecture}");
        if (h.Os.InstallDate != default)
            sb.AppendLine($"  Installed:    {h.Os.InstallDate:yyyy-MM-dd}");
        sb.AppendLine($"  Secure Boot:  {(h.Os.IsSecureBoot ? "Enabled" : "Disabled")}");
        sb.AppendLine($"  UEFI:         {(h.Os.IsUefi ? "Yes" : "No")}");
        return sb.ToString();
    }
}
