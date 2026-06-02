# Optimizer

A Windows system optimizer built with WinUI 3 and .NET 10. It provides a modern, Material-style
dashboard for monitoring system health, applying performance tweaks, managing startup programs,
and maintaining an undo/redo history of every change made.

## Prerequisites

| Requirement | Version |
|-------------|---------|
| .NET SDK | 10.0 or later |
| Windows App SDK | 1.5 or later (NuGet reference, restored automatically) |
| OS | Windows 10 22H2 (build 19045) or later |
| Visual Studio | 2022 17.9+ with the *Windows application development* workload, **or** VS Code with the C# Dev Kit extension |

> Some optimizations (e.g. power plan changes, telemetry registry keys) require the app to be run
> as **Administrator**. The elevation prompt is automatic when needed; the UI shows a shield badge
> on cards that require admin rights.

## Building

```powershell
# Restore packages and build the WinUI project (Debug/x64)
dotnet build Optimizer.WinUI/Optimizer.WinUI.csproj -c Debug -r win-x64

# Release build with self-contained output
dotnet publish Optimizer.WinUI/Optimizer.WinUI.csproj -c Release -r win-x64 --self-contained
```

The output lands in `Optimizer.WinUI/bin/<Configuration>/net10.0-windows10.0.19041.0/win-x64/`.

## Running

```powershell
# After building, run the executable directly:
.\Optimizer.WinUI\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Optimizer.WinUI.exe
```

Or press **F5** inside Visual Studio / VS Code to launch with the debugger attached.

Application data (settings, history, snapshots, crash logs) is stored at:
```
%LOCALAPPDATA%\Optimizer\
```

## Project Structure

```
Optimizer.WinUI/
  App.xaml / App.xaml.cs       Host startup, DI container, crash handler
  MainWindow.xaml(.cs)         Shell window with NavigationView
  Controls/
    OptimizationCard.xaml(.cs) Reusable toggle card for each optimization
  Converters/                  IValueConverter implementations (XAML bindings)
  Helpers/
    ByteFormatter.cs           Shared bytes-to-string formatting
    DialogHelper.cs            Shared ContentDialog helper
    ThemeHelper.cs             Mica/Acrylic backdrop + dark/light theme
  Models/
    AppSettings.cs             Persisted user preferences
    OptimizationIds.cs         Compile-time constants for optimization IDs
    SettingsProfile.cs         Snapshot/preset data model
  Services/
    HistoryService.cs          Change log: applied / undone events
    IElevationService.cs       Admin elevation check
    IWindowsOptimizerService   Engine abstraction
    ProfileService.cs          Built-in presets + user snapshots
    SettingsService.cs         Load/save AppSettings JSON
    SystemMonitorService.cs    CPU/GPU/memory/disk/network metrics
    WindowsOptimizerService.cs Real Windows registry/system calls
  ViewModels/
    DashboardViewModel.cs      Live metrics, health score, quick actions
    CategoryViewModelBase.cs   Base for optimization category pages
    Performance/Network/       Category-specific ViewModels
      Storage/SystemCategoryViewModel.cs
    ProfilesViewModel.cs       Preset + snapshot management
    HistoryViewModel.cs        Day-grouped change log
    SettingsViewModel.cs       Settings load/save with live theme apply
  Views/
    DashboardPage.xaml(.cs)    Main metrics dashboard
    PerformancePage.xaml(.cs)  Performance tweaks
    NetworkPage.xaml(.cs)      Network tweaks
    StoragePage.xaml(.cs)      Storage cleanup
    SystemPage.xaml(.cs)       System/privacy tweaks
    StartupPage.xaml(.cs)      Startup program manager
    ProfilesPage.xaml(.cs)     Preset + snapshot UI
    HistoryPage.xaml(.cs)      Change history log
    SettingsPage.xaml(.cs)     App settings
```

## Architecture

The project follows **MVVM** with **constructor-injected services** via
`Microsoft.Extensions.Hosting` / `Microsoft.Extensions.DependencyInjection`.

- **ViewModels** use `CommunityToolkit.Mvvm` (`ObservableObject`, `[ObservableProperty]`,
  `[RelayCommand]`, `[NotifyPropertyChangedFor]`).
- **Services** are registered as `Singleton` (stateful: `SettingsService`, `HistoryService`,
  `SystemMonitorService`) or `Transient` (per-page ViewModels such as category pages).
- **DashboardViewModel** owns a `DispatcherTimer` whose interval comes from
  `SettingsService.Settings.MetricsRefreshSeconds`.
- **ConfirmBeforeApply** is enforced in each category page code-behind via `DialogHelper.ConfirmAsync`
  before calling `ToggleOptimizationAsync`.
- All byte formatting goes through `ByteFormatter` (Format / FormatSpeed).
- Optimization IDs are typed constants in `Models.OptimizationIds` — no raw strings outside the
  Windows service switch-dispatch layer (which uses `.ToLowerInvariant()` for case-insensitive matching).

## License

TBD
