using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

/// <summary>
/// "CPU &amp; Power" — the merged Performance + Tuning destination from the Optimize hub.
/// Hosts two view-models: <see cref="ViewModel"/> (PerformanceCategoryViewModel) drives the
/// optimizations + power-plan + processes panel; <see cref="TuningVM"/> drives the advanced
/// tuning panel (CPU sliders, presets, stress test, GPU OC, memory diagnostics).
/// The in-page <c>Segmented</c> switches between the two panels.
/// </summary>
public sealed partial class PerformancePage : Page
{
    public PerformanceCategoryViewModel ViewModel { get; }
    public TuningViewModel TuningVM { get; }
    private readonly ISystemRepairService _repair;
    private readonly Dictionary<string, EventHandler<bool>> _toggleHandlers = [];

    private readonly ISystemDataBus _bus;
    private const int TrendLength = 40;
    private readonly Queue<double> _cpuTrend = new();
    private readonly Queue<double> _memTrend = new();

    // Guard against re-entrant SelectionChanged while loading
    private bool _suppressBoostModeChange;

    /// <summary>
    /// When HubPage navigates here with a sub-section int parameter, land on that inner
    /// Segmented panel (0 = Optimizations &amp; Power, 1 = Advanced Tuning). Lets the AI
    /// navigate_to_page("Tuning") open the Tuning panel directly inside the Optimize hub
    /// instead of dumping the user on the default panel.
    /// </summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is int idx && SectionSeg is not null
            && idx >= 0 && idx < SectionSeg.Items.Count)
        {
            SectionSeg.SelectedIndex = idx;
        }
    }

    public PerformancePage()
    {
        ViewModel = App.GetService<PerformanceCategoryViewModel>();
        TuningVM  = App.GetService<TuningViewModel>();
        _bus      = App.GetService<ISystemDataBus>();
        _repair   = App.GetService<ISystemRepairService>();
        InitializeComponent();
        Unloaded += (_, _) => _bus.MetricsUpdated -= OnMetrics;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
        await ViewModel.LoadPowerAsync();

        _bus.MetricsUpdated += OnMetrics;
        if (_bus.LatestMetrics is { } seed) OnMetrics(seed);

        // Tuning lazy-loads on demand. Init dispatcher now so cross-thread updates work
        // even if the user immediately switches to the Tuning panel.
        TuningVM.InitDispatcher(DispatcherQueue.GetForCurrentThread());
        await TuningVM.LoadAsync();
        SyncBoostModeCombo();
    }

    // ── Section switcher (Optimizations & Power | Advanced Tuning) ───────────

    private void Section_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Guard: panels may not yet be named during InitializeComponent.
        if (PanelOptimizations is null) return;

        var i = SectionSeg.SelectedIndex;
        PanelOptimizations.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelTuning.Visibility        = i == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Live metrics → shared StatTiles ──────────────────────────────────────

    private void OnMetrics(SystemResource m)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            double cpu = m.CpuUsagePercentage;
            Push(_cpuTrend, cpu);
            CpuTile.Value = cpu.ToString("F0");
            CpuTile.Caption = m.CpuTemperature > 0 ? $"{m.CpuTemperature:F0}°C" : "live";
            CpuTile.SetTrend(_cpuTrend.ToArray());

            double memTotal = m.TotalPhysicalMemory;
            double memUsed = memTotal > 0 ? memTotal - m.AvailablePhysicalMemory : 0;
            double memPct = memTotal > 0 ? memUsed / memTotal * 100.0 : 0;
            Push(_memTrend, memPct);
            MemTile.Value = memPct.ToString("F0");
            MemTile.Caption = $"{memUsed / 1_073_741_824.0:F1} GB used";
            MemTile.SetTrend(_memTrend.ToArray());

            OptTile.Value = $"{ViewModel.ActiveCount} / {ViewModel.TotalCount}";
        });
    }

    private static void Push(Queue<double> q, double v)
    {
        q.Enqueue(v);
        while (q.Count > TrendLength) q.Dequeue();
    }

    // ── Panel A: Optimizations & Power ───────────────────────────────────────

    private void OptimizationCard_Loaded(object sender, RoutedEventArgs e)
        => CategoryPageHelper.OnCardLoaded(sender, XamlRoot, ViewModel, _toggleHandlers);

    private async void PowerPlan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid guid)
        {
            var plan = ViewModel.PowerPlans.FirstOrDefault(p => p.Guid == guid);
            if (plan != null)
                await ViewModel.SetPowerPlanCommand.ExecuteAsync(plan);
        }
    }

    private async void UltimatePerf_Click(object sender, RoutedEventArgs e)
        => await ViewModel.CreateUltimatePlanCommand.ExecuteAsync(null);

    private void Priority_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        if (cb.Tag is not int pid) return;
        if (cb.SelectedItem is not ComboBoxItem item) return;

        var priorityStr = item.Content?.ToString();
        if (string.IsNullOrEmpty(priorityStr)) return;

        var priority = priorityStr switch
        {
            "High" => ProcessPriorityClass.High,
            "AboveNormal" => ProcessPriorityClass.AboveNormal,
            "Normal" => ProcessPriorityClass.Normal,
            "BelowNormal" => ProcessPriorityClass.BelowNormal,
            "Idle" => ProcessPriorityClass.Idle,
            _ => (ProcessPriorityClass?)null
        };

        if (priority.HasValue)
            ViewModel.SetProcessPriority(pid, priority.Value);
    }

    private async void Affinity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not int pid) return;

        var coreCount   = ViewModel.LogicalCoreCount;
        var currentMask = ViewModel.GetAffinity(pid);
        var currentCores = AffinityMask.ToCores(currentMask);

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Select which logical cores (CPU 0–{coreCount - 1}) this process may use.",
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var checkBoxes = new CheckBox[coreCount];
        var wrapGrid = new VariableSizedWrapGrid
        {
            Orientation = Orientation.Horizontal,
            ItemWidth   = 90,
            ItemHeight  = 32,
        };

        for (var i = 0; i < coreCount; i++)
        {
            var cb = new CheckBox
            {
                Content   = $"CPU {i}",
                IsChecked = currentCores.Contains(i),
                Tag       = i,
            };
            checkBoxes[i] = cb;
            wrapGrid.Children.Add(cb);
        }

        panel.Children.Add(wrapGrid);

        var applyButton = new Button
        {
            Content             = "Apply",
            Style               = Application.Current.Resources["AccentButtonStyle"] as Style,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin              = new Thickness(0, 12, 0, 0),
        };
        panel.Children.Add(applyButton);

        void UpdateApply()
        {
            applyButton.IsEnabled = checkBoxes.Any(c => c.IsChecked == true);
        }
        foreach (var c in checkBoxes)
            c.Checked += (_, _) => UpdateApply();
        foreach (var c in checkBoxes)
            c.Unchecked += (_, _) => UpdateApply();
        UpdateApply();

        var dialog = new ContentDialog
        {
            Title           = $"CPU Affinity — PID {pid}",
            Content         = panel,
            CloseButtonText = "Cancel",
            DefaultButton   = ContentDialogButton.Close,
            XamlRoot        = XamlRoot,
        };

        applyButton.Click += async (_, _) =>
        {
            var selected = checkBoxes
                .Where(c => c.IsChecked == true)
                .Select(c => (int)c.Tag!)
                .ToArray();

            var mask = AffinityMask.FromCores(selected, coreCount);

            if (!AffinityMask.IsValid(mask, coreCount))
                return;

            var ok = ViewModel.SetAffinity(pid, mask);
            if (!ok)
            {
                var errBar = new InfoBar
                {
                    Severity  = InfoBarSeverity.Error,
                    Title     = "Failed to set affinity. The process may have exited or access was denied.",
                    IsOpen    = true,
                    IsClosable = false,
                    Margin    = new Thickness(0, 8, 0, 0),
                };
                panel.Children.Add(errBar);
                return;
            }

            dialog.Hide();
            await Task.CompletedTask;
        };

        await dialog.ShowAsync();
    }

    // ── Process row context menu (Batch 3) ──────────────────────────────────
    private static ProcessPriorityInfo? ProcOf(object sender)
        => (sender as FrameworkElement)?.Tag as ProcessPriorityInfo;

    private void ProcOpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (ProcOf(sender) is not { } p) return;
        if (!RowActions.RevealProcessLocation(p.Pid))
            _ = DialogHelper.InfoAsync(XamlRoot, "Åbn filplacering",
                $"Kunne ikke finde filplaceringen for {p.Name} (PID {p.Pid}) — processen kan være beskyttet eller afsluttet.");
    }

    private async void ProcEndTask_Click(object sender, RoutedEventArgs e)
    {
        if (ProcOf(sender) is not { } p) return;
        var confirm = await DialogHelper.ConfirmAsync(XamlRoot, "Afslut proces?",
            $"Afslut {p.Name} (PID {p.Pid})? Ikke-gemt arbejde i processen går tabt.", "Afslut");
        if (!confirm) return;
        var (ok, msg) = RowActions.TryEndProcess(p.Pid);
        await DialogHelper.InfoAsync(XamlRoot, "Afslut proces", msg);
        if (ok) ViewModel.Load();
    }

    // ── Panel B: Advanced Tuning ─────────────────────────────────────────────

    private void SyncBoostModeCombo()
    {
        if (BoostModeCombo is null) return;
        _suppressBoostModeChange = true;
        try
        {
            var targetIndex = (int)TuningVM.CurrentCpu.BoostMode;
            if (targetIndex >= 0 && targetIndex < BoostModeCombo.Items.Count)
                BoostModeCombo.SelectedIndex = targetIndex;
        }
        finally
        {
            _suppressBoostModeChange = false;
        }
    }

    private void BoostModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressBoostModeChange) return;

        if (BoostModeCombo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tagStr &&
            int.TryParse(tagStr, out var modeIndex))
        {
            TuningVM.CurrentCpu.BoostMode = (BoostMode)modeIndex;
        }
    }

    private async void ApplyPreset_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(async () =>
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                var preset = TuningVM.Presets.FirstOrDefault(p => p.Id == id);
                if (preset != null)
                {
                    await TuningVM.ApplyPresetCommand.ExecuteAsync(preset);
                    SyncBoostModeCombo();
                }
            }
        }, XamlRoot, "Apply preset");

    private async void ApplyCpu_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(async () =>
        {
            await TuningVM.ApplyCurrentCpuCommand.ExecuteAsync(null);
            SyncBoostModeCombo();
        }, XamlRoot, "Apply CPU settings");

    private async void Revert_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(async () =>
        {
            await TuningVM.RevertCommand.ExecuteAsync(null);
            SyncBoostModeCombo();
        }, XamlRoot, "Revert CPU settings");

    private async void LaunchTool_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(async () =>
        {
            if (sender is Button btn && btn.Tag is string name)
            {
                var tool = TuningVM.GpuTools.FirstOrDefault(t => t.Name == name);
                if (tool != null)
                    await TuningVM.LaunchToolCommand.ExecuteAsync(tool);
            }
        }, XamlRoot, "Launch GPU tool");

    private async void MemoryTest_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(
            () => _repair.LaunchMemoryTestAsync(),
            XamlRoot, "Memory test");

    private async void RunBuiltIn_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(
            () => TuningVM.RunBuiltInStressCommand.ExecuteAsync(null),
            XamlRoot, "Stress test");

    private void StopStress_Click(object sender, RoutedEventArgs e)
        => TuningVM.StopStressCommand.Execute(null);

    private async void LaunchPrime95_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(
            () => TuningVM.LaunchPrime95Command.ExecuteAsync(null),
            XamlRoot, "Launch Prime95");

    private async void LaunchCinebench_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(
            () => TuningVM.LaunchCinebenchCommand.ExecuteAsync(null),
            XamlRoot, "Launch Cinebench");

    private void ApplyGpuOc_Click(object sender, RoutedEventArgs e)
        => TuningVM.ApplyGpuOcCommand.Execute(null);

    private async void ApplyGpuOcWatchdog_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(
            () => TuningVM.ApplyGpuOcWithWatchdogCommand.ExecuteAsync(null),
            XamlRoot, "GPU OC watchdog test");

    private void ResetGpuOc_Click(object sender, RoutedEventArgs e)
        => TuningVM.ResetGpuToDefaultCommand.Execute(null);

    private void StopGpuWatchdog_Click(object sender, RoutedEventArgs e)
        => TuningVM.StopGpuWatchdogCommand.Execute(null);

    private async void OpenGpuVendorTool_Click(object sender, RoutedEventArgs e)
        => await PageExceptionHelper.SafeAsync(
            () => TuningVM.OpenGpuVendorToolCommand.ExecuteAsync(null),
            XamlRoot, "Open GPU vendor tool");

    private void LaunchXtu_Click(object sender, RoutedEventArgs e)
    {
        var path = TuningViewModel.DetectXtuPathPublic();
        if (!string.IsNullOrEmpty(path))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = path, UseShellExecute = true });
    }

    private void LaunchRyzenMaster_Click(object sender, RoutedEventArgs e)
    {
        var path = TuningViewModel.DetectRyzenMasterPathPublic();
        if (!string.IsNullOrEmpty(path))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = path, UseShellExecute = true });
    }
}
