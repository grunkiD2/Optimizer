using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Optimizer.Contracts.Services;
using Optimizer.Contracts.Views;
using Optimizer.Models;
using Optimizer.Services;
using Optimizer.ViewModels;
using Optimizer.Views;

namespace Optimizer
{
    // For more inforation about application lifecyle events see https://docs.microsoft.com/dotnet/framework/wpf/app-development/application-management-overview

    // WPF UI elements use language en-US by default.
    // If you need to support other cultures make sure you add converters and review dates and numbers in your UI to ensure everything adapts correctly.
    // Tracking issue for improving this is https://github.com/dotnet/wpf/issues/1946
    public partial class App : Application
    {
        private IHost _host;

        public T GetService<T>()
            where T : class
            => _host.Services.GetService(typeof(T)) as T;

        public App()
        {
            Helpers.AppLogging.Initialize();

            // License key is resolved from the SYNCFUSION_LICENSE_KEY env var or a local
            // (untracked) license.key file — never hard-coded here. See Helpers/LicenseConfig.
            var licenseKey = Helpers.LicenseConfig.GetSyncfusionKey();
            if (!string.IsNullOrWhiteSpace(licenseKey))
            {
                Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(licenseKey);
            }
        }

        private async void OnStartup(object sender, StartupEventArgs e)
        {
            var appLocation = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            // For more information about .NET generic host see  https://docs.microsoft.com/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-3.0
            _host = Host.CreateDefaultBuilder(e.Args)
                    .ConfigureAppConfiguration(c =>
                    {
                        c.SetBasePath(appLocation);
                    })
                    .ConfigureServices(ConfigureServices)
                    .Build();

            // Headless mode for scheduled tasks: apply a profile and exit, no UI.
            var applyArg = Array.Find(e.Args, a => a.StartsWith("--apply-profile=", StringComparison.OrdinalIgnoreCase));
            if (applyArg != null)
            {
                var profileId = applyArg.Substring("--apply-profile=".Length);
                try
                {
                    var optimizer = _host.Services.GetService(typeof(WindowsOptimizer.Services.IWindowsOptimizerService))
                        as WindowsOptimizer.Services.IWindowsOptimizerService;
                    if (optimizer != null) await optimizer.ApplyProfileAsync(profileId);
                    Serilog.Log.Information("Headless applied profile {Id}", profileId);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "Headless apply failed for {Id}", profileId);
                }
                Shutdown();
                return;
            }

            var settings = _host.Services.GetService(typeof(Optimizer.Services.ISettingsService)) as Optimizer.Services.ISettingsService;

            // Self-elevate the unpackaged desktop exe when requested (MSIX builds stay asInvoker).
            if (settings?.Settings.AlwaysRunAsAdmin == true && !Helpers.PackageInfo.IsPackaged())
            {
                var elevation = _host.Services.GetService(typeof(WindowsOptimizer.Services.IElevationService))
                    as WindowsOptimizer.Services.IElevationService;
                if (elevation is { IsElevated: false } && elevation.TryRelaunchElevated())
                {
                    Shutdown(); // an elevated instance is starting; exit this one
                    return;
                }
            }

            // Apply the saved theme before the shell window is created (ShellWindow reads this).
            Current.Properties["Theme"] = settings?.Settings.Theme ?? "FluentLight";

            await _host.StartAsync();

            // First-run onboarding.
            if (settings != null && !settings.Settings.HasSeenOnboarding)
            {
                try
                {
                    new Views.OnboardingWindow().ShowDialog();
                }
                catch { /* non-fatal */ }
                settings.Settings.HasSeenOnboarding = true;
                settings.Save();
            }
        }

        private void ConfigureServices(HostBuilderContext context, IServiceCollection services)
        {
            // TODO WTS: Register your services, viewmodels and pages here

            // App Host
            services.AddHostedService<ApplicationHostService>();

            // Core Services
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<TrayIconService>();
            services.AddSingleton<ITrayIconService>(sp => sp.GetRequiredService<TrayIconService>());
            services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<TrayIconService>());
            services.AddSingleton<ISchedulerService, SchedulerService>();
            services.AddSingleton<IContextSwitchService, ContextSwitchService>();
            services.AddSingleton<IUpdateService, UpdateService>();

            // Optimization engine
            services.AddSingleton<WindowsOptimizer.Services.IElevationService, WindowsOptimizer.Services.ElevationService>();
            services.AddSingleton<WindowsOptimizer.Services.IUndoService, WindowsOptimizer.Services.UndoService>();
            services.AddSingleton<WindowsOptimizer.Services.SystemMonitorService>();
            services.AddSingleton<WindowsOptimizer.Services.IProcessService, WindowsOptimizer.Services.ProcessService>();
            services.AddSingleton<WindowsOptimizer.Services.IStartupService, WindowsOptimizer.Services.StartupService>();
            services.AddSingleton<WindowsOptimizer.Services.IWindowsOptimizerService, WindowsOptimizer.Services.WindowsOptimizerService>();

            // Services
            services.AddSingleton<IWindowManagerService, WindowManagerService>();
            services.AddSingleton<IRightPaneService, RightPaneService>();
            services.AddSingleton<IPageService, PageService>();
            services.AddSingleton<INavigationService, NavigationService>();

            // Views and ViewModels
            services.AddTransient<IShellWindow, ShellWindow>();
            services.AddTransient<ShellViewModel>();

            services.AddTransient<MainViewModel>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<ProfilesViewModel>();
            services.AddTransient<HistoryViewModel>();
            services.AddTransient<StartupViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<MainPage>();

            services.AddTransient<MenuAdvViewModel>();
            services.AddTransient<MenuAdvPage>();

            services.AddTransient<IShellDialogWindow, ShellDialogWindow>();
            services.AddTransient<ShellDialogViewModel>();

            // ViewModel Locator factory pattern
            services.AddSingleton<IViewModelLocator, ViewModelLocator>();

            // Configuration
            services.Configure<AppConfig>(context.Configuration.GetSection(nameof(AppConfig)));
        }

        private async void OnExit(object sender, ExitEventArgs e)
        {
            await _host.StopAsync();
            _host.Dispose();
            _host = null;
            Helpers.AppLogging.Shutdown();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Serilog.Log.Error(e.Exception, "Unhandled dispatcher exception");
        }
    }
}
