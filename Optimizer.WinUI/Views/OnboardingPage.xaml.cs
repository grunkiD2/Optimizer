using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class OnboardingPage : Page
{
    public OnboardingViewModel ViewModel { get; }

    public OnboardingPage()
    {
        ViewModel = App.GetService<OnboardingViewModel>();
        InitializeComponent();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
        => ViewModel.NextCommand.Execute(null);

    private void Back_Click(object sender, RoutedEventArgs e)
        => ViewModel.BackCommand.Execute(null);

    private async void Finish_Click(object sender, RoutedEventArgs e)
        => await ViewModel.FinishCommand.ExecuteAsync(null);

    private void Skip_Click(object sender, RoutedEventArgs e)
        => ViewModel.SkipCommand.Execute(null);

    private void UsageOption_Clicked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
            ViewModel.SelectedUsage = tag;
    }

    private void PrivacyOption_Clicked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
            ViewModel.SelectedPrivacy = tag;
    }
}
