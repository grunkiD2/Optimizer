using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
}
