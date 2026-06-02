using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Controls;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class PerformancePage : Page
{
    public PerformanceCategoryViewModel ViewModel { get; }

    public PerformancePage()
    {
        ViewModel = App.GetService<PerformanceCategoryViewModel>();
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
        => ViewModel.Load();

    private void OptimizationCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is OptimizationCard card && card.Tag is string id)
        {
            var model = ViewModel.Optimizations.FirstOrDefault(o => o.Id == id);
            if (model != null)
            {
                card.LoadFromInfo(model.Info, model.IsActive, model.IsElevated);
                card.Toggled += async (_, isOn) =>
                    await ViewModel.ToggleOptimizationAsync(id, isOn);
            }
        }
    }
}
