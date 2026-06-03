using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Assistant;
using Optimizer.WinUI.Services.Cloud;
using Optimizer.WinUI.Services.Data;
using Optimizer.WinUI.Services.Diagnostics;
using Optimizer.WinUI.Services.Plugins;
using Optimizer.WinUI.Services.Optimizations;
using Optimizer.WinUI.Services.Optimizations.Network;
using Optimizer.WinUI.Services.Optimizations.Performance;
using Optimizer.WinUI.Services.Optimizations.Storage;
using Optimizer.WinUI.Services.Optimizations.System;
using Optimizer.WinUI.Services.Events;
using Optimizer.WinUI.Services.Gpu;
using Optimizer.WinUI.ViewModels;
using Serilog;

namespace Optimizer.WinUI;

public partial class App : Application
{
    public static IHost AppHost { get; private set; } = null!;
    public static T GetService<T>() where T : class => AppHost.Services.GetRequiredService<T>();
    internal static IHost GetHost() => AppHost;

    /// <summary>The main window's UI-thread dispatcher, captured at MainWindow construction.
    /// Used to marshal background-thread events (e.g. event-bus publishes) onto the UI thread.</summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue? UiDispatcher { get; set; }

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
                services.AddSingleton<IWmiQueryService, WmiQueryService>();

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
                // Note: BackgroundMonitorService now subscribes to ISystemDataBus instead of polling directly.
                services.AddSingleton<IReportService, ReportService>();
                services.AddSingleton<ITuningService, TuningService>();
                services.AddSingleton<ISystemRepairService, SystemRepairService>();
                services.AddSingleton<ISensorService, SensorService>();
                services.AddSingleton<IStressTestService, StressTestService>();
                // GPU control backends (registered in priority order: NVAPI, ADL, Null)
                services.AddSingleton<IGpuControlBackend, NvApiGpuBackend>();
                services.AddSingleton<IGpuControlBackend, AdlGpuBackend>();
                services.AddSingleton<IGpuControlBackend, NullGpuBackend>();
                services.AddSingleton<IGpuControlService, GpuControlService>();
                services.AddSingleton<IDeviceControlService, DeviceControlService>();
                services.AddSingleton<IDriverDiagnosticsService, DriverDiagnosticsService>();
                services.AddSingleton<IBottleneckDetectorService, BottleneckDetectorService>();
                services.AddSingleton<ISmartInsightsService, SmartInsightsService>();
                services.AddSingleton<ISystemDataBus, SystemDataBus>();
                services.AddHostedService(sp => (SystemDataBus)sp.GetRequiredService<ISystemDataBus>());
                services.AddSingleton<IManifestParser, ManifestParser>();
                services.AddSingleton<IDeclarativeChangeExecutor, DeclarativeChangeExecutor>();
                services.AddSingleton<IPluginLoader, PluginLoader>();
                services.AddSingleton<IPluginVerifier, PluginVerifier>();
                services.AddSingleton<IMarketplaceService, MarketplaceService>();
                services.AddSingleton<IIntelligenceService, IntelligenceService>();
                services.AddSingleton<ITrendHistoryService, TrendHistoryService>();
                services.AddSingleton<IPredictiveMaintenanceService, PredictiveMaintenanceService>();

                // Enterprise services
                services.AddSingleton<IFleetService, FleetService>();
                services.AddSingleton<ITemplatesService, TemplatesService>();
                services.AddSingleton<IComplianceService, ComplianceService>();

                // Event bus (depends on IOptimizerCloudClient — registered just below)
                services.AddSingleton<IOptimizerCloudClient, OptimizerCloudClient>();
                services.AddSingleton<IEventBus, EventBus>();

                // Cloud sync
                services.AddSingleton<ISyncTombstoneCollector, SyncTombstoneCollector>();
                services.AddSingleton<ICloudSyncOrchestrator, CloudSyncOrchestrator>();
                // Federated learning scaffold (opt-in, DP-protected)
                services.AddSingleton<IFederatedClient, FederatedClient>();

                // ── Phase 1: SQLite Database + Learning Foundation ──
                // Database infrastructure
                services.AddSingleton<DatabaseService>();

                // Phase 4: Granular change sets with before/after snapshots
                services.AddSingleton<IChangeSetService, ChangeSetService>();

                // Phase 5: Scheduled optimizations (hosted background loop)
                services.AddSingleton<IScheduledOptimizationService, ScheduledOptimizationService>();
                services.AddHostedService(sp =>
                    (ScheduledOptimizationService)sp.GetRequiredService<IScheduledOptimizationService>());

