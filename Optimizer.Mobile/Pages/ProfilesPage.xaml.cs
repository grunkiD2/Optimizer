using Optimizer.Mobile.ViewModels;

namespace Optimizer.Mobile.Pages;

public partial class ProfilesPage : ContentPage
{
    private ProfilesViewModel? _vm;

    public ProfilesPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        _vm = BindingContext as ProfilesViewModel;
        _vm?.LoadCommand.Execute(null);
    }
}
