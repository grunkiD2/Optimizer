using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Diagnostics;
using Optimizer.WinUI.Services.Optimizations;
using Optimizer.WinUI.Services.Optimizations.Network;
using Optimizer.WinUI.Services.Optimizations.Performance;
using Optimizer.WinUI.Services.Optimizations.Storage;
using Optimizer.WinUI.Services.Optimizations.System;
using Optimizer.WinUI.ViewModels;
using Serilog;

namespace Optimizer.WinUI;

public partial class App : Application
{
    public static IHost AppHost { get; private set; } = null!;
    public static T GetService<T>() where T : class => AppHost.Services.GetRequiredService<T>();
    internal static IHost GetHost() => AppHost;

    private Window? _window;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static readonly string CrashLogPath = AppPaths.GetDataFile("crash.log");

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
                // Infrastructure
                services.AddSingleton<IPowerShellRunner, PowerShellRunner>();

                // Core services (from WPF port)
                services.AddSingleton<IElevationService, ElevationService>();
                services.AddSingleton<IUndoService, UndoService>();
                services.AddSingleton<IStartupService, StartupService>();
                services.AddSingleton<ProcessService>();
                services.AddSingleton<IProcessService>(sp => sp.GetRequiredService<ProcessService>());
                services.AddSingleton<SystemMonitorService>();
                services.AddSingleton<ISystemMonitorService>(sp => sp.GetRequiredService<SystemMonitorService>());

                // Optimization handlers (one per optimization)
                services.AddTransient<IOptimizationHandler, DisableBackgroundAppsHandler>();
                services.AddTransient<IOptimizationHandler, DisableAnimationsHandler>();
                services.AddTransient<IOptimizationHandler, DisableVisualEffectsHandler>();
                services.AddTransient<IOptimizationHandler, OptimizePowerSettingsHandler>();
                services.AddTransient<IOptimizationHandler, AdjustPageFileSizeHandler>();
                services.AddTransient<IOptimizationHandler, OptimizeNetworkSettingsHandler>();
                services.AddTransient<IOptimizationHandler, FlushDnsCacheHandler>();
                services.AddTransient<IOptimizationHandler, ClearTemporaryFilesHandler>();
                services.AddTransient<IOptimizationHandler, ClearWindowsUpdateCacheHandler>();
                services.AddTransient<IOptimizationHandler, DisableTelemetryHandler>();
                services.AddTransient<IOptimizationHandler, DisableConsumerFeaturesHandler>();
                services.AddTransient<IOptimizationHandler, DisableHibernationHandler>();
                services.AddTransient<IOptimizationHandler, DisableStartupProgramsHandler>();

                services.AddSingleton<IWindowsOptimizerService, WindowsOptimizerService>();

                // New services
                services.AddSingleton<INetworkConfigService, NetworkConfigService>();
                services.AddSingleton<IPrivacyService, PrivacyService>();
                services.AddSingleton<IThemeService, ThemeService>();
                services.AddSingleton<NavigationService>();
                services.AddSingleton<ISettingsService, SettingsService>();
                services.AddSingleton<SettingsService>(sp => (SettingsService)sp.GetRequiredService<ISettingsService>());
                services.AddSingleton<IProfileService, ProfileService>();
                services.AddSingleton<ProfileService>(sp => (ProfileService)sp.GetRequiredService<IProfileService>());
                services.AddSingleton<IHistoryService, HistoryService>();
                services.AddSingleton<HistoryService>(sp => (HistoryService)sp.GetRequiredService<IHistoryService>());
                services.AddSingleton<ITrayIconService, TrayIconService>();
                services.AddSingleton<IHardwareInfoService, HardwareInfoService>();
                services.AddSingleton<IDiskHealthService, DiskHealthService>();
                services.AddSingleton<IServiceManagerService, ServiceManagerService>();
                services.AddSingleton<IPowerService, PowerService>();
                services.AddSingleton<IBootAnalysisService, BootAnalysisService>();
                // Diagnostic plugins (one per diagnostic category)
                services.AddTransient<IDiagnosticPlugin, MemoryUsagePlugin>();
                services.AddTransient<IDiagnosticPlugin, DiskSmartPlugin>();
                services.AddTransient<IDiagnosticPlugin, DiskSpacePlugin>();
                services.AddTransient<IDiagnosticPlugin, PrivacyScorePlugin>();
                services.AddTransient<IDiagnosticPlugin, UptimePlugin>();
                services.AddTransient<IDiagnosticPlugin, BsodPlugin>();
                services.AddTransient<IDiagnosticPlugin, HibernationPlugin>();
                services.AddTransient<IDiagnosticPlugin, BootTimePlugin>();
                services.AddTransient<IDiagnosticPlugin, ServicesAuditPlugin>();
                services.AddTransient<IDiagnosticPlugin, HardwareSpecsPlugin>();

