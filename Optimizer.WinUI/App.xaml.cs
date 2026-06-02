using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.ViewModels;
using Serilog;

namespace Optimizer.WinUI;

public partial class App : Application
{
    public static IHost AppHost { get; private set; } = null!;
    public static T GetService<T>() where T : class => AppHost.Services.GetRequiredService<T>();

    private Window? _window;

    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Optimizer", "crash.log");

    public App()
    {
        InitializeComponent();

        // Catch all unhandled WinUI exceptions and write to crash log
        UnhandledException += (_, e) =>
        {
            e.Handled = true;
            WriteCrashLog("UnhandledException", e.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            WriteCrashLog("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            WriteCrashLog("UnobservedTaskException", e.Exception);
        };

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
                services.AddSingleton<DashboardViewModel>();
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
        try
        {
            // Wire Serilog before any service usage so all engine messages are captured.
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Optimizer", "app.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

            var logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true)
                .CreateLogger();

            EngineLog.Configure((message, ex) =>
            {
                if (ex != null) logger.Error(ex, message);
                else logger.Information(message);
            });

            var settings = GetService<SettingsService>();
            settings.Load();

            var historyService = GetService<HistoryService>();
            historyService.Load();

            var profileService = GetService<ProfileService>();
            profileService.Load();

            GetService<IUndoService>().Load();

            _window = GetService<MainWindow>();
            _window.Activate();

            ThemeHelper.ApplyBackdrop(_window, settings.Settings.BackdropMaterial);
            if (_window.Content is FrameworkElement root)
                ThemeHelper.ApplyTheme(root, settings.Settings.Theme);
        }
        catch (Exception ex)
        {
            WriteCrashLog("OnLaunched", ex);
            throw;
        }
    }

    private static void WriteCrashLog(string context, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{context}]\n{ex}\n\n";
            File.AppendAllText(CrashLogPath, msg);
        }
        catch { /* cannot crash in the crash handler */ }
    }
}
