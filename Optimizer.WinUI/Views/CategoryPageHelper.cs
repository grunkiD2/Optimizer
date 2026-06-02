using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Controls;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

/// <summary>
/// Static helper that centralises the OptimizationCard toggle-subscription pattern
/// shared by PerformancePage, NetworkPage, StoragePage, and SystemPage.
/// Each page passes its own <c>_toggleHandlers</c> dictionary so lifecycle stays per-page.
/// </summary>
public static class CategoryPageHelper
{
    /// <summary>
    /// Wire a loaded <see cref="OptimizationCard"/> to the category ViewModel.
    /// Call from the page's <c>OptimizationCard_Loaded</c> event handler.
    /// </summary>
    public static void OnCardLoaded(
        object sender,
        XamlRoot xamlRoot,
        CategoryViewModelBase viewModel,
        Dictionary<string, EventHandler<bool>> toggleHandlers)
    {
        if (sender is not OptimizationCard card || card.Tag is not string id) return;

        var model = viewModel.Optimizations.FirstOrDefault(o => o.Id == id);
        if (model == null) return;

        card.LoadFromInfo(model.Info, model.IsActive, model.IsElevated);

        // Deregister previous handler to prevent duplicate subscriptions on card recycling
        if (toggleHandlers.TryGetValue(id, out var oldHandler))
            card.Toggled -= oldHandler;

        EventHandler<bool> handler = async (_, isOn) =>
        {
            try
            {
                if (isOn && ShouldConfirm())
                {
                    var ok = await DialogHelper.ConfirmAsync(
                        xamlRoot,
                        "Confirm Optimization",
                        $"Apply \"{model.Info.Title}\"?");
                    if (!ok) { card.IsActive = false; return; }
                }
                await viewModel.ToggleOptimizationAsync(id, isOn);
            }
            catch (Exception ex)
            {
                EngineLog.Error($"Toggle failed for {id}", ex);
            }
        };

        toggleHandlers[id] = handler;
        card.Toggled += handler;
    }

    private static bool ShouldConfirm()
    {
        try { return App.GetService<ISettingsService>().Settings.ConfirmBeforeApply; }
        catch { return false; }
    }
}
