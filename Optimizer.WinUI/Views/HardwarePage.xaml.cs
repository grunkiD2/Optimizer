using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class HardwarePage : Page
{
    public HardwareViewModel ViewModel { get; }

    public HardwarePage()
    {
        ViewModel = App.GetService<HardwareViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
        => await ViewModel.LoadAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await ViewModel.RefreshCommand.ExecuteAsync(null);

    private void Export_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExportToFileCommand.Execute(null);
}