                services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
                services.AddSingleton<IRecommendationsService, RecommendationsService>();
                services.AddSingleton<IUpdateService, UpdateService>();
                services.AddSingleton<ISecurityService, SecurityService>();
                services.AddSingleton<INetworkSpeedTestService, NetworkSpeedTestService>();
                services.AddSingleton<IEventLogService, EventLogService>();
                services.AddSingleton<ICleanupService, CleanupService>();
                services.AddSingleton<IProfileAutomationService, ProfileAutomationService>();
                services.AddHostedService(sp => (ProfileAutomationService)sp.GetRequiredService<IProfileAutomationService>());
                services.AddSingleton<INotificationService, NotificationService>();
                services.AddSingleton<BackgroundMonitorService>();
                services.AddHostedService(sp => sp.GetRequiredService<BackgroundMonitorService>());
                services.AddSingleton<IReportService, ReportService>();
                services.AddSingleton<ITuningService, TuningService>();
                services.AddSingleton<ISystemRepairService, SystemRepairService>();
                services.AddSingleton<ISensorService, SensorService>();
                services.AddSingleton<IStressTestService, StressTestService>();
                services.AddSingleton<IDriverDiagnosticsService, DriverDiagnosticsService>();
                services.AddSingleton<IBottleneckDetectorService, BottleneckDetectorService>();
                services.AddSingleton<ISmartInsightsService, SmartInsightsService>();
                services.AddSingleton<IMarketplaceService, MarketplaceService>();
                services.AddSingleton<IIntelligenceService, IntelligenceService>();

                // Enterprise services
                services.AddSingleton<IFleetService, FleetService>();
                services.AddSingleton<ITemplatesService, TemplatesService>();
                services.AddSingleton<IComplianceService, ComplianceService>();

                // REST API host
                services.AddSingleton<IApiHostService>(sp =>
                    new ApiHostService(sp));

                // ViewModels
                services.AddTransient<OnboardingViewModel>();
                services.AddSingleton<DashboardViewModel>();
                services.AddTransient<PerformanceCategoryViewModel>();
                services.AddSingleton<NetworkCategoryViewModel>();
                services.AddTransient<StorageCategoryViewModel>();
                services.AddTransient<SystemCategoryViewModel>();
                services.AddTransient<StartupCategoryViewModel>();
                services.AddTransient<ProfilesViewModel>();
                services.AddTransient<HistoryViewModel>();
                services.AddSingleton<HardwareViewModel>();
                services.AddTransient<ServicesViewModel>();
                services.AddTransient<DiagnosticsViewModel>();
                services.AddTransient<RecommendationsViewModel>();
                services.AddTransient<UpdatesViewModel>();
                services.AddTransient<SecurityViewModel>();
                services.AddTransient<EventLogsViewModel>();
                services.AddTransient<ReportsViewModel>();
                services.AddTransient<TuningViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddTransient<MarketplaceViewModel>();
                services.AddTransient<FleetViewModel>();
                services.AddTransient<TemplatesViewModel>();
                services.AddTransient<ComplianceViewModel>();

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
            var logPath = AppPaths.GetDataFile("app.log");
            AppPaths.EnsureFolderExists();

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

            var settings = GetService<ISettingsService>();
            settings.Load();

            var historyService = GetService<IHistoryService>();
            historyService.Load();

            var profileService = GetService<IProfileService>();
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

            // Register toast notifications
            Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Register();

            // Start all IHostedService registrations (ProfileAutomationService, BackgroundMonitorService)
            _ = AppHost.StartAsync();

            // Ensure API token is populated (guards against empty value from old settings files)
            if (string.IsNullOrWhiteSpace(settings.Settings.ApiToken))
            {
                settings.Settings.ApiToken = Guid.NewGuid().ToString();
                settings.Save();
            }

            // Start embedded REST API if the user has opted in
            if (settings.Settings.ApiEnabled)
            {
                _ = GetService<IApiHostService>().StartAsync(
                    settings.Settings.ApiPort,
                    settings.Settings.ApiToken);
            }

            // Schedule ML model training in background after app settles
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMinutes(2));
                await GetService<IIntelligenceService>().TrainAsync();
            });

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
