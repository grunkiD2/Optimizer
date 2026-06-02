using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI;

public partial class App : Application
{
    public static IHost AppHost { get; private set; } = null!;
    public static T GetService<T>() where T : class => AppHost.Services.GetRequiredService<T>();

    private Window? _window;

    public App()
    {
        InitializeComponent();

        AppHost = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // Core services (from WPF port)
                services.AddSingleton<IElevationService, ElevationService>();
                services.AddSingleton<IUndoService, UndoService>();
                services.AddSingleton<IStartupService, StartupService>();
                services.AddSingleton<IProcessService, ProcessService>();
                services.AddSingleton<SystemMonitorService>();
                services.AddSingleton<IWindowsOptimizerService, WindowsOptimizerService>();

                // New services
                services.AddSingleton<NavigationService>();
                services.AddSingleton<SettingsService>();
                services.AddSingleton<ProfileService>();
                services.AddSingleton<HistoryService>();

                // ViewModels
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<PerformanceCategoryViewModel>();
                services.AddTransient<NetworkCategoryViewModel>();
                services.AddTransient<StorageCategoryViewModel>();
                services.AddTransient<SystemCategoryViewModel>();
                services.AddTransient<StartupCategoryViewModel>();
                services.AddTransient<ProfilesViewModel>();
                services.AddTransient<HistoryViewModel>();
                services.AddTransient<SettingsViewModel>();

                // MainWindow (registered as singleton so DI can inject it)
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settings = GetService<SettingsService>();
        settings.Load();

        var historyService = GetService<HistoryService>();
        historyService.Load();

        var profileService = GetService<ProfileService>();
        profileService.Load();

        var undoService = GetService<IUndoService>() as UndoService;
        undoService?.Load();

        _window = GetService<MainWindow>();
        _window.Activate();

        ThemeHelper.ApplyBackdrop(_window, settings.Settings.BackdropMaterial);
        if (_window.Content is FrameworkElement root)
            ThemeHelper.ApplyTheme(root, settings.Settings.Theme);
    }
}
