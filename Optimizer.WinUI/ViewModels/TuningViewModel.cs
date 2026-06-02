using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class TuningViewModel : ObservableObject
{
    private readonly ITuningService _tuning;

    [ObservableProperty] private CpuTuning currentCpu = new();
    [ObservableProperty] private RamInfo ramInfo = new();
    [ObservableProperty] private bool isApplying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string statusMessage = "";

    [ObservableProperty] private bool hasReadDisclaimer;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    public ObservableCollection<TuningPreset> Presets { get; } = [];
    public ObservableCollection<GpuClockInfo> Gpus    { get; } = [];

    public string CategoryName => "Tuning";
    public string CategoryIcon => "⚡";  // ⚡

    // Convenience: display-friendly boost mode name for the current setting.
    public string BoostModeDisplay => CurrentCpu.BoostMode switch
    {
        BoostMode.Disabled                       => "Disabled",
        BoostMode.Enabled                        => "Enabled",
        BoostMode.Aggressive                     => "Aggressive",
        BoostMode.EfficientEnabled               => "Efficient Enabled",
        BoostMode.EfficientAggressive            => "Efficient Aggressive",
        BoostMode.AggressiveAtGuaranteed         => "Aggressive at Guaranteed",
        BoostMode.EfficientAggressiveAtGuaranteed => "Efficient Aggressive at Guaranteed",
        _ => CurrentCpu.BoostMode.ToString()
    };

    public TuningViewModel(ITuningService tuning)
    {
        _tuning = tuning;
        foreach (var p in tuning.GetPresets()) Presets.Add(p);
    }

    public async Task LoadAsync()
    {
        CurrentCpu = await _tuning.GetCurrentCpuTuningAsync();
        RamInfo    = await _tuning.GetRamInfoAsync();

        var gpus = await _tuning.GetGpuClocksAsync();
        Gpus.Clear();
        foreach (var g in gpus) Gpus.Add(g);

        OnPropertyChanged(nameof(BoostModeDisplay));
    }

    // ── Apply a preset card ───────────────────────────────────────────────────

    [RelayCommand]
    public async Task ApplyPresetAsync(TuningPreset preset)
    {
        if (!HasReadDisclaimer)
        {
            StatusMessage = "Please acknowledge the disclaimer before applying a preset.";
            return;
        }

        IsApplying = true;
        try
        {
            var ok = await _tuning.ApplyPresetAsync(preset);
            StatusMessage = ok
                ? $"Applied preset: {preset.Name}"
                : "Failed to apply preset — check app is running as Administrator.";

            if (ok) await LoadAsync();
        }
        finally { IsApplying = false; }
    }

    // ── Revert to stock defaults ──────────────────────────────────────────────

    [RelayCommand]
    public async Task RevertAsync()
    {
        IsApplying = true;
        try
        {
            var ok = await _tuning.RevertToDefaultsAsync();
            StatusMessage = ok
                ? "Reverted to Stock defaults."
                : "Revert failed — check app is running as Administrator.";
            if (ok) await LoadAsync();
        }
        finally { IsApplying = false; }
    }

    // ── Apply manually-configured CPU sliders ─────────────────────────────────

    [RelayCommand]
    public async Task ApplyCurrentCpuAsync()
    {
        if (!HasReadDisclaimer)
        {
            StatusMessage = "Please acknowledge the disclaimer first.";
            return;
        }

        IsApplying = true;
        try
        {
            var ok = await _tuning.ApplyCpuTuningAsync(CurrentCpu);
            StatusMessage = ok
                ? "CPU tuning applied."
                : "Failed to apply CPU tuning — check app is running as Administrator.";
            if (ok) await LoadAsync();
        }
        finally { IsApplying = false; }
    }
}
