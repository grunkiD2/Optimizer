using Optimizer.Mobile.ViewModels;

namespace Optimizer.Mobile.Pages;

public partial class SettingsPage : ContentPage
{
    private SettingsViewModel? _vm;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        _vm = BindingContext as SettingsViewModel;
        _vm?.Refresh();
    }
}