                // Assistant learning services
                services.AddSingleton<IAssistantActionLogger, AssistantActionLogger>();
                services.AddSingleton<ISessionPersistence, SessionPersistence>();
                services.AddSingleton<IContextDetectionService, ContextDetectionService>();

                // Phase 2: Analytics, pattern recognition, feedback
                services.AddSingleton<Optimizer.WinUI.Services.Analytics.IActionAnalyticsService,
                                      Optimizer.WinUI.Services.Analytics.ActionAnalyticsService>();
                services.AddSingleton<Optimizer.WinUI.Services.Analytics.IPatternExtractionService,
                                      Optimizer.WinUI.Services.Analytics.PatternExtractionService>();
                services.AddSingleton<Optimizer.WinUI.Services.Analytics.IAssistantFeedbackService,
                                      Optimizer.WinUI.Services.Analytics.AssistantFeedbackService>();

                // Phase 3: Context-aware profiles & automation learning
                services.AddSingleton<Optimizer.WinUI.Services.Analytics.IProfileContextService,
                                      Optimizer.WinUI.Services.Analytics.ProfileContextService>();
                services.AddSingleton<Optimizer.WinUI.Services.Analytics.IRuleSuggestionService,
                                      Optimizer.WinUI.Services.Analytics.RuleSuggestionService>();
                services.AddSingleton<Optimizer.WinUI.Services.Analytics.IRecommendationRanker,
                                      Optimizer.WinUI.Services.Analytics.RecommendationRanker>();

                // Phase 6: Intelligence — anomaly detection & predictive alerts
                services.AddSingleton<Optimizer.WinUI.Services.Analytics.IAnomalyDetector,
                                      Optimizer.WinUI.Services.Analytics.AnomalyDetector>();
                services.AddSingleton<Optimizer.WinUI.Services.Analytics.IPredictiveAlertService,
                                      Optimizer.WinUI.Services.Analytics.PredictiveAlertService>();

                // Phase 7: Autonomous context switching & full automation (opt-in)
                services.AddSingleton<Optimizer.WinUI.Services.Analytics.IAutoApplyPolicy,
                                      Optimizer.WinUI.Services.Analytics.AutoApplyPolicy>();
                services.AddSingleton<Optimizer.WinUI.Services.Analytics.IContextStateManager,
                                      Optimizer.WinUI.Services.Analytics.ContextStateManager>();
                services.AddSingleton<Optimizer.WinUI.Services.Analytics.ContextAutomationService>();
                services.AddHostedService(sp =>
                    sp.GetRequiredService<Optimizer.WinUI.Services.Analytics.ContextAutomationService>());

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
                services.AddTransient<LearningDashboardViewModel>();
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
                services.AddTransient<PluginsViewModel>();
                services.AddTransient<DevicesViewModel>();
                services.AddTransient<FleetViewModel>();
                services.AddTransient<TemplatesViewModel>();
                services.AddTransient<ComplianceViewModel>();

                // ── AI Assistant: key store, Claude client, settings, orchestration ──
                services.AddSingleton<Optimizer.WinUI.Services.Assistant.IApiKeyStore,
                                      Optimizer.WinUI.Services.Assistant.DpapiApiKeyStore>();
                services.AddSingleton<Optimizer.WinUI.Services.Assistant.IAssistantSettings,
                                      Optimizer.WinUI.Services.Assistant.AssistantSettings>();
                services.AddSingleton<Optimizer.WinUI.Services.Assistant.IClaudeClient,
                                      Optimizer.WinUI.Services.Assistant.ClaudeClient>();
                services.AddSingleton<Optimizer.WinUI.Services.Assistant.IContextualPromptBuilder,
                                      Optimizer.WinUI.Services.Assistant.ContextualPromptBuilder>();
                services.AddSingleton<Optimizer.WinUI.Services.Assistant.IAssistantService,
                                      Optimizer.WinUI.Services.Assistant.AssistantService>();

                // ── Command registry + commands (assistant tool surface) ──
                services.AddSingleton<Optimizer.WinUI.Services.Commands.PageNavigator>();
                services.AddSingleton<Optimizer.WinUI.Services.Commands.IPageNavigator>(
                    sp => sp.GetRequiredService<Optimizer.WinUI.Services.Commands.PageNavigator>());

