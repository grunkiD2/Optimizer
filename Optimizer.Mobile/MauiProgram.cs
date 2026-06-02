using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
using Optimizer.Mobile.Services;
using Optimizer.Mobile.ViewModels;

namespace Optimizer.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Core services
        builder.Services.AddSingleton<ApiClient>();

        // ViewModels (transient so each page gets a fresh one)
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<ProfilesViewModel>();
        builder.Services.AddTransient<RecommendationsViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

        // Pages
        builder.Services.AddTransient<Pages.DashboardPage>();
        builder.Services.AddTransient<Pages.ProfilesPage>();
        builder.Services.AddTransient<Pages.RecommendationsPage>();
        builder.Services.AddTransient<Pages.SettingsPage>();
        builder.Services.AddTransient<Pages.SetupPage>();

        return builder.Build();
    }
}
