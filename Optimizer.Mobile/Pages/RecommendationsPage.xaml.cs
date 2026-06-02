using Optimizer.Mobile.ViewModels;

namespace Optimizer.Mobile.Pages;

public partial class RecommendationsPage : ContentPage
{
    private RecommendationsViewModel? _vm;

    public RecommendationsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        _vm = BindingContext as RecommendationsViewModel;
        _vm?.LoadCommand.Execute(null);
    }
}