                services.AddSingleton<Optimizer.WinUI.Services.Commands.IAppCommand, Optimizer.WinUI.Services.Commands.GetMetricsCommand>();
                services.AddSingleton<Optimizer.WinUI.Services.Commands.IAppCommand, Optimizer.WinUI.Services.Commands.GetRecommendationsCommand>();
                services.AddSingleton<Optimizer.WinUI.Services.Commands.IAppCommand, Optimizer.WinUI.Services.Commands.RunDiagnosticsScanCommand>();
                services.AddSingleton<Optimizer.WinUI.Services.Commands.IAppCommand, Optimizer.WinUI.Services.Commands.GetBottlenecksCommand>();
                services.AddSingleton<Optimizer.WinUI.Services.Commands.IAppCommand, Optimizer.WinUI.Services.Commands.GetStartupItemsCommand>();
                services.AddSingleton<Optimizer.WinUI.Services.Commands.IAppCommand, Optimizer.WinUI.Services.Commands.SetStartupItemCommand>();
                services.AddSingleton<Optimizer.WinUI.Services.Commands.IAppCommand, Optimizer.WinUI.Services.Commands.ListProfilesCommand>();
                services.AddSingleton<Optimizer.WinUI.Services.Commands.IAppCommand, Optimizer.WinUI.Services.Commands.NavigateToPageCommand>();
                services.AddSingleton<Optimizer.WinUI.Services.Commands.IAppCommand, Optimizer.WinUI.Services.Commands.ApplyProfileCommand>();
                services.AddSingleton<Optimizer.WinUI.Services.Commands.IAppCommand, Optimizer.WinUI.Services.Commands.ApplyOptimizationCommand>();
                services.AddSingleton<Optimizer.WinUI.Services.Commands.IAppCommand, Optimizer.WinUI.Services.Commands.RunCleanupCommand>();
                services.AddSingleton<Optimizer.WinUI.Services.Commands.IAppCommand, Optimizer.WinUI.Services.Commands.UndoLastCommand>();

                services.AddSingleton<Optimizer.WinUI.Services.Commands.ICommandRegistry>(sp =>
                {
                    var reg = new Optimizer.WinUI.Services.Commands.CommandRegistry();
                    foreach (var c in sp.GetServices<Optimizer.WinUI.Services.Commands.IAppCommand>())
                        reg.Register(c);
                    return reg;
                });

                // ── Console + Assistant ViewModels ──
                // Marshal onto the captured UI dispatcher. Event-bus publishes fire on
                // background threads where GetForCurrentThread() is null, so we must use
                // the dispatcher captured on the UI thread (App.UiDispatcher) or lines drop.
                static void OnUi(System.Action a)
                {
                    var dq = UiDispatcher;
                    if (dq != null) dq.TryEnqueue(() => a());
                    else a();
                }
                services.AddSingleton(sp => new Optimizer.WinUI.ViewModels.ConsoleViewModel(
                    sp.GetRequiredService<Optimizer.WinUI.Services.Events.IEventBus>(),
                    OnUi,
                    sp.GetRequiredService<ISettingsService>()));
                services.AddSingleton(sp => new Optimizer.WinUI.ViewModels.AssistantViewModel(
                    sp.GetRequiredService<Optimizer.WinUI.Services.Assistant.IAssistantService>(),
                    sp.GetRequiredService<Optimizer.WinUI.Services.Assistant.IApiKeyStore>(),
                    sp.GetRequiredService<Optimizer.WinUI.Services.Assistant.ISessionPersistence>(),
                    sp.GetRequiredService<Optimizer.WinUI.Services.Analytics.IAssistantFeedbackService>(),
                    OnUi));

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

            // Initialize SQLite database (Phase 1)
            var dbService = GetService<DatabaseService>();
            dbService.InitializeAsync().GetAwaiter().GetResult();

            var historyService = GetService<IHistoryService>();
            historyService.Load();

            var profileService = GetService<IProfileService>();
            profileService.Load();

            GetService<IUndoService>().Load();

            // Discover installed plugins and merge them into the optimizer service
            var pluginLoader = GetService<IPluginLoader>();
            pluginLoader.Reload();
            ((WindowsOptimizerService)GetService<IWindowsOptimizerService>()).RefreshHandlers();

            _window = GetService<MainWindow>();
            _window.Activate();
            // Maximize after activation so it reliably sticks (constructor-time maximize doesn't).
            (_window as MainWindow)?.ApplyStartupWindowState();

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

