using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class TuningPage : Page
{
    public TuningViewModel ViewModel { get; }
    private readonly ISystemRepairService _repair;

    // Guard against re-entrant SelectionChanged while loading
    private bool _suppressBoostModeChange;

    public TuningPage()
    {
        ViewModel = App.GetService<TuningViewModel>();
        _repair   = App.GetService<ISystemRepairService>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        SyncBoostModeCombo();
    }

    // ── Sync the ComboBox selection to the current BoostMode value ────────────

    private void SyncBoostModeCombo()
    {
        _suppressBoostModeChange = true;
        try
        {
            var targetIndex = (int)ViewModel.CurrentCpu.BoostMode;
            if (targetIndex >= 0 && targetIndex < BoostModeCombo.Items.Count)
                BoostModeCombo.SelectedIndex = targetIndex;
        }
        finally
        {
            _suppressBoostModeChange = false;
        }
    }

    // ── Boost mode ComboBox changed ───────────────────────────────────────────

    private void BoostModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressBoostModeChange) return;

        if (BoostModeCombo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tagStr &&
            int.TryParse(tagStr, out var modeIndex))
        {
            ViewModel.CurrentCpu.BoostMode = (BoostMode)modeIndex;
        }
    }

    // ── Preset card "Apply" button ────────────────────────────────────────────

    private async void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            var preset = ViewModel.Presets.FirstOrDefault(p => p.Id == id);
            if (preset != null)
            {
                await ViewModel.ApplyPresetCommand.ExecuteAsync(preset);
                SyncBoostModeCombo();
            }
        }
    }

    // ── Apply manual CPU sliders ──────────────────────────────────────────────

    private async void ApplyCpu_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ApplyCurrentCpuCommand.ExecuteAsync(null);
        SyncBoostModeCombo();
    }

    // ── Revert to Stock defaults ──────────────────────────────────────────────

    private async void Revert_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RevertCommand.ExecuteAsync(null);
        SyncBoostModeCombo();
    }

    // ── GPU vendor tool card button ───────────────────────────────────────────

    private async void LaunchTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string name)
        {
            var tool = ViewModel.GpuTools.FirstOrDefault(t => t.Name == name);
            if (tool != null)
                await ViewModel.LaunchToolCommand.ExecuteAsync(tool);
        }
    }

    // ── Memory test launcher ──────────────────────────────────────────────────

    private async void MemoryTest_Click(object sender, RoutedEventArgs e)
    {
        await _repair.LaunchMemoryTestAsync();
    }
}
