using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Controls;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class NetworkPage : Page
{
    public NetworkCategoryViewModel ViewModel { get; }
    private readonly Dictionary<string, EventHandler<bool>> _toggleHandlers = [];

    public NetworkPage()
    {
        ViewModel = App.GetService<NetworkCategoryViewModel>();
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
        => ViewModel.Load();

    private void OptimizationCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not OptimizationCard card || card.Tag is not string id) return;

        var model = ViewModel.Optimizations.FirstOrDefault(o => o.Id == id);
        if (model == null) return;

        card.LoadFromInfo(model.Info, model.IsActive, model.IsElevated);

        if (_toggleHandlers.TryGetValue(id, out var oldHandler))
            card.Toggled -= oldHandler;

        EventHandler<bool> handler = async (_, isOn) =>
            await ViewModel.ToggleOptimizationAsync(id, isOn);
        _toggleHandlers[id] = handler;
        card.Toggled += handler;
    }
}
