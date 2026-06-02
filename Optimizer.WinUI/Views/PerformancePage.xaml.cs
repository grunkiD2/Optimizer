using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class PerformancePage : Page
{
    public PerformanceCategoryViewModel ViewModel { get; }
    private readonly Dictionary<string, EventHandler<bool>> _toggleHandlers = [];

    public PerformancePage()
    {
        ViewModel = App.GetService<PerformanceCategoryViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
        await ViewModel.LoadPowerAsync();
    }

    private void OptimizationCard_Loaded(object sender, RoutedEventArgs e)
        => CategoryPageHelper.OnCardLoaded(sender, XamlRoot, ViewModel, _toggleHandlers);

    // ── Power plan handlers ──────────────────────────────────────────────────

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

    // ── Process priority handler ─────────────────────────────────────────────

    private void Priority_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb) return;

        // Tag is bound as Guid (from PowerPlan.Guid) for the power plan repeater
        // but here it's bound as Pid (int). The ComboBox is inside the process ListView template.
        if (cb.Tag is not int pid) return;
        if (cb.SelectedItem is not ComboBoxItem item) return;

        var priorityStr = item.Content?.ToString();
        if (string.IsNullOrEmpty(priorityStr)) return;

        // Map display strings to ProcessPriorityClass enum values
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

    // ── Per-core affinity handler ────────────────────────────────────────────

    private async void Affinity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not int pid) return;

        var coreCount   = ViewModel.LogicalCoreCount;
        var currentMask = ViewModel.GetAffinity(pid);
        var currentCores = AffinityMask.ToCores(currentMask);

        // Build the dialog content: one CheckBox per logical core
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Select which logical cores (CPU 0–{coreCount - 1}) this process may use.",
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var checkBoxes = new CheckBox[coreCount];
        // Lay them out in a wrap panel — 4 per row
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

        // Validation: disable Apply when no core is checked
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

        // Wire Apply inside the dialog (closes it on success)
        applyButton.Click += async (_, _) =>
        {
            var selected = checkBoxes
                .Where(c => c.IsChecked == true)
                .Select(c => (int)c.Tag!)
                .ToArray();

            var mask = AffinityMask.FromCores(selected, coreCount);

            if (!AffinityMask.IsValid(mask, coreCount))
            {
                // Should not reach here due to the Apply guard, but defend anyway
                return;
            }

            var ok = ViewModel.SetAffinity(pid, mask);
            if (!ok)
            {
                // Show brief error inside the same dialog
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
}
