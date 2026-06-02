using Optimizer.Mobile.Pages;
using Optimizer.Mobile.Services;

namespace Optimizer.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        RegisterRoutes();
    }

    private static void RegisterRoutes()
    {
        Routing.RegisterRoute(nameof(SetupPage), typeof(SetupPage));
        Routing.RegisterRoute(nameof(DashboardPage), typeof(DashboardPage));
        Routing.RegisterRoute(nameof(ProfilesPage), typeof(ProfilesPage));
        Routing.RegisterRoute(nameof(RecommendationsPage), typeof(RecommendationsPage));
        Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // If not configured, push the setup page modally
        var client = Handler?.MauiContext?.Services.GetService<ApiClient>();
        if (client != null && !client.IsConfigured)
        {
            await Shell.Current.GoToAsync($"//{nameof(SetupPage)}", animate: false);
        }
    }
}
