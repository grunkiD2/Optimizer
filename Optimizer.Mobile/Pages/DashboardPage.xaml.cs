using Optimizer.Mobile.ViewModels;

namespace Optimizer.Mobile.Pages;

public partial class DashboardPage : ContentPage
{
    private DashboardViewModel? _vm;

    public DashboardPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        _vm = BindingContext as DashboardViewModel;
        _vm?.RefreshCommand.Execute(null);
    }
}
