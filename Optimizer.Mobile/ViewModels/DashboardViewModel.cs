using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.Mobile.Services;
using System.Text.Json;

namespace Optimizer.Mobile.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ApiClient _api;

    [ObservableProperty] private string _cpuText  = "—";
    [ObservableProperty] private string _memText  = "—";
    [ObservableProperty] private string _gpuText  = "—";
    [ObservableProperty] private string _cpuTemp  = "—";
    [ObservableProperty] private string _gpuTemp  = "—";
    [ObservableProperty] private string _cpuPower = "—";
    [ObservableProperty] private string _gpuPower = "—";
    [ObservableProperty] private double _cpuPct   = 0;
    [ObservableProperty] private double _memPct   = 0;
    [ObservableProperty] private double _gpuPct   = 0;
    [ObservableProperty] private bool _isBusy;

    public DashboardViewModel(ApiClient api)
    {
        _api = api;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var metrics = await _api.GetJsonAsync("/api/metrics");
            if (metrics.HasValue)
            {
                var m = metrics.Value;
                var cpu = m.TryGetProperty("cpu", out var cpuEl) ? cpuEl.GetDouble() : 0;
                double memPct = 0;
                if (m.TryGetProperty("memory", out var memEl))
                {
                    var total = memEl.TryGetProperty("total", out var t) ? t.GetDouble() : 0;
                    var avail = memEl.TryGetProperty("available", out var a) ? a.GetDouble() : 0;
                    if (total > 0) memPct = (total - avail) / total * 100;
                }
                var gpu = m.TryGetProperty("gpu", out var gpuEl) ? gpuEl.GetDouble() : 0;

                CpuText = $"{cpu:F0}%";
                MemText = $"{memPct:F0}%";
                GpuText = $"{gpu:F0}%";
                CpuPct  = Math.Min(1.0, cpu / 100.0);
                MemPct  = Math.Min(1.0, memPct / 100.0);
                GpuPct  = Math.Min(1.0, gpu / 100.0);
            }

            var sensors = await _api.GetJsonAsync("/api/sensors");
            if (sensors.HasValue)
            {
                var s = sensors.Value;
                if (s.TryGetProperty("cpuTemp",  out var ct)) CpuTemp  = $"{ct.GetDouble():F0}°C";
                if (s.TryGetProperty("gpuTemp",  out var gt)) GpuTemp  = $"{gt.GetDouble():F0}°C";
                if (s.TryGetProperty("cpuPower", out var cp)) CpuPower = $"{cp.GetDouble():F0}W";
                if (s.TryGetProperty("gpuPower", out var gp)) GpuPower = $"{gp.GetDouble():F0}W";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task CleanupAsync()
    {
        var result = await _api.PostJsonAsync("/api/cleanup");
        bool success = result.HasValue &&
            result.Value.TryGetProperty("success", out var s) && s.GetBoolean();
        string msg = success ? "Cleanup started!" : "Cleanup failed.";
        if (result.HasValue && result.Value.TryGetProperty("message", out var msgEl))
            msg = msgEl.GetString() ?? msg;
        await Shell.Current.DisplayAlertAsync(success ? "Done" : "Error", msg, "OK");
    }
}