            // Start embedded REST API if the user has opted in. In DEBUG builds we auto-start it
            // regardless so local dev/smoke-testing can hit the API without flipping a setting —
            // crucially WITHOUT persisting ApiEnabled=true, so Release builds stay opt-in (the
            // listener exposes mutating endpoints and the token lives in plaintext settings).
            var startApi = settings.Settings.ApiEnabled;
#if DEBUG
            if (!startApi)
            {
                startApi = true;
                EngineLog.Write($"[DEBUG] Auto-starting REST API on port {settings.Settings.ApiPort} " +
                                "(ApiEnabled remains false on disk; Release builds stay opt-in).");
            }
#endif
            if (startApi)
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

            // Record daily trend sample (drive space + SMART) — fire and forget
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15)); // let services settle
                    await GetService<ITrendHistoryService>().RecordSampleAsync();
                }
                catch (Exception ex)
                {
                    WriteCrashLog("TrendHistoryService.RecordSampleAsync", ex);
                }
            });

            // Federated learning scaffold (opt-in, DP-protected).
            // Only runs when the user has explicitly enabled the feature.
            // Uploads only differentially-private aggregates; never raw data.
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30)); // let auth session restore
                    await GetService<IFederatedClient>().SyncAsync();
                }
                catch (Exception ex)
                {
                    WriteCrashLog("FederatedClient.SyncAsync", ex);
                }
            });

            // Phase 2: Recalculate analytics + re-mine learned patterns from the
            // action log after the app settles. Cheap, local, fire-and-forget.
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1));
                    await GetService<Optimizer.WinUI.Services.Analytics.IActionAnalyticsService>()
                        .RecalculateMetricsAsync();
                    await GetService<Optimizer.WinUI.Services.Analytics.IPatternExtractionService>()
                        .ExtractPatternsAsync();
                }
                catch (Exception ex)
                {
                    WriteCrashLog("Analytics/PatternExtraction", ex);
                }
            });

            // Phase 3: Resolve "did the profile stick?" verdicts and (re)generate
            // automation-rule suggestions from observed behavior. Runs periodically.
            _ = Task.Run(async () =>
            {
                var stop = GetService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>().ApplicationStopping;
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(45), stop);
                    var profileContext = GetService<Optimizer.WinUI.Services.Analytics.IProfileContextService>();
                    var ruleSuggest = GetService<Optimizer.WinUI.Services.Analytics.IRuleSuggestionService>();
                    while (!stop.IsCancellationRequested)
                    {
                        await profileContext.ResolvePendingAsync(TimeSpan.FromMinutes(30));
                        await ruleSuggest.GenerateSuggestionsAsync();
                        await Task.Delay(TimeSpan.FromMinutes(10), stop);
                    }
                }
                catch (OperationCanceledException) { /* app shutting down */ }
                catch (Exception ex)
                {
                    WriteCrashLog("ProfileContext/RuleSuggestion", ex);
                }
            });

            // Phase 6: Learn metric baselines per context, flag anomalies, and raise
            // predictive-maintenance alerts. Samples every 2 minutes; alerts hourly.
            _ = Task.Run(async () =>
            {
                var stop = GetService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>().ApplicationStopping;
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(2), stop);
                    var detector = GetService<Optimizer.WinUI.Services.Analytics.IAnomalyDetector>();
                    var contextDetect = GetService<IContextDetectionService>();
                    var monitor = GetService<ISystemMonitorService>();
                    var predictive = GetService<Optimizer.WinUI.Services.Analytics.IPredictiveAlertService>();

                    var ticks = 0;
                    while (!stop.IsCancellationRequested)
                    {
                        var context = await contextDetect.DetectContextAsync();
                        var snap = monitor.CollectSnapshot();
                        var cpu = snap.CpuUsagePercentage;
                        var memUsedPct = snap.TotalPhysicalMemory > 0
                            ? 100.0 * (snap.TotalPhysicalMemory - snap.AvailablePhysicalMemory) / snap.TotalPhysicalMemory
                            : 0;

                        await detector.RecordSampleAsync(context, "cpu", cpu);
                        await detector.RecordSampleAsync(context, "memory", memUsedPct);
                        await detector.EvaluateAsync(context, new Dictionary<string, double>
                        {
                            ["cpu"] = cpu,
                            ["memory"] = memUsedPct
                        });

                        // Predictive maintenance check once per ~30 ticks (~hourly).
                        if (ticks % 30 == 0)
                            await predictive.EvaluateAsync();
                        ticks++;

                        await Task.Delay(TimeSpan.FromMinutes(2), stop);
                    }
                }
                catch (OperationCanceledException) { /* app shutting down */ }
                catch (Exception ex)
                {
                    WriteCrashLog("AnomalyDetector/PredictiveAlerts", ex);
                }
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
