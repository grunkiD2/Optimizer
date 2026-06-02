using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.ViewModels;
using Serilog;

namespace Optimizer.WinUI;

public partial class App : Application
{
    public static IHost AppHost { get; private set; } = null!;
    public static T GetService<T>() where T : class => AppHost.Services.GetRequiredService<T>();

    private Window? _window;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

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
                services.AddSingleton<ProcessService>();
                services.AddSingleton<IProcessService>(sp => sp.GetRequiredService<ProcessService>());
                services.AddSingleton<SystemMonitorService>();
                services.AddSingleton<IWindowsOptimizerService, WindowsOptimizerService>();

                // New services
                services.AddSingleton<INetworkConfigService, NetworkConfigService>();
                services.AddSingleton<IPrivacyService, PrivacyService>();
                services.AddSingleton<IThemeService, ThemeService>();
                services.AddSingleton<NavigationService>();
                services.AddSingleton<SettingsService>();
                services.AddSingleton<ProfileService>();
                services.AddSingleton<HistoryService>();
                services.AddSingleton<ITrayIconService, TrayIconService>();
                services.AddSingleton<IHardwareInfoService, HardwareInfoService>();
                services.AddSingleton<IDiskHealthService, DiskHealthService>();
                services.AddSingleton<IServiceManagerService, ServiceManagerService>();
                services.AddSingleton<IPowerService, PowerService>();
                services.AddSingleton<IBootAnalysisService, BootAnalysisService>();
                services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
                services.AddSingleton<IRecommendationsService, RecommendationsService>();
                services.AddSingleton<IUpdateService, UpdateService>();
                services.AddSingleton<ISecurityService, SecurityService>();
                services.AddSingleton<INetworkSpeedTestService, NetworkSpeedTestService>();
                services.AddSingleton<IEventLogService, EventLogService>();
                services.AddSingleton<ICleanupService, CleanupService>();
                services.AddSingleton<IProfileAutomationService, ProfileAutomationService>();

                // ViewModels
                services.AddSingleton<DashboardViewModel>();
                services.AddTransient<PerformanceCategoryViewModel>();
                services.AddTransient<NetworkCategoryViewModel>();
                services.AddTransient<StorageCategoryViewModel>();
                services.AddTransient<SystemCategoryViewModel>();
                services.AddTransient<StartupCategoryViewModel>();
                services.AddTransient<ProfilesViewModel>();
                services.AddTransient<HistoryViewModel>();
                services.AddTransient<HardwareViewModel>();
                services.AddTransient<ServicesViewModel>();
                services.AddTransient<DiagnosticsViewModel>();
                services.AddTransient<RecommendationsViewModel>();
                services.AddTransient<UpdatesViewModel>();
                services.AddTransient<SecurityViewModel>();
                services.AddTransient<EventLogsViewModel>();
                services.AddSingleton<SettingsViewModel>();

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

            var themeService = GetService<IThemeService>();
            themeService.Initialize(_window);
            themeService.ApplyBackdrop(settings.Settings.BackdropMaterial);
            themeService.ApplyTheme(settings.Settings.Theme);

            // Initialize system tray icon
            var trayService = GetService<ITrayIconService>();
            trayService.Initialize(_window);

            // Start smart profile automation
            GetService<IProfileAutomationService>().Start();

            // Honor StartMinimized — hide window immediately after activation
            if (settings.Settings.StartMinimized)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
                ShowWindow(hwnd, 6); // SW_MINIMIZE = 6
                // Also hide from taskbar so it goes fully to tray
                _window.AppWindow.Hide();
            }
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
