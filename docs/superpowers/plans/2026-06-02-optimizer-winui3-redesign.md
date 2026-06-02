# Optimizer WinUI 3 Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate the Windows Optimizer from WPF to WinUI 3 with a dark utility UI, sidebar navigation, dedicated category pages, full system monitoring dashboard, and in-app elevation flow.

**Architecture:** New WinUI 3 project (`Optimizer.WinUI`) alongside the existing WPF project. Core services (WindowsOptimizerService, UndoService, ElevationService, SystemMonitorService, StartupService, ProcessService) are copied from `Optimizer/Optimization/` with minimal namespace changes. New presentation layer uses CommunityToolkit.Mvvm for MVVM infrastructure, Syncfusion WinUI 3 for charts, and WinUI Community Toolkit for SettingsCards. NavigationView provides sidebar navigation to 9 pages.

**Tech Stack:** WinUI 3 (Windows App SDK 1.7+), .NET 10, CommunityToolkit.Mvvm, CommunityToolkit.WinUI, Syncfusion.WinUI.Charts, Microsoft.Extensions.Hosting/DependencyInjection, Serilog

**Spec:** `docs/superpowers/specs/2026-06-02-optimizer-winui3-redesign.md`

---

## File Structure

```
Optimizer.WinUI/
├── Optimizer.WinUI.csproj
├── app.manifest                         ← asInvoker
├── App.xaml / App.xaml.cs               ← Application entry, DI, theme
├── MainWindow.xaml / MainWindow.xaml.cs  ← Window + NavigationView shell
│
├── Services/
│   ├── NavigationService.cs             ← NEW: page routing for NavigationView
│   ├── ProfileService.cs               ← NEW: preset + snapshot management
│   ├── HistoryService.cs               ← NEW: change log persistence
│   ├── SettingsService.cs              ← REUSE + extend from WPF
│   ├── WindowsOptimizerService.cs      ← REUSE from WPF (copy)
│   ├── IWindowsOptimizerService.cs     ← REUSE from WPF (copy)
│   ├── SystemMonitorService.cs         ← REUSE from WPF (copy)
│   ├── UndoService.cs                  ← REUSE from WPF (copy)
│   ├── IUndoService.cs                 ← REUSE from WPF (copy)
│   ├── ElevationService.cs             ← REUSE from WPF (copy)
│   ├── IElevationService.cs            ← REUSE from WPF (copy)
│   ├── StartupService.cs              ← REUSE from WPF (copy)
│   ├── IStartupService.cs             ← REUSE from WPF (copy)
│   ├── ProcessService.cs              ← REUSE from WPF (copy)
│   └── IProcessService.cs             ← REUSE from WPF (copy)
│
├── ViewModels/
│   ├── DashboardViewModel.cs
│   ├── CategoryViewModelBase.cs         ← shared base for all category pages
│   ├── PerformanceCategoryViewModel.cs
│   ├── NetworkCategoryViewModel.cs
│   ├── StorageCategoryViewModel.cs
│   ├── SystemCategoryViewModel.cs
│   ├── StartupCategoryViewModel.cs
│   ├── ProfilesViewModel.cs
│   ├── HistoryViewModel.cs
│   └── SettingsViewModel.cs
│
├── Views/
│   ├── DashboardPage.xaml(.cs)
│   ├── PerformancePage.xaml(.cs)
│   ├── NetworkPage.xaml(.cs)
│   ├── StoragePage.xaml(.cs)
│   ├── SystemPage.xaml(.cs)
│   ├── StartupPage.xaml(.cs)
│   ├── ProfilesPage.xaml(.cs)
│   ├── HistoryPage.xaml(.cs)
│   └── SettingsPage.xaml(.cs)
│
├── Controls/
│   └── OptimizationCard.xaml(.cs)       ← reusable control for optimization toggles
│
├── Models/
│   ├── OptimizationInfo.cs              ← REUSE from WPF
│   ├── SystemResource.cs               ← REUSE from WPF
│   ├── ProcessInfo.cs                  ← REUSE from WPF
│   ├── SettingsProfile.cs              ← REUSE from WPF
│   ├── StartupEntry.cs                 ← REUSE from WPF
│   ├── ProfileSettings.cs             ← REUSE from WPF (NlaSetting, PowerSetting, etc.)
│   ├── RegistrySetting.cs             ← REUSE from WPF
│   ├── HistoryEntry.cs                 ← NEW
│   └── AppSettings.cs                  ← NEW (extended from WPF SettingsService)
│
├── Helpers/
│   └── ThemeHelper.cs                  ← backdrop + theme switching
│
├── Converters/
│   ├── BoolToVisibilityConverter.cs
│   ├── BytesToStringConverter.cs
│   └── HealthScoreToColorConverter.cs
│
├── Themes/
│   └── Generic.xaml                    ← custom control styles
│
└── Assets/
    └── optimizer.ico
```

---

### Task 1: Create WinUI 3 Project and Configure Dependencies

**Files:**
- Create: `Optimizer.WinUI/Optimizer.WinUI.csproj`
- Create: `Optimizer.WinUI/app.manifest`
- Modify: `Optimizer.slnx` (add new project)

- [ ] **Step 1: Create the WinUI 3 project using the Windows App SDK template**

Run:
```powershell
cd L:\Projects
dotnet new winui3 -n Optimizer.WinUI -o Optimizer.WinUI --framework net10.0-windows10.0.22621.0
```
Expected: Project created at `L:\Projects\Optimizer.WinUI/`

- [ ] **Step 2: Add NuGet dependencies**

Run:
```powershell
cd L:\Projects\Optimizer.WinUI
dotnet add package CommunityToolkit.Mvvm
dotnet add package CommunityToolkit.WinUI.Controls.SettingsControls
dotnet add package CommunityToolkit.WinUI.Converters
dotnet add package Microsoft.Extensions.Hosting
dotnet add package Syncfusion.Chart.WinUI
dotnet add package Serilog
dotnet add package Serilog.Sinks.File
dotnet add package System.Diagnostics.PerformanceCounter
dotnet add package System.Management
dotnet add package TaskScheduler
```
Expected: All packages installed successfully.

- [ ] **Step 3: Create the app manifest with asInvoker**

Create `Optimizer.WinUI/app.manifest`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>
```

- [ ] **Step 4: Update .csproj with manifest reference and platform targets**

Replace the contents of `Optimizer.WinUI/Optimizer.WinUI.csproj` `<PropertyGroup>` to include:
```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
  <RootNamespace>Optimizer.WinUI</RootNamespace>
  <ApplicationManifest>app.manifest</ApplicationManifest>
  <Platforms>x64;ARM64</Platforms>
  <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
  <UseWinUI>true</UseWinUI>
  <WindowsSdkPackageVersion>10.0.22621.49</WindowsSdkPackageVersion>
  <SyncfusionLicenseKey></SyncfusionLicenseKey>
</PropertyGroup>
```

- [ ] **Step 5: Add the new project to the solution**

Run:
```powershell
cd L:\Projects
dotnet sln Optimizer.slnx add Optimizer.WinUI/Optimizer.WinUI.csproj
```
Expected: Project added to solution.

- [ ] **Step 6: Verify the project builds**

Run:
```powershell
cd L:\Projects
dotnet build Optimizer.WinUI/Optimizer.WinUI.csproj -c Debug
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 7: Commit**

```
git init (if not already a repo)
git add Optimizer.WinUI/Optimizer.WinUI.csproj Optimizer.WinUI/app.manifest Optimizer.slnx
git commit -m "feat: scaffold WinUI 3 project with dependencies"
```

---

### Task 2: Copy and Adapt Core Services and Models from WPF

**Files:**
- Copy from `Optimizer/Optimization/Services/` → `Optimizer.WinUI/Services/`
- Copy from `Optimizer/Optimization/Models/` → `Optimizer.WinUI/Models/`
- Copy from `Optimizer/Services/SettingsService.cs` → `Optimizer.WinUI/Services/`

- [ ] **Step 1: Copy all service files**

Run:
```powershell
# Create directories
mkdir -p L:\Projects\Optimizer.WinUI\Services
mkdir -p L:\Projects\Optimizer.WinUI\Models

# Copy services
cp L:\Projects\Optimizer\Optimization\Services\WindowsOptimizerService.cs L:\Projects\Optimizer.WinUI\Services\
cp L:\Projects\Optimizer\Optimization\Services\IWindowsOptimizerService.cs L:\Projects\Optimizer.WinUI\Services\
cp L:\Projects\Optimizer\Optimization\Services\SystemMonitorService.cs L:\Projects\Optimizer.WinUI\Services\
cp L:\Projects\Optimizer\Optimization\Services\UndoService.cs L:\Projects\Optimizer.WinUI\Services\
cp L:\Projects\Optimizer\Optimization\Services\ElevationService.cs L:\Projects\Optimizer.WinUI\Services\
cp L:\Projects\Optimizer\Optimization\Services\StartupService.cs L:\Projects\Optimizer.WinUI\Services\
cp L:\Projects\Optimizer\Optimization\Services\ProcessService.cs L:\Projects\Optimizer.WinUI\Services\
cp L:\Projects\Optimizer\Services\SettingsService.cs L:\Projects\Optimizer.WinUI\Services\
```

- [ ] **Step 2: Copy all model files**

Run:
```powershell
cp L:\Projects\Optimizer\Optimization\Models\OptimizationInfo.cs L:\Projects\Optimizer.WinUI\Models\
cp L:\Projects\Optimizer\Optimization\Models\SystemResource.cs L:\Projects\Optimizer.WinUI\Models\
cp L:\Projects\Optimizer\Optimization\Models\ProcessInfo.cs L:\Projects\Optimizer.WinUI\Models\
cp L:\Projects\Optimizer\Optimization\Models\SettingsProfile.cs L:\Projects\Optimizer.WinUI\Models\
cp L:\Projects\Optimizer\Optimization\Models\StartupEntry.cs L:\Projects\Optimizer.WinUI\Models\
cp L:\Projects\Optimizer\Optimization\Models\ProfileSettings.cs L:\Projects\Optimizer.WinUI\Models\
cp L:\Projects\Optimizer\Optimization\Models\RegistrySetting.cs L:\Projects\Optimizer.WinUI\Models\
```

- [ ] **Step 3: Update namespaces across all copied files**

All service files currently use `namespace WindowsOptimizer.Services` or `namespace Optimizer.Services`. Update them all to `namespace Optimizer.WinUI.Services`.

All model files currently use `namespace WindowsOptimizer.Models`. Update them all to `namespace Optimizer.WinUI.Models`.

Also update `using` statements in each service file to reference `Optimizer.WinUI.Models` instead of `WindowsOptimizer.Models`.

For each file, find and replace:
- `namespace WindowsOptimizer.Services` → `namespace Optimizer.WinUI.Services`
- `namespace WindowsOptimizer.Models` → `namespace Optimizer.WinUI.Models`
- `namespace Optimizer.Services` → `namespace Optimizer.WinUI.Services`
- `using WindowsOptimizer.Models` → `using Optimizer.WinUI.Models`
- `using WindowsOptimizer.Services` → `using Optimizer.WinUI.Services`
- `using Optimizer.Services` → `using Optimizer.WinUI.Services`

- [ ] **Step 4: Extract interface files that are embedded in service files**

Check each service file — some interfaces (like `IUndoService`, `IElevationService`, `IStartupService`, `IProcessService`) may be in the same file as the implementation or in separate files. If embedded, extract them into separate `I*.cs` files in `Services/`. Ensure each interface file has `namespace Optimizer.WinUI.Services`.

- [ ] **Step 5: Verify build**

Run:
```powershell
dotnet build L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj -c Debug
```
Expected: Build succeeded. Fix any remaining namespace issues.

- [ ] **Step 6: Commit**

```
git add Optimizer.WinUI/Services/ Optimizer.WinUI/Models/
git commit -m "feat: port core services and models from WPF project"
```

---

### Task 3: Create New Models (AppSettings, HistoryEntry)

**Files:**
- Create: `Optimizer.WinUI/Models/AppSettings.cs`
- Create: `Optimizer.WinUI/Models/HistoryEntry.cs`

- [ ] **Step 1: Create AppSettings model**

Create `Optimizer.WinUI/Models/AppSettings.cs`:
```csharp
using System.Text.Json.Serialization;

namespace Optimizer.WinUI.Models;

public class AppSettings
{
    public int MetricsRefreshSeconds { get; set; } = 1;
    public int ChartHistorySeconds { get; set; } = 60;
    public string Theme { get; set; } = "Dark";
    public string BackdropMaterial { get; set; } = "Mica";
    public string AccentColor { get; set; } = "#3B82F6";
    public bool StartWithWindows { get; set; }
    public bool ConfirmBeforeApply { get; set; } = true;
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 800;
    public string LastNavigationItem { get; set; } = "Dashboard";
}
```

- [ ] **Step 2: Create HistoryEntry model**

Create `Optimizer.WinUI/Models/HistoryEntry.cs`:
```csharp
namespace Optimizer.WinUI.Models;

public class HistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OptimizationId { get; set; } = "";
    public string OptimizationTitle { get; set; } = "";
    public string Category { get; set; } = "";
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public HistoryAction Action { get; set; }
    public bool IsReversible { get; set; }
    public bool IsUndone { get; set; }
    public string? ResultText { get; set; }
}

public enum HistoryAction
{
    Applied,
    Undone,
    OneTime
}
```

- [ ] **Step 3: Verify build**

Run:
```powershell
dotnet build L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj -c Debug
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```
git add Optimizer.WinUI/Models/AppSettings.cs Optimizer.WinUI/Models/HistoryEntry.cs
git commit -m "feat: add AppSettings and HistoryEntry models"
```

---

### Task 4: Create New Services (NavigationService, ProfileService, HistoryService, SettingsService)

**Files:**
- Create: `Optimizer.WinUI/Services/NavigationService.cs`
- Create: `Optimizer.WinUI/Services/ProfileService.cs`
- Create: `Optimizer.WinUI/Services/HistoryService.cs`
- Modify: `Optimizer.WinUI/Services/SettingsService.cs` (rewrite for new AppSettings)

- [ ] **Step 1: Create NavigationService**

Create `Optimizer.WinUI/Services/NavigationService.cs`:
```csharp
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Optimizer.WinUI.Services;

public class NavigationService
{
    private Frame? _frame;

    public Frame? Frame
    {
        get => _frame;
        set => _frame = value;
    }

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public void GoBack()
    {
        if (_frame?.CanGoBack == true)
            _frame.GoBack();
    }

    public bool NavigateTo(Type pageType, object? parameter = null)
    {
        if (_frame == null) return false;
        if (_frame.Content?.GetType() == pageType) return false;

        return _frame.Navigate(pageType, parameter,
            new DrillInNavigationTransitionInfo());
    }
}
```

- [ ] **Step 2: Create HistoryService**

Create `Optimizer.WinUI/Services/HistoryService.cs`:
```csharp
using System.Text.Json;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class HistoryService
{
    private readonly List<HistoryEntry> _entries = [];
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Optimizer", "change-history.json");

    public IReadOnlyList<HistoryEntry> Entries => _entries;

    public void Load()
    {
        _entries.Clear();
        if (!File.Exists(FilePath)) return;

        try
        {
            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<List<HistoryEntry>>(json);
            if (loaded != null) _entries.AddRange(loaded);
        }
        catch
        {
            // corrupted file — start fresh
        }
    }

    public void RecordApplied(string optimizationId, string title, string category, bool reversible)
    {
        _entries.Insert(0, new HistoryEntry
        {
            OptimizationId = optimizationId,
            OptimizationTitle = title,
            Category = category,
            Action = HistoryAction.Applied,
            IsReversible = reversible,
            TimestampUtc = DateTime.UtcNow
        });
        Save();
    }

    public void RecordOneTime(string optimizationId, string title, string category, string resultText)
    {
        _entries.Insert(0, new HistoryEntry
        {
            OptimizationId = optimizationId,
            OptimizationTitle = title,
            Category = category,
            Action = HistoryAction.OneTime,
            IsReversible = false,
            ResultText = resultText,
            TimestampUtc = DateTime.UtcNow
        });
        Save();
    }

    public void RecordUndone(string optimizationId, string title, string category)
    {
        _entries.Insert(0, new HistoryEntry
        {
            OptimizationId = optimizationId,
            OptimizationTitle = title,
            Category = category,
            Action = HistoryAction.Undone,
            IsReversible = false,
            TimestampUtc = DateTime.UtcNow
        });

        foreach (var e in _entries.Where(e =>
            e.OptimizationId == optimizationId && e.Action == HistoryAction.Applied && !e.IsUndone))
        {
            e.IsUndone = true;
        }
        Save();
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
```

- [ ] **Step 3: Create ProfileService**

Create `Optimizer.WinUI/Services/ProfileService.cs`:
```csharp
using System.Text.Json;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class ProfileService
{
    private readonly IWindowsOptimizerService _optimizer;
    private readonly List<SettingsProfile> _snapshots = [];
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Optimizer", "snapshots.json");

    public ProfileService(IWindowsOptimizerService optimizer)
    {
        _optimizer = optimizer;
    }

    public IReadOnlyList<SettingsProfile> BuiltInPresets => _optimizer.GetBuiltInPresets();
    public IReadOnlyList<SettingsProfile> Snapshots => _snapshots;

    public void Load()
    {
        _snapshots.Clear();
        if (!File.Exists(FilePath)) return;

        try
        {
            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<List<SettingsProfile>>(json);
            if (loaded != null) _snapshots.AddRange(loaded);
        }
        catch { }
    }

    public async Task<bool> ApplyPresetAsync(string profileId)
    {
        return await _optimizer.ApplyProfileAsync(profileId);
    }

    public async Task SaveSnapshotAsync(string name)
    {
        var optimizations = await _optimizer.GetAvailableOptimizationsAsync();
        var activeIds = optimizations
            .Where(id => _optimizer.IsOptimizationApplied(id) == true)
            .ToList();

        var snapshot = new SettingsProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Description = $"Snapshot saved {DateTime.Now:g}",
            ProfileType = ProfileType.Custom,
            CreatedAt = DateTime.UtcNow,
            Optimizations = activeIds
        };

        _snapshots.Add(snapshot);
        SaveSnapshots();
    }

    public async Task<bool> RestoreSnapshotAsync(SettingsProfile snapshot)
    {
        return await _optimizer.ApplyProfileAsync(snapshot.Id);
    }

    public void UpdateSnapshot(SettingsProfile snapshot)
    {
        // Re-capture current state into existing snapshot
        var optimizations = _optimizer.GetAvailableOptimizationsAsync().Result;
        snapshot.Optimizations = optimizations
            .Where(id => _optimizer.IsOptimizationApplied(id) == true)
            .ToList();
        snapshot.LastAppliedAt = DateTime.UtcNow;
        SaveSnapshots();
    }

    public void DeleteSnapshot(string snapshotId)
    {
        _snapshots.RemoveAll(s => s.Id == snapshotId);
        SaveSnapshots();
    }

    public string ExportAll()
    {
        return JsonSerializer.Serialize(_snapshots, new JsonSerializerOptions { WriteIndented = true });
    }

    public void ImportFromJson(string json)
    {
        var imported = JsonSerializer.Deserialize<List<SettingsProfile>>(json);
        if (imported != null)
        {
            _snapshots.AddRange(imported);
            SaveSnapshots();
        }
    }

    private void SaveSnapshots()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(_snapshots, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
```

- [ ] **Step 4: Rewrite SettingsService for new AppSettings model**

Replace `Optimizer.WinUI/Services/SettingsService.cs` with:
```csharp
using System.Text.Json;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class SettingsService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Optimizer", "app-settings.json");

    public AppSettings Settings { get; private set; } = new();

    public void Load()
    {
        if (!File.Exists(FilePath)) return;
        try
        {
            var json = File.ReadAllText(FilePath);
            Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        }
        catch
        {
            Settings = new();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }

    public void Reset()
    {
        Settings = new AppSettings();
        Save();
    }
}
```

- [ ] **Step 5: Verify build**

Run:
```powershell
dotnet build L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj -c Debug
```
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```
git add Optimizer.WinUI/Services/NavigationService.cs Optimizer.WinUI/Services/ProfileService.cs Optimizer.WinUI/Services/HistoryService.cs Optimizer.WinUI/Services/SettingsService.cs
git commit -m "feat: add NavigationService, ProfileService, HistoryService, rewrite SettingsService"
```

---

### Task 5: Configure App.xaml — DI Container, Theme, and Startup

**Files:**
- Modify: `Optimizer.WinUI/App.xaml`
- Modify: `Optimizer.WinUI/App.xaml.cs`
- Create: `Optimizer.WinUI/Helpers/ThemeHelper.cs`

- [ ] **Step 1: Create ThemeHelper**

Create `Optimizer.WinUI/Helpers/ThemeHelper.cs`:
```csharp
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Optimizer.WinUI.Helpers;

public static class ThemeHelper
{
    public static void ApplyBackdrop(Window window, string material)
    {
        window.SystemBackdrop = material switch
        {
            "Mica" => new MicaBackdrop(),
            "MicaAlt" => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            "Acrylic" => new DesktopAcrylicBackdrop(),
            _ => null
        };
    }

    public static void ApplyTheme(FrameworkElement root, string theme)
    {
        root.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }
}
```

- [ ] **Step 2: Update App.xaml for dark theme default**

Replace `Optimizer.WinUI/App.xaml`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<Application
    x:Class="Optimizer.WinUI.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    RequestedTheme="Dark">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 3: Update App.xaml.cs with DI container and startup**

Replace `Optimizer.WinUI/App.xaml.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.ViewModels;
using Optimizer.WinUI.Views;

namespace Optimizer.WinUI;

public partial class App : Application
{
    public static IHost Host { get; private set; } = null!;
    public static T GetService<T>() where T : class => Host.Services.GetRequiredService<T>();

    private Window? _window;

    public App()
    {
        InitializeComponent();

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // Core services (reused from WPF)
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

                // MainWindow
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Initialize services that need loading
        var settings = GetService<SettingsService>();
        settings.Load();

        var undoService = GetService<IUndoService>() as UndoService;
        undoService?.Load();

        var historyService = GetService<HistoryService>();
        historyService.Load();

        var profileService = GetService<ProfileService>();
        profileService.Load();

        _window = GetService<MainWindow>();
        _window.Activate();

        // Apply theme and backdrop
        ThemeHelper.ApplyBackdrop(_window, settings.Settings.BackdropMaterial);
        if (_window.Content is FrameworkElement root)
            ThemeHelper.ApplyTheme(root, settings.Settings.Theme);
    }
}
```

- [ ] **Step 4: Verify build**

Run:
```powershell
dotnet build L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj -c Debug
```
Expected: Build will fail because ViewModels and Views don't exist yet. That's expected — we'll create them next.

- [ ] **Step 5: Commit**

```
git add Optimizer.WinUI/App.xaml Optimizer.WinUI/App.xaml.cs Optimizer.WinUI/Helpers/ThemeHelper.cs
git commit -m "feat: configure DI container, dark theme, and Mica backdrop"
```

---

### Task 6: Build MainWindow with NavigationView Shell

**Files:**
- Modify: `Optimizer.WinUI/MainWindow.xaml`
- Modify: `Optimizer.WinUI/MainWindow.xaml.cs`
- Create stub pages for all 9 views so navigation compiles

- [ ] **Step 1: Create all 9 stub pages**

For each page, create a minimal XAML + code-behind. Example for `DashboardPage`:

Create `Optimizer.WinUI/Views/DashboardPage.xaml`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="Optimizer.WinUI.Views.DashboardPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Background="Transparent">
    <TextBlock Text="Dashboard" Style="{StaticResource TitleTextBlockStyle}" Margin="24"/>
</Page>
```

Create `Optimizer.WinUI/Views/DashboardPage.xaml.cs`:
```csharp
using Microsoft.UI.Xaml.Controls;

namespace Optimizer.WinUI.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
    }
}
```

Repeat for: `PerformancePage`, `NetworkPage`, `StoragePage`, `SystemPage`, `StartupPage`, `ProfilesPage`, `HistoryPage`, `SettingsPage` — each with the same structure, only changing the class name and the placeholder TextBlock text.

- [ ] **Step 2: Create all 9 stub ViewModels**

For each ViewModel, create a minimal class. Example for `DashboardViewModel`:

Create `Optimizer.WinUI/ViewModels/DashboardViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace Optimizer.WinUI.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
}
```

Repeat for: `PerformanceCategoryViewModel`, `NetworkCategoryViewModel`, `StorageCategoryViewModel`, `SystemCategoryViewModel`, `StartupCategoryViewModel`, `ProfilesViewModel`, `HistoryViewModel`, `SettingsViewModel`.

Also create `Optimizer.WinUI/ViewModels/CategoryViewModelBase.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace Optimizer.WinUI.ViewModels;

public abstract partial class CategoryViewModelBase : ObservableObject
{
}
```

- [ ] **Step 3: Build MainWindow.xaml with NavigationView**

Replace `Optimizer.WinUI/MainWindow.xaml`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<Window
    x:Class="Optimizer.WinUI.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Optimizer"
    MinWidth="900" MinHeight="600">

    <Grid>
        <NavigationView
            x:Name="NavView"
            IsBackButtonVisible="Collapsed"
            IsSettingsVisible="False"
            PaneDisplayMode="Left"
            OpenPaneLength="220"
            SelectionChanged="NavView_SelectionChanged"
            Loaded="NavView_Loaded">

            <NavigationView.MenuItems>
                <NavigationViewItem Content="Dashboard" Tag="Dashboard" Icon="Home"/>

                <NavigationViewItemHeader Content="OPTIMIZE"/>
                <NavigationViewItem Content="Performance" Tag="Performance">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE945;"/>
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="Network" Tag="Network">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE968;"/>
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="Storage" Tag="Storage">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xEDA2;"/>
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="System" Tag="System">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE770;"/>
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="Startup" Tag="Startup">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE7B5;"/>
                    </NavigationViewItem.Icon>
                </NavigationViewItem>

                <NavigationViewItemHeader Content="MANAGE"/>
                <NavigationViewItem Content="Profiles" Tag="Profiles">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE8FD;"/>
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="History" Tag="History">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE81C;"/>
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
            </NavigationView.MenuItems>

            <NavigationView.FooterMenuItems>
                <NavigationViewItem Content="Settings" Tag="Settings" Icon="Setting"/>
            </NavigationView.FooterMenuItems>

            <Frame x:Name="ContentFrame"/>
        </NavigationView>
    </Grid>
</Window>
```

- [ ] **Step 4: Build MainWindow.xaml.cs with navigation logic**

Replace `Optimizer.WinUI/MainWindow.xaml.cs`:
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Views;

namespace Optimizer.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly NavigationService _navigationService;
    private readonly SettingsService _settingsService;

    private static readonly Dictionary<string, Type> PageMap = new()
    {
        ["Dashboard"] = typeof(DashboardPage),
        ["Performance"] = typeof(PerformancePage),
        ["Network"] = typeof(NetworkPage),
        ["Storage"] = typeof(StoragePage),
        ["System"] = typeof(SystemPage),
        ["Startup"] = typeof(StartupPage),
        ["Profiles"] = typeof(ProfilesPage),
        ["History"] = typeof(HistoryPage),
        ["Settings"] = typeof(SettingsPage),
    };

    public MainWindow(NavigationService navigationService, SettingsService settingsService)
    {
        InitializeComponent();

        _navigationService = navigationService;
        _settingsService = settingsService;
        _navigationService.Frame = ContentFrame;

        // Set window size from settings
        var s = _settingsService.Settings;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)s.WindowWidth, (int)s.WindowHeight));
        Title = "Optimizer";
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // Select Dashboard on startup
        var lastNav = _settingsService.Settings.LastNavigationItem;
        if (!PageMap.ContainsKey(lastNav)) lastNav = "Dashboard";

        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag?.ToString() == lastNav)
            {
                NavView.SelectedItem = item;
                break;
            }
        }

        _navigationService.NavigateTo(PageMap[lastNav]);
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            if (PageMap.TryGetValue(tag, out var pageType))
            {
                _navigationService.NavigateTo(pageType);
                _settingsService.Settings.LastNavigationItem = tag;
            }
        }
    }
}
```

- [ ] **Step 5: Verify build and run**

Run:
```powershell
dotnet build L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj -c Debug
```
Expected: Build succeeded.

Then run the app to verify the NavigationView shell appears with all sidebar items and clicking them shows the stub pages:
```powershell
dotnet run --project L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj
```
Expected: Window opens with dark theme, Mica backdrop, sidebar with Dashboard/Performance/Network/Storage/System/Startup/Profiles/History/Settings items. Clicking items navigates between stub pages.

- [ ] **Step 6: Commit**

```
git add Optimizer.WinUI/MainWindow.xaml Optimizer.WinUI/MainWindow.xaml.cs Optimizer.WinUI/Views/ Optimizer.WinUI/ViewModels/
git commit -m "feat: build NavigationView shell with page routing"
```

---

### Task 7: Build the Elevation UX (InfoBar + Shield Flow)

**Files:**
- Modify: `Optimizer.WinUI/MainWindow.xaml` (add elevation InfoBar)
- Modify: `Optimizer.WinUI/MainWindow.xaml.cs` (add elevation logic)

- [ ] **Step 1: Add elevation InfoBar to MainWindow.xaml**

In `MainWindow.xaml`, wrap the NavigationView in a Grid and add an InfoBar above it. Replace the `<Grid>` content:

```xml
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="*"/>
    </Grid.RowDefinitions>

    <!-- Elevation InfoBar -->
    <InfoBar
        x:Name="ElevationInfoBar"
        Grid.Row="0"
        IsOpen="True"
        IsClosable="False"
        Severity="Warning"
        Title="Running without administrator privileges"
        Message="Some optimizations require admin access to apply."
        Visibility="Collapsed">
        <InfoBar.ActionButton>
            <Button Content="🛡️ Relaunch as Admin" Click="RelaunchElevated_Click"/>
        </InfoBar.ActionButton>
    </InfoBar>

    <InfoBar
        x:Name="ElevatedInfoBar"
        Grid.Row="0"
        IsOpen="True"
        IsClosable="True"
        Severity="Success"
        Title="Running as Administrator"
        Message="All optimizations are available."
        Visibility="Collapsed"/>

    <NavigationView
        x:Name="NavView"
        Grid.Row="1"
        IsBackButtonVisible="Collapsed"
        IsSettingsVisible="False"
        PaneDisplayMode="Left"
        OpenPaneLength="220"
        SelectionChanged="NavView_SelectionChanged"
        Loaded="NavView_Loaded">
        <!-- ... existing NavigationView content unchanged ... -->
    </NavigationView>
</Grid>
```

- [ ] **Step 2: Add elevation logic to MainWindow.xaml.cs**

Add to the constructor of `MainWindow`, after the existing code:
```csharp
var elevationService = App.GetService<IElevationService>();
if (elevationService.IsElevated)
{
    ElevatedInfoBar.Visibility = Visibility.Visible;
    ElevationInfoBar.Visibility = Visibility.Collapsed;
}
else
{
    ElevationInfoBar.Visibility = Visibility.Visible;
    ElevatedInfoBar.Visibility = Visibility.Collapsed;
}
```

Add this method to `MainWindow`:
```csharp
private async void RelaunchElevated_Click(object sender, RoutedEventArgs e)
{
    var dialog = new ContentDialog
    {
        Title = "Relaunch as Administrator?",
        Content = "The app will close and reopen with elevated permissions. Your current state will be preserved.",
        PrimaryButtonText = "🛡️ Relaunch",
        CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Primary,
        XamlRoot = Content.XamlRoot
    };

    var result = await dialog.ShowAsync();
    if (result == ContentDialogResult.Primary)
    {
        _settingsService.Save();
        var elevationService = App.GetService<IElevationService>();
        if (elevationService.TryRelaunchElevated())
        {
            Close();
        }
    }
}
```

- [ ] **Step 3: Verify build and test elevation states**

Run:
```powershell
dotnet build L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj -c Debug && dotnet run --project L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj
```
Expected: App launches with amber InfoBar "Running without administrator privileges" at the top. The "Relaunch as Admin" button shows a ContentDialog confirmation. (Don't actually relaunch — just verify the dialog appears.)

- [ ] **Step 4: Commit**

```
git add Optimizer.WinUI/MainWindow.xaml Optimizer.WinUI/MainWindow.xaml.cs
git commit -m "feat: add elevation InfoBar and relaunch-as-admin flow"
```

---

### Task 8: Build the Dashboard Page

**Files:**
- Modify: `Optimizer.WinUI/ViewModels/DashboardViewModel.cs`
- Modify: `Optimizer.WinUI/Views/DashboardPage.xaml`
- Modify: `Optimizer.WinUI/Views/DashboardPage.xaml.cs`
- Create: `Optimizer.WinUI/Converters/BytesToStringConverter.cs`
- Create: `Optimizer.WinUI/Converters/HealthScoreToColorConverter.cs`

- [ ] **Step 1: Create value converters**

Create `Optimizer.WinUI/Converters/BytesToStringConverter.cs`:
```csharp
using Microsoft.UI.Xaml.Data;

namespace Optimizer.WinUI.Converters;

public class BytesToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is long bytes)
        {
            return bytes switch
            {
                >= 1_073_741_824 => $"{bytes / 1073741824.0:F1} GB",
                >= 1_048_576 => $"{bytes / 1048576.0:F0} MB",
                >= 1024 => $"{bytes / 1024.0:F0} KB",
                _ => $"{bytes} B"
            };
        }
        return "0 B";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
```

Create `Optimizer.WinUI/Converters/HealthScoreToColorConverter.cs`:
```csharp
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Optimizer.WinUI.Converters;

public class HealthScoreToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int score)
        {
            return score switch
            {
                >= 70 => new SolidColorBrush(ColorHelper.FromArgb(255, 74, 222, 128)),   // green
                >= 40 => new SolidColorBrush(ColorHelper.FromArgb(255, 251, 191, 36)),   // yellow
                _ => new SolidColorBrush(ColorHelper.FromArgb(255, 248, 113, 113))        // red
            };
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
```

- [ ] **Step 2: Build DashboardViewModel**

Replace `Optimizer.WinUI/ViewModels/DashboardViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly IWindowsOptimizerService _optimizer;
    private readonly SystemMonitorService _monitor;
    private readonly IProcessService _processService;
    private readonly NavigationService _navigationService;
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private double cpuUsage;
    [ObservableProperty] private double memoryUsage;
    [ObservableProperty] private long totalMemoryBytes;
    [ObservableProperty] private long usedMemoryBytes;
    [ObservableProperty] private double gpuUsage;
    [ObservableProperty] private double diskUsage;
    [ObservableProperty] private double networkUsage;
    [ObservableProperty] private int totalCores;
    [ObservableProperty] private int healthScore;
    [ObservableProperty] private string healthText = "Good";
    [ObservableProperty] private int activeOptimizations;
    [ObservableProperty] private int undoableChanges;
    [ObservableProperty] private string lastUpdated = "";
    [ObservableProperty] private double diskReadSpeed;
    [ObservableProperty] private double diskWriteSpeed;
    [ObservableProperty] private double networkInSpeed;
    [ObservableProperty] private double networkOutSpeed;

    public ObservableCollection<ProcessInfo> TopProcesses { get; } = [];
    public ObservableCollection<double> PerCoreUsage { get; } = [];
    public ObservableCollection<SystemResource> ChartHistory { get; } = [];

    public DashboardViewModel(
        IWindowsOptimizerService optimizer,
        SystemMonitorService monitor,
        IProcessService processService,
        NavigationService navigationService)
    {
        _optimizer = optimizer;
        _monitor = monitor;
        _processService = processService;
        _navigationService = navigationService;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
    }

    public void StartMonitoring()
    {
        _ = _monitor.StartMonitoringAsync();
        _timer.Start();
        Refresh();
    }

    public void StopMonitoring()
    {
        _timer.Stop();
    }

    private void Refresh()
    {
        var snapshot = _monitor.CollectSnapshot();

        CpuUsage = snapshot.CpuUsagePercentage;
        MemoryUsage = 100.0 * (snapshot.TotalPhysicalMemory - snapshot.AvailablePhysicalMemory) / Math.Max(snapshot.TotalPhysicalMemory, 1);
        TotalMemoryBytes = snapshot.TotalPhysicalMemory;
        UsedMemoryBytes = snapshot.TotalPhysicalMemory - snapshot.AvailablePhysicalMemory;
        GpuUsage = snapshot.GpuUsagePercentage;
        TotalCores = snapshot.TotalProcessors;
        DiskReadSpeed = snapshot.DiskReadSpeed;
        DiskWriteSpeed = snapshot.DiskWriteSpeed;
        NetworkInSpeed = snapshot.NetworkInSpeed;
        NetworkOutSpeed = snapshot.NetworkOutSpeed;
        LastUpdated = DateTime.Now.ToString("HH:mm:ss");

        UndoableChanges = _optimizer.PendingUndoCount;

        // Per-core
        var cores = _monitor.GetPerCoreUsage();
        PerCoreUsage.Clear();
        foreach (var c in cores) PerCoreUsage.Add(c);

        // Chart
        ChartHistory.Add(snapshot);
        while (ChartHistory.Count > 60) ChartHistory.RemoveAt(0);

        // Top processes
        TopProcesses.Clear();
        foreach (var p in _processService.GetTopProcesses(8))
            TopProcesses.Add(p);
    }

    [RelayCommand]
    private void RefreshNow() => Refresh();

    [RelayCommand]
    private async Task ApplySafeTune()
    {
        var presets = _optimizer.GetBuiltInPresets();
        var light = presets.FirstOrDefault(p => p.Name.Contains("Light", StringComparison.OrdinalIgnoreCase));
        if (light != null)
            await _optimizer.ApplyProfileAsync(light.Id);
        Refresh();
    }

    [RelayCommand]
    private async Task UndoAll()
    {
        await _optimizer.UndoAllOptimizationsAsync();
        Refresh();
    }

    public void Dispose()
    {
        _timer.Stop();
    }
}
```

- [ ] **Step 3: Build DashboardPage.xaml**

Replace `Optimizer.WinUI/Views/DashboardPage.xaml` with the full dashboard layout. This is a long file — key sections:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="Optimizer.WinUI.Views.DashboardPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:converters="using:Optimizer.WinUI.Converters"
    Background="Transparent"
    Loaded="Page_Loaded"
    Unloaded="Page_Unloaded">

    <Page.Resources>
        <converters:BytesToStringConverter x:Key="BytesToString"/>
        <converters:HealthScoreToColorConverter x:Key="HealthToColor"/>
    </Page.Resources>

    <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="24">
        <StackPanel Spacing="20">

            <!-- Header -->
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <StackPanel>
                    <TextBlock Text="System Dashboard" Style="{StaticResource TitleTextBlockStyle}"/>
                    <TextBlock Text="{x:Bind ViewModel.LastUpdated, Mode=OneWay}" 
                               Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                               Style="{StaticResource CaptionTextBlockStyle}"/>
                </StackPanel>
                <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8">
                    <Button Content="↻ Refresh" Command="{x:Bind ViewModel.RefreshNowCommand}"/>
                    <Button Content="⚡ Apply Safe Tune" Command="{x:Bind ViewModel.ApplySafeTuneCommand}"
                            Style="{StaticResource AccentButtonStyle}"/>
                    <Button Command="{x:Bind ViewModel.UndoAllCommand}">
                        <StackPanel Orientation="Horizontal" Spacing="4">
                            <TextBlock Text="↶ Undo All"/>
                            <TextBlock Text="{x:Bind ViewModel.UndoableChanges, Mode=OneWay}" 
                                       Foreground="{ThemeResource SystemFillColorCriticalBrush}"/>
                        </StackPanel>
                    </Button>
                </StackPanel>
            </Grid>

            <!-- Health Banner -->
            <Border Background="{x:Bind ViewModel.HealthScore, Mode=OneWay, Converter={StaticResource HealthToColor}}"
                    CornerRadius="8" Padding="16,10" Opacity="0.15"/>
            <!-- Overlay text on the banner with proper contrast -->
            <InfoBar IsOpen="True" IsClosable="False"
                     Severity="Success"
                     Title="{x:Bind ViewModel.HealthText, Mode=OneWay}">
                <InfoBar.Content>
                    <StackPanel Orientation="Horizontal" Spacing="12">
                        <TextBlock Text="{x:Bind ViewModel.HealthScore, Mode=OneWay}"
                                   FontSize="20" FontWeight="Bold" FontFamily="Cascadia Code"/>
                        <TextBlock VerticalAlignment="Center"
                                   Foreground="{ThemeResource TextFillColorSecondaryBrush}">
                            <Run Text="{x:Bind ViewModel.ActiveOptimizations, Mode=OneWay}"/>
                            <Run Text=" optimizations active ·"/>
                            <Run Text="{x:Bind ViewModel.UndoableChanges, Mode=OneWay}"/>
                            <Run Text=" changes undoable"/>
                        </TextBlock>
                    </StackPanel>
                </InfoBar.Content>
            </InfoBar>

            <!-- Metric Cards -->
            <Grid ColumnSpacing="12">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>

                <!-- CPU Card -->
                <Border Grid.Column="0" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                        CornerRadius="8" Padding="14" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                    <StackPanel Spacing="8">
                        <Grid>
                            <TextBlock Text="CPU" Foreground="{ThemeResource TextFillColorTertiaryBrush}"
                                       FontSize="11" CharacterSpacing="50"/>
                            <TextBlock Text="{x:Bind ViewModel.TotalCores, Mode=OneWay}" HorizontalAlignment="Right"
                                       Foreground="{ThemeResource TextFillColorDisabledBrush}" FontSize="10"/>
                        </Grid>
                        <TextBlock FontSize="26" FontWeight="Bold" FontFamily="Cascadia Code" Foreground="#60A5FA">
                            <Run Text="{x:Bind ViewModel.CpuUsage, Mode=OneWay}"/><Run Text="%"/>
                        </TextBlock>
                        <ProgressBar Value="{x:Bind ViewModel.CpuUsage, Mode=OneWay}" Maximum="100"
                                     Height="4" Foreground="#60A5FA"/>
                    </StackPanel>
                </Border>

                <!-- Memory Card -->
                <Border Grid.Column="1" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                        CornerRadius="8" Padding="14" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                    <StackPanel Spacing="8">
                        <Grid>
                            <TextBlock Text="MEMORY" Foreground="{ThemeResource TextFillColorTertiaryBrush}"
                                       FontSize="11" CharacterSpacing="50"/>
                            <TextBlock Text="{x:Bind ViewModel.TotalMemoryBytes, Mode=OneWay, Converter={StaticResource BytesToString}}"
                                       HorizontalAlignment="Right" Foreground="{ThemeResource TextFillColorDisabledBrush}" FontSize="10"/>
                        </Grid>
                        <TextBlock FontSize="26" FontWeight="Bold" FontFamily="Cascadia Code" Foreground="#34D399">
                            <Run Text="{x:Bind ViewModel.MemoryUsage, Mode=OneWay}"/><Run Text="%"/>
                        </TextBlock>
                        <ProgressBar Value="{x:Bind ViewModel.MemoryUsage, Mode=OneWay}" Maximum="100"
                                     Height="4" Foreground="#34D399"/>
                    </StackPanel>
                </Border>

                <!-- GPU Card -->
                <Border Grid.Column="2" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                        CornerRadius="8" Padding="14" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                    <StackPanel Spacing="8">
                        <TextBlock Text="GPU" Foreground="{ThemeResource TextFillColorTertiaryBrush}"
                                   FontSize="11" CharacterSpacing="50"/>
                        <TextBlock FontSize="26" FontWeight="Bold" FontFamily="Cascadia Code" Foreground="#F59E0B">
                            <Run Text="{x:Bind ViewModel.GpuUsage, Mode=OneWay}"/><Run Text="%"/>
                        </TextBlock>
                        <ProgressBar Value="{x:Bind ViewModel.GpuUsage, Mode=OneWay}" Maximum="100"
                                     Height="4" Foreground="#F59E0B"/>
                    </StackPanel>
                </Border>

                <!-- Disk Card -->
                <Border Grid.Column="3" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                        CornerRadius="8" Padding="14" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                    <StackPanel Spacing="8">
                        <TextBlock Text="DISK" Foreground="{ThemeResource TextFillColorTertiaryBrush}"
                                   FontSize="11" CharacterSpacing="50"/>
                        <TextBlock FontSize="26" FontWeight="Bold" FontFamily="Cascadia Code" Foreground="#A78BFA">
                            <Run Text="{x:Bind ViewModel.DiskUsage, Mode=OneWay}"/><Run Text="%"/>
                        </TextBlock>
                        <ProgressBar Value="{x:Bind ViewModel.DiskUsage, Mode=OneWay}" Maximum="100"
                                     Height="4" Foreground="#A78BFA"/>
                    </StackPanel>
                </Border>

                <!-- Network Card -->
                <Border Grid.Column="4" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                        CornerRadius="8" Padding="14" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                    <StackPanel Spacing="8">
                        <TextBlock Text="NETWORK" Foreground="{ThemeResource TextFillColorTertiaryBrush}"
                                   FontSize="11" CharacterSpacing="50"/>
                        <TextBlock FontSize="26" FontWeight="Bold" FontFamily="Cascadia Code" Foreground="#F472B6">
                            <Run Text="{x:Bind ViewModel.NetworkUsage, Mode=OneWay}"/><Run Text="%"/>
                        </TextBlock>
                        <ProgressBar Value="{x:Bind ViewModel.NetworkUsage, Mode=OneWay}" Maximum="100"
                                     Height="4" Foreground="#F472B6"/>
                    </StackPanel>
                </Border>
            </Grid>

            <!-- Top Processes -->
            <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                    CornerRadius="8" Padding="16" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                <StackPanel Spacing="8">
                    <TextBlock Text="Top Processes" FontWeight="SemiBold"/>
                    <ListView ItemsSource="{x:Bind ViewModel.TopProcesses, Mode=OneWay}" SelectionMode="None">
                        <ListView.ItemTemplate>
                            <DataTemplate x:DataType="x:String">
                                <Grid ColumnSpacing="16">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="80"/>
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Text="{x:Bind}" Grid.Column="0"/>
                                </Grid>
                            </DataTemplate>
                        </ListView.ItemTemplate>
                    </ListView>
                </StackPanel>
            </Border>

            <!-- I/O Activity -->
            <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                    CornerRadius="8" Padding="16" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                <StackPanel Spacing="12">
                    <TextBlock Text="I/O Activity" FontWeight="SemiBold"/>
                    <Grid ColumnSpacing="24">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
                        <StackPanel Grid.Column="0" Spacing="8">
                            <Grid><TextBlock Text="Disk Read" Foreground="#A78BFA" FontSize="11"/>
                                <TextBlock HorizontalAlignment="Right" FontFamily="Cascadia Code" FontSize="11" Foreground="{ThemeResource TextFillColorSecondaryBrush}">
                                    <Run Text="{x:Bind ViewModel.DiskReadSpeed, Mode=OneWay}"/><Run Text=" MB/s"/>
                                </TextBlock></Grid>
                            <Grid><TextBlock Text="Disk Write" Foreground="#C084FC" FontSize="11"/>
                                <TextBlock HorizontalAlignment="Right" FontFamily="Cascadia Code" FontSize="11" Foreground="{ThemeResource TextFillColorSecondaryBrush}">
                                    <Run Text="{x:Bind ViewModel.DiskWriteSpeed, Mode=OneWay}"/><Run Text=" MB/s"/>
                                </TextBlock></Grid>
                        </StackPanel>
                        <StackPanel Grid.Column="1" Spacing="8">
                            <Grid><TextBlock Text="Net ↓" Foreground="#F472B6" FontSize="11"/>
                                <TextBlock HorizontalAlignment="Right" FontFamily="Cascadia Code" FontSize="11" Foreground="{ThemeResource TextFillColorSecondaryBrush}">
                                    <Run Text="{x:Bind ViewModel.NetworkInSpeed, Mode=OneWay}"/><Run Text=" MB/s"/>
                                </TextBlock></Grid>
                            <Grid><TextBlock Text="Net ↑" Foreground="#FB923C" FontSize="11"/>
                                <TextBlock HorizontalAlignment="Right" FontFamily="Cascadia Code" FontSize="11" Foreground="{ThemeResource TextFillColorSecondaryBrush}">
                                    <Run Text="{x:Bind ViewModel.NetworkOutSpeed, Mode=OneWay}"/><Run Text=" MB/s"/>
                                </TextBlock></Grid>
                        </StackPanel>
                    </Grid>
                </StackPanel>
            </Border>

        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 4: Update DashboardPage.xaml.cs**

Replace `Optimizer.WinUI/Views/DashboardPage.xaml.cs`:
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        ViewModel = App.GetService<DashboardViewModel>();
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.StartMonitoring();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.StopMonitoring();
    }
}
```

- [ ] **Step 5: Verify build and run — confirm metric cards update live**

Run:
```powershell
dotnet build L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj -c Debug && dotnet run --project L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj
```
Expected: Dashboard shows live CPU/Memory/GPU/Disk/Network metrics updating every second. Top processes list populates. I/O values update.

- [ ] **Step 6: Commit**

```
git add Optimizer.WinUI/Views/DashboardPage.xaml Optimizer.WinUI/Views/DashboardPage.xaml.cs Optimizer.WinUI/ViewModels/DashboardViewModel.cs Optimizer.WinUI/Converters/
git commit -m "feat: build dashboard page with live system monitoring"
```

---

### Task 9: Build the OptimizationCard Custom Control

**Files:**
- Create: `Optimizer.WinUI/Controls/OptimizationCard.xaml`
- Create: `Optimizer.WinUI/Controls/OptimizationCard.xaml.cs`

- [ ] **Step 1: Create OptimizationCard.xaml**

Create `Optimizer.WinUI/Controls/OptimizationCard.xaml`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<UserControl
    x:Class="Optimizer.WinUI.Controls.OptimizationCard"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
            CornerRadius="8" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
        <Expander HorizontalAlignment="Stretch" HorizontalContentAlignment="Stretch"
                  Padding="0" ExpandDirection="Down">
            <Expander.Header>
                <Grid Padding="14,14,14,14" ColumnSpacing="12">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>

                    <!-- Icon -->
                    <Border Grid.Column="0" Width="36" Height="36" CornerRadius="8"
                            Background="{x:Bind IconBackground, Mode=OneWay}">
                        <TextBlock Text="{x:Bind Icon, Mode=OneWay}" FontSize="18"
                                   HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Border>

                    <!-- Title + Description -->
                    <StackPanel Grid.Column="1" VerticalAlignment="Center" Spacing="2">
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <TextBlock Text="{x:Bind Title, Mode=OneWay}" FontSize="14" FontWeight="SemiBold"
                                       Foreground="{x:Bind TitleForeground, Mode=OneWay}"/>
                            <Border Visibility="{x:Bind ActiveBadgeVisibility, Mode=OneWay}"
                                    Background="#065F46" CornerRadius="4" Padding="6,2">
                                <TextBlock Text="ACTIVE" FontSize="9" Foreground="#6EE7B7"/>
                            </Border>
                            <TextBlock Text="🛡️" Visibility="{x:Bind ShieldVisibility, Mode=OneWay}" FontSize="12"/>
                        </StackPanel>
                        <TextBlock Text="{x:Bind Description, Mode=OneWay}" FontSize="12"
                                   Foreground="{ThemeResource TextFillColorSecondaryBrush}" TextTrimming="CharacterEllipsis"/>
                    </StackPanel>

                    <!-- Toggle -->
                    <ToggleSwitch Grid.Column="3" IsOn="{x:Bind IsActive, Mode=TwoWay}"
                                  Toggled="Toggle_Toggled" VerticalAlignment="Center"
                                  MinWidth="0" MinHeight="0"
                                  Opacity="{x:Bind ToggleOpacity, Mode=OneWay}"/>
                </Grid>
            </Expander.Header>

            <Expander.Content>
                <Grid Padding="14,0,14,14" Margin="48,0,0,0" ColumnSpacing="16">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>

                    <!-- What changes -->
                    <StackPanel Grid.Column="0" Spacing="4">
                        <TextBlock Text="WHAT CHANGES" FontSize="10"
                                   Foreground="{ThemeResource TextFillColorDisabledBrush}" CharacterSpacing="100"/>
                        <Border Background="{ThemeResource SolidBackgroundFillColorBaseBrush}"
                                CornerRadius="4" Padding="8">
                            <TextBlock Text="{x:Bind ChangesText, Mode=OneWay}" FontFamily="Cascadia Code"
                                       FontSize="11" TextWrapping="Wrap"
                                       Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
                        </Border>
                    </StackPanel>

                    <!-- Impact -->
                    <StackPanel Grid.Column="1" Spacing="4">
                        <TextBlock Text="IMPACT" FontSize="10"
                                   Foreground="{ThemeResource TextFillColorDisabledBrush}" CharacterSpacing="100"/>
                        <ItemsRepeater ItemsSource="{x:Bind Pros, Mode=OneWay}">
                            <ItemsRepeater.ItemTemplate>
                                <DataTemplate x:DataType="x:String">
                                    <StackPanel Orientation="Horizontal" Spacing="6" Margin="0,2">
                                        <TextBlock Text="▲" Foreground="#4ADE80" FontSize="12"/>
                                        <TextBlock Text="{x:Bind}" Foreground="{ThemeResource TextFillColorSecondaryBrush}" FontSize="12"/>
                                    </StackPanel>
                                </DataTemplate>
                            </ItemsRepeater.ItemTemplate>
                        </ItemsRepeater>
                        <ItemsRepeater ItemsSource="{x:Bind Cons, Mode=OneWay}">
                            <ItemsRepeater.ItemTemplate>
                                <DataTemplate x:DataType="x:String">
                                    <StackPanel Orientation="Horizontal" Spacing="6" Margin="0,2">
                                        <TextBlock Text="▼" Foreground="#F87171" FontSize="12"/>
                                        <TextBlock Text="{x:Bind}" Foreground="{ThemeResource TextFillColorSecondaryBrush}" FontSize="12"/>
                                    </StackPanel>
                                </DataTemplate>
                            </ItemsRepeater.ItemTemplate>
                        </ItemsRepeater>
                    </StackPanel>

                    <!-- Badges -->
                    <StackPanel Grid.ColumnSpan="2" Orientation="Horizontal" Spacing="6" Margin="0,10,0,0">
                        <Border Visibility="{x:Bind ReversibleVisibility, Mode=OneWay}"
                                Background="#1E1B4B" CornerRadius="4" Padding="8,2">
                            <TextBlock Text="Reversible" FontSize="9" Foreground="#A78BFA"/>
                        </Border>
                        <Border Visibility="{x:Bind AdminVisibility, Mode=OneWay}"
                                Background="#422006" CornerRadius="4" Padding="8,2">
                            <TextBlock Text="🛡️ Requires Admin" FontSize="9" Foreground="#FBBF24"/>
                        </Border>
                        <Border Visibility="{x:Bind RestartVisibility, Mode=OneWay}"
                                Background="#4C0519" CornerRadius="4" Padding="8,2">
                            <TextBlock Text="Requires Restart" FontSize="9" Foreground="#FDA4AF"/>
                        </Border>
                    </StackPanel>
                </Grid>
            </Expander.Content>
        </Expander>
    </Border>
</UserControl>
```

- [ ] **Step 2: Create OptimizationCard.xaml.cs**

Create `Optimizer.WinUI/Controls/OptimizationCard.xaml.cs`:
```csharp
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Controls;

public sealed partial class OptimizationCard : UserControl
{
    public event EventHandler<bool>? Toggled;

    public string Icon { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; }
    public bool RequiresAdmin { get; set; }
    public bool IsReversible { get; set; }
    public bool RequiresRestart { get; set; }
    public string ChangesText { get; set; } = "";
    public List<string> Pros { get; set; } = [];
    public List<string> Cons { get; set; } = [];
    public bool IsElevated { get; set; } = true;

    public Brush IconBackground => IsActive
        ? new SolidColorBrush(ColorHelper.FromArgb(255, 30, 58, 95))
        : new SolidColorBrush(ColorHelper.FromArgb(255, 31, 41, 55));

    public Brush TitleForeground => IsActive
        ? new SolidColorBrush(ColorHelper.FromArgb(255, 224, 224, 224))
        : new SolidColorBrush(ColorHelper.FromArgb(255, 156, 163, 175));

    public Visibility ActiveBadgeVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShieldVisibility => RequiresAdmin && !IsElevated ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReversibleVisibility => IsReversible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AdminVisibility => RequiresAdmin ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RestartVisibility => RequiresRestart ? Visibility.Visible : Visibility.Collapsed;
    public double ToggleOpacity => RequiresAdmin && !IsElevated ? 0.5 : 1.0;

    public OptimizationCard()
    {
        InitializeComponent();
    }

    public void LoadFromInfo(OptimizationInfo info, bool isActive, bool isElevated)
    {
        Title = info.Title;
        Description = info.Summary;
        IsActive = isActive;
        RequiresAdmin = info.RequiresAdmin;
        IsReversible = info.Reversible;
        RequiresRestart = info.RequiresRestart;
        ChangesText = string.Join("\n", info.Changes);
        Pros = info.Pros;
        Cons = info.Cons;
        IsElevated = isElevated;
        Bindings.Update();
    }

    private void Toggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch ts)
            Toggled?.Invoke(this, ts.IsOn);
    }
}
```

- [ ] **Step 3: Verify build**

Run:
```powershell
dotnet build L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj -c Debug
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```
git add Optimizer.WinUI/Controls/
git commit -m "feat: create OptimizationCard reusable control"
```

---

### Task 10: Build CategoryViewModelBase and PerformancePage (Template Category)

**Files:**
- Modify: `Optimizer.WinUI/ViewModels/CategoryViewModelBase.cs`
- Modify: `Optimizer.WinUI/ViewModels/PerformanceCategoryViewModel.cs`
- Modify: `Optimizer.WinUI/Views/PerformancePage.xaml`
- Modify: `Optimizer.WinUI/Views/PerformancePage.xaml.cs`

- [ ] **Step 1: Build CategoryViewModelBase**

Replace `Optimizer.WinUI/ViewModels/CategoryViewModelBase.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public abstract partial class CategoryViewModelBase : ObservableObject
{
    protected readonly IWindowsOptimizerService Optimizer;
    protected readonly IElevationService Elevation;
    protected readonly HistoryService History;

    [ObservableProperty] private int activeCount;
    [ObservableProperty] private int totalCount;

    public ObservableCollection<OptimizationCardModel> Optimizations { get; } = [];

    public abstract string CategoryName { get; }
    public abstract string CategoryIcon { get; }
    protected abstract string[] OptimizationIds { get; }

    protected CategoryViewModelBase(
        IWindowsOptimizerService optimizer,
        IElevationService elevation,
        HistoryService history)
    {
        Optimizer = optimizer;
        Elevation = elevation;
        History = history;
    }

    public virtual void Load()
    {
        Optimizations.Clear();
        foreach (var id in OptimizationIds)
        {
            var info = Optimizer.GetOptimizationInfo(id);
            if (info == null) continue;

            var isActive = Optimizer.IsOptimizationApplied(id) == true;
            Optimizations.Add(new OptimizationCardModel
            {
                Id = id,
                Info = info,
                IsActive = isActive,
                IsElevated = Elevation.IsElevated
            });
        }

        TotalCount = Optimizations.Count;
        ActiveCount = Optimizations.Count(o => o.IsActive);
    }

    public async Task ToggleOptimizationAsync(string id, bool enable)
    {
        if (enable)
        {
            var result = await Optimizer.ApplyOptimizationAsync(id);
            var info = Optimizer.GetOptimizationInfo(id);
            if (result.Success && info != null)
                History.RecordApplied(id, info.Title, CategoryName, info.Reversible);
        }
        else
        {
            var entry = Optimizer.GetUndoEntries().FirstOrDefault(e => e.Description.Contains(id));
            if (entry != null)
            {
                await Optimizer.UndoEntryAsync(entry);
                var info = Optimizer.GetOptimizationInfo(id);
                if (info != null)
                    History.RecordUndone(id, info.Title, CategoryName);
            }
        }
        Load(); // refresh state
    }

    [RelayCommand]
    public async Task ApplyAll()
    {
        foreach (var opt in Optimizations.Where(o => !o.IsActive))
            await ToggleOptimizationAsync(opt.Id, true);
    }

    [RelayCommand]
    public async Task UndoCategory()
    {
        foreach (var opt in Optimizations.Where(o => o.IsActive).ToList())
            await ToggleOptimizationAsync(opt.Id, false);
    }
}

public partial class OptimizationCardModel : ObservableObject
{
    public string Id { get; set; } = "";
    public OptimizationInfo Info { get; set; } = new();
    [ObservableProperty] private bool isActive;
    public bool IsElevated { get; set; }
}
```

- [ ] **Step 2: Build PerformanceCategoryViewModel**

Replace `Optimizer.WinUI/ViewModels/PerformanceCategoryViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class PerformanceCategoryViewModel : CategoryViewModelBase
{
    private readonly SystemMonitorService _monitor;

    [ObservableProperty] private double cpuUsage;
    [ObservableProperty] private double memoryUsage;
    [ObservableProperty] private long usedMemoryBytes;
    [ObservableProperty] private string powerPlan = "Unknown";

    public override string CategoryName => "Performance";
    public override string CategoryIcon => "⚡";

    protected override string[] OptimizationIds =>
    [
        "DisableBackgroundApps",
        "DisableAnimations",
        "DisableVisualEffects",
        "OptimizePowerSettings",
        "AdjustPageFileSize"
    ];

    public PerformanceCategoryViewModel(
        IWindowsOptimizerService optimizer,
        IElevationService elevation,
        HistoryService history,
        SystemMonitorService monitor)
        : base(optimizer, elevation, history)
    {
        _monitor = monitor;
    }

    public override void Load()
    {
        base.Load();
        RefreshMetrics();
    }

    public void RefreshMetrics()
    {
        var snapshot = _monitor.CollectSnapshot();
        CpuUsage = snapshot.CpuUsagePercentage;
        var totalMem = snapshot.TotalPhysicalMemory;
        var availMem = snapshot.AvailablePhysicalMemory;
        UsedMemoryBytes = totalMem - availMem;
        MemoryUsage = totalMem > 0 ? 100.0 * (totalMem - availMem) / totalMem : 0;
    }
}
```

- [ ] **Step 3: Build PerformancePage.xaml**

Replace `Optimizer.WinUI/Views/PerformancePage.xaml`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="Optimizer.WinUI.Views.PerformancePage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:Optimizer.WinUI.Controls"
    xmlns:vm="using:Optimizer.WinUI.ViewModels"
    xmlns:converters="using:Optimizer.WinUI.Converters"
    Background="Transparent"
    Loaded="Page_Loaded">

    <Page.Resources>
        <converters:BytesToStringConverter x:Key="BytesToString"/>
    </Page.Resources>

    <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="24">
        <StackPanel Spacing="20">

            <!-- Page Header -->
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <StackPanel Orientation="Horizontal" Spacing="12">
                    <TextBlock Text="{x:Bind ViewModel.CategoryIcon}" FontSize="24" VerticalAlignment="Center"/>
                    <StackPanel>
                        <TextBlock Text="{x:Bind ViewModel.CategoryName}" Style="{StaticResource TitleTextBlockStyle}"/>
                        <TextBlock Foreground="{ThemeResource TextFillColorSecondaryBrush}" FontSize="12">
                            <Run Text="{x:Bind ViewModel.ActiveCount, Mode=OneWay}"/>
                            <Run Text=" of "/>
                            <Run Text="{x:Bind ViewModel.TotalCount, Mode=OneWay}"/>
                            <Run Text=" optimizations active"/>
                        </TextBlock>
                    </StackPanel>
                </StackPanel>
                <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8">
                    <Button Content="Apply All" Command="{x:Bind ViewModel.ApplyAllCommand}"
                            Style="{StaticResource AccentButtonStyle}"/>
                    <Button Content="Undo Category" Command="{x:Bind ViewModel.UndoCategoryCommand}"/>
                </StackPanel>
            </Grid>

            <!-- Local Metrics -->
            <Grid ColumnSpacing="12">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>

                <Border Grid.Column="0" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                        CornerRadius="8" Padding="12" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                    <StackPanel>
                        <TextBlock Text="CPU USAGE" FontSize="10" Foreground="{ThemeResource TextFillColorTertiaryBrush}" CharacterSpacing="100"/>
                        <TextBlock FontSize="22" FontWeight="Bold" FontFamily="Cascadia Code" Foreground="#60A5FA" Margin="0,4,0,0">
                            <Run Text="{x:Bind ViewModel.CpuUsage, Mode=OneWay}"/><Run Text="%"/>
                        </TextBlock>
                    </StackPanel>
                </Border>

                <Border Grid.Column="1" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                        CornerRadius="8" Padding="12" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                    <StackPanel>
                        <TextBlock Text="MEMORY" FontSize="10" Foreground="{ThemeResource TextFillColorTertiaryBrush}" CharacterSpacing="100"/>
                        <TextBlock FontSize="22" FontWeight="Bold" FontFamily="Cascadia Code" Foreground="#34D399" Margin="0,4,0,0"
                                   Text="{x:Bind ViewModel.UsedMemoryBytes, Mode=OneWay, Converter={StaticResource BytesToString}}"/>
                    </StackPanel>
                </Border>

                <Border Grid.Column="2" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                        CornerRadius="8" Padding="12" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                    <StackPanel>
                        <TextBlock Text="POWER PLAN" FontSize="10" Foreground="{ThemeResource TextFillColorTertiaryBrush}" CharacterSpacing="100"/>
                        <TextBlock Text="{x:Bind ViewModel.PowerPlan, Mode=OneWay}" FontSize="16" FontWeight="SemiBold"
                                   Foreground="#F59E0B" Margin="0,4,0,0"/>
                    </StackPanel>
                </Border>
            </Grid>

            <!-- Optimizations -->
            <TextBlock Text="OPTIMIZATIONS" FontSize="11" Foreground="{ThemeResource TextFillColorDisabledBrush}" CharacterSpacing="100"/>

            <ItemsRepeater ItemsSource="{x:Bind ViewModel.Optimizations, Mode=OneWay}">
                <ItemsRepeater.Layout>
                    <StackLayout Spacing="8"/>
                </ItemsRepeater.Layout>
                <ItemsRepeater.ItemTemplate>
                    <DataTemplate x:DataType="vm:OptimizationCardModel">
                        <controls:OptimizationCard
                            x:Name="Card"
                            Loaded="OptimizationCard_Loaded"
                            Tag="{x:Bind Id}"/>
                    </DataTemplate>
                </ItemsRepeater.ItemTemplate>
            </ItemsRepeater>

        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 4: Build PerformancePage.xaml.cs**

Replace `Optimizer.WinUI/Views/PerformancePage.xaml.cs`:
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.Controls;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class PerformancePage : Page
{
    public PerformanceCategoryViewModel ViewModel { get; }

    public PerformancePage()
    {
        ViewModel = App.GetService<PerformanceCategoryViewModel>();
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
    }

    private void OptimizationCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is OptimizationCard card && card.Tag is string id)
        {
            var model = ViewModel.Optimizations.FirstOrDefault(o => o.Id == id);
            if (model != null)
            {
                card.LoadFromInfo(model.Info, model.IsActive, model.IsElevated);
                card.Toggled += async (_, isOn) =>
                {
                    await ViewModel.ToggleOptimizationAsync(id, isOn);
                };
            }
        }
    }
}
```

- [ ] **Step 5: Verify build and run — navigate to Performance page, see optimization cards**

Run:
```powershell
dotnet build L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj -c Debug && dotnet run --project L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj
```
Expected: Navigate to Performance page in sidebar. See local metrics (CPU, Memory, Power Plan) and 5 optimization cards with toggle switches, expandable details, impact lists, and requirement badges.

- [ ] **Step 6: Commit**

```
git add Optimizer.WinUI/ViewModels/CategoryViewModelBase.cs Optimizer.WinUI/ViewModels/PerformanceCategoryViewModel.cs Optimizer.WinUI/Views/PerformancePage.xaml Optimizer.WinUI/Views/PerformancePage.xaml.cs
git commit -m "feat: build category page template with Performance as reference implementation"
```

---

### Task 11: Build Remaining Category Pages (Network, Storage, System, Startup)

**Files:**
- Modify: `Optimizer.WinUI/ViewModels/NetworkCategoryViewModel.cs`
- Modify: `Optimizer.WinUI/ViewModels/StorageCategoryViewModel.cs`
- Modify: `Optimizer.WinUI/ViewModels/SystemCategoryViewModel.cs`
- Modify: `Optimizer.WinUI/ViewModels/StartupCategoryViewModel.cs`
- Modify: All corresponding View `.xaml` and `.xaml.cs` files

Each category ViewModel extends `CategoryViewModelBase` and only defines its `OptimizationIds`, `CategoryName`, `CategoryIcon`, and any category-specific local metrics. Each View follows the same XAML template as PerformancePage with different local metrics.

- [ ] **Step 1: Build NetworkCategoryViewModel**

Replace `Optimizer.WinUI/ViewModels/NetworkCategoryViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class NetworkCategoryViewModel : CategoryViewModelBase
{
    private readonly SystemMonitorService _monitor;

    [ObservableProperty] private double downloadSpeed;
    [ObservableProperty] private double uploadSpeed;

    public override string CategoryName => "Network";
    public override string CategoryIcon => "🌐";

    protected override string[] OptimizationIds =>
    [
        "OptimizeNetworkSettings",
        "FlushDnsCache"
    ];

    public NetworkCategoryViewModel(
        IWindowsOptimizerService optimizer, IElevationService elevation,
        HistoryService history, SystemMonitorService monitor)
        : base(optimizer, elevation, history)
    {
        _monitor = monitor;
    }

    public override void Load()
    {
        base.Load();
        var snapshot = _monitor.CollectSnapshot();
        DownloadSpeed = snapshot.NetworkInSpeed;
        UploadSpeed = snapshot.NetworkOutSpeed;
    }
}
```

- [ ] **Step 2: Build StorageCategoryViewModel**

Replace `Optimizer.WinUI/ViewModels/StorageCategoryViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class StorageCategoryViewModel : CategoryViewModelBase
{
    [ObservableProperty] private string diskUsageText = "";

    public override string CategoryName => "Storage";
    public override string CategoryIcon => "💾";

    protected override string[] OptimizationIds =>
    [
        "ClearTemporaryFiles",
        "ClearWindowsUpdateCache"
    ];

    public StorageCategoryViewModel(
        IWindowsOptimizerService optimizer, IElevationService elevation,
        HistoryService history)
        : base(optimizer, elevation, history) { }

    public override void Load()
    {
        base.Load();
        try
        {
            var tempPath = Path.GetTempPath();
            var tempSize = Directory.Exists(tempPath)
                ? new DirectoryInfo(tempPath).EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(f => { try { return f.Length; } catch { return 0L; } })
                : 0L;
            DiskUsageText = tempSize >= 1_073_741_824
                ? $"{tempSize / 1073741824.0:F1} GB"
                : $"{tempSize / 1048576.0:F0} MB";
        }
        catch { DiskUsageText = "N/A"; }
    }
}
```

- [ ] **Step 3: Build SystemCategoryViewModel**

Replace `Optimizer.WinUI/ViewModels/SystemCategoryViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class SystemCategoryViewModel : CategoryViewModelBase
{
    [ObservableProperty] private string telemetryStatus = "Unknown";

    public override string CategoryName => "System";
    public override string CategoryIcon => "🖥️";

    protected override string[] OptimizationIds =>
    [
        "DisableTelemetry",
        "DisableConsumerFeatures",
        "DisableHibernation"
    ];

    public SystemCategoryViewModel(
        IWindowsOptimizerService optimizer, IElevationService elevation,
        HistoryService history)
        : base(optimizer, elevation, history) { }

    public override void Load()
    {
        base.Load();
        TelemetryStatus = Optimizer.IsOptimizationApplied("DisableTelemetry") == true
            ? "Disabled" : "Active";
    }
}
```

- [ ] **Step 4: Build StartupCategoryViewModel**

Replace `Optimizer.WinUI/ViewModels/StartupCategoryViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class StartupCategoryViewModel : ObservableObject
{
    private readonly IStartupService _startupService;
    private readonly IElevationService _elevationService;

    [ObservableProperty] private int enabledCount;
    [ObservableProperty] private int totalCount;

    public ObservableCollection<StartupEntry> Entries { get; } = [];

    public string CategoryName => "Startup";
    public string CategoryIcon => "🚀";

    public StartupCategoryViewModel(IStartupService startupService, IElevationService elevationService)
    {
        _startupService = startupService;
        _elevationService = elevationService;
    }

    public void Load()
    {
        Entries.Clear();
        var entries = _startupService.GetEntries();
        foreach (var e in entries) Entries.Add(e);
        TotalCount = Entries.Count;
        EnabledCount = Entries.Count(e => e.Enabled);
    }

    [RelayCommand]
    public void ToggleEntry(StartupEntry entry)
    {
        _startupService.SetEnabled(entry, !entry.Enabled);
        Load();
    }
}
```

- [ ] **Step 5: Build the View XAML and code-behind for each remaining category**

Each follows the PerformancePage template. Create/update `NetworkPage.xaml`, `StoragePage.xaml`, `SystemPage.xaml`, `StartupPage.xaml` — each with:
- Page header with category icon, name, active count
- Local metrics strip (category-specific)
- `ItemsRepeater` of `OptimizationCard` controls (except Startup, which uses a `ListView` of `StartupEntry` with `ToggleSwitch`)

The `.xaml.cs` for each follows the same pattern as `PerformancePage.xaml.cs` — resolve the ViewModel from DI, call `Load()` on `Page_Loaded`, wire up `OptimizationCard_Loaded`.

For `StartupPage`, replace the `ItemsRepeater` with a `ListView` of startup entries:
```xml
<ListView ItemsSource="{x:Bind ViewModel.Entries, Mode=OneWay}" SelectionMode="None">
    <ListView.ItemTemplate>
        <DataTemplate x:DataType="models:StartupEntry">
            <Grid Padding="12" ColumnSpacing="12">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <StackPanel>
                    <TextBlock Text="{x:Bind Name}" FontWeight="SemiBold"/>
                    <TextBlock Text="{x:Bind LocationText}" FontSize="11"
                               Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
                </StackPanel>
                <TextBlock Grid.Column="1" Text="🛡️" FontSize="12"
                           Visibility="{x:Bind RequiresAdmin}" VerticalAlignment="Center"/>
                <ToggleSwitch Grid.Column="2" IsOn="{x:Bind Enabled, Mode=OneWay}"
                              VerticalAlignment="Center" MinWidth="0"/>
            </Grid>
        </DataTemplate>
    </ListView.ItemTemplate>
</ListView>
```

- [ ] **Step 6: Verify build and test all category pages**

Run:
```powershell
dotnet build L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj -c Debug && dotnet run --project L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj
```
Expected: All 5 category pages show local metrics and optimization controls. Toggling optimizations works and changes persist.

- [ ] **Step 7: Commit**

```
git add Optimizer.WinUI/ViewModels/ Optimizer.WinUI/Views/
git commit -m "feat: build all category pages (Network, Storage, System, Startup)"
```

---

### Task 12: Build the Profiles Page

**Files:**
- Modify: `Optimizer.WinUI/ViewModels/ProfilesViewModel.cs`
- Modify: `Optimizer.WinUI/Views/ProfilesPage.xaml`
- Modify: `Optimizer.WinUI/Views/ProfilesPage.xaml.cs`

- [ ] **Step 1: Build ProfilesViewModel**

Replace `Optimizer.WinUI/ViewModels/ProfilesViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class ProfilesViewModel : ObservableObject
{
    private readonly ProfileService _profileService;

    public ObservableCollection<SettingsProfile> Presets { get; } = [];
    public ObservableCollection<SettingsProfile> Snapshots { get; } = [];

    public ProfilesViewModel(ProfileService profileService)
    {
        _profileService = profileService;
    }

    public void Load()
    {
        Presets.Clear();
        foreach (var p in _profileService.BuiltInPresets) Presets.Add(p);

        Snapshots.Clear();
        foreach (var s in _profileService.Snapshots) Snapshots.Add(s);
    }

    [RelayCommand]
    public async Task ApplyPreset(SettingsProfile preset)
    {
        await _profileService.ApplyPresetAsync(preset.Id);
        Load();
    }

    [RelayCommand]
    public async Task SaveSnapshot(string name)
    {
        await _profileService.SaveSnapshotAsync(name);
        Load();
    }

    [RelayCommand]
    public async Task RestoreSnapshot(SettingsProfile snapshot)
    {
        await _profileService.RestoreSnapshotAsync(snapshot);
        Load();
    }

    [RelayCommand]
    public void DeleteSnapshot(SettingsProfile snapshot)
    {
        _profileService.DeleteSnapshot(snapshot.Id);
        Load();
    }

    [RelayCommand]
    public void UpdateSnapshot(SettingsProfile snapshot)
    {
        _profileService.UpdateSnapshot(snapshot);
        Load();
    }
}
```

- [ ] **Step 2: Build ProfilesPage.xaml with preset grid and snapshot list**

Replace `Optimizer.WinUI/Views/ProfilesPage.xaml`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="Optimizer.WinUI.Views.ProfilesPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:models="using:Optimizer.WinUI.Models"
    Background="Transparent"
    Loaded="Page_Loaded">

    <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="24">
        <StackPanel Spacing="20">

            <!-- Header -->
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <StackPanel Orientation="Horizontal" Spacing="12">
                    <TextBlock Text="📋" FontSize="24" VerticalAlignment="Center"/>
                    <StackPanel>
                        <TextBlock Text="Profiles" Style="{StaticResource TitleTextBlockStyle}"/>
                        <TextBlock Text="Quick-apply presets or save your current configuration"
                                   Foreground="{ThemeResource TextFillColorSecondaryBrush}" FontSize="12"/>
                    </StackPanel>
                </StackPanel>
                <Button Grid.Column="1" Content="+ Save Current State" Click="SaveSnapshot_Click"
                        Style="{StaticResource AccentButtonStyle}"/>
            </Grid>

            <!-- Built-in Presets -->
            <TextBlock Text="BUILT-IN PRESETS" FontSize="11" Foreground="{ThemeResource TextFillColorDisabledBrush}" CharacterSpacing="100"/>
            <GridView ItemsSource="{x:Bind ViewModel.Presets, Mode=OneWay}" SelectionMode="None">
                <GridView.ItemTemplate>
                    <DataTemplate x:DataType="models:SettingsProfile">
                        <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                                CornerRadius="8" Padding="14" Width="280"
                                BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                            <StackPanel Spacing="8">
                                <TextBlock Text="{x:Bind Name}" FontSize="14" FontWeight="SemiBold"/>
                                <TextBlock Text="{x:Bind Description}" FontSize="11"
                                           Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                                           TextWrapping="Wrap" MaxLines="2"/>
                                <Grid>
                                    <TextBlock FontSize="10" Foreground="{ThemeResource TextFillColorDisabledBrush}">
                                        <Run Text="{x:Bind Optimizations.Count}"/><Run Text=" optimizations"/>
                                    </TextBlock>
                                    <Button Content="Apply" HorizontalAlignment="Right" FontSize="11"
                                            Click="ApplyPreset_Click" Tag="{x:Bind Id}"/>
                                </Grid>
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </GridView.ItemTemplate>
            </GridView>

            <!-- Saved Snapshots -->
            <TextBlock Text="SAVED SNAPSHOTS" FontSize="11" Foreground="{ThemeResource TextFillColorDisabledBrush}" CharacterSpacing="100"/>
            <ItemsRepeater ItemsSource="{x:Bind ViewModel.Snapshots, Mode=OneWay}">
                <ItemsRepeater.Layout>
                    <StackLayout Spacing="8"/>
                </ItemsRepeater.Layout>
                <ItemsRepeater.ItemTemplate>
                    <DataTemplate x:DataType="models:SettingsProfile">
                        <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                                CornerRadius="8" Padding="14"
                                BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <StackPanel>
                                    <TextBlock Text="{x:Bind Name}" FontSize="14" FontWeight="SemiBold"/>
                                    <TextBlock FontSize="11" Foreground="{ThemeResource TextFillColorSecondaryBrush}">
                                        <Run Text="Saved "/><Run Text="{x:Bind CreatedAt}"/>
                                        <Run Text=" · "/><Run Text="{x:Bind Optimizations.Count}"/>
                                        <Run Text=" optimizations"/>
                                    </TextBlock>
                                </StackPanel>
                                <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="6">
                                    <Button Content="Restore" FontSize="11" Click="RestoreSnapshot_Click" Tag="{x:Bind Id}"/>
                                    <Button Content="Update" FontSize="11" Click="UpdateSnapshot_Click" Tag="{x:Bind Id}"/>
                                    <Button Content="Delete" FontSize="11" Foreground="{ThemeResource SystemFillColorCriticalBrush}"
                                            Click="DeleteSnapshot_Click" Tag="{x:Bind Id}"/>
                                </StackPanel>
                            </Grid>
                        </Border>
                    </DataTemplate>
                </ItemsRepeater.ItemTemplate>
            </ItemsRepeater>

        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 3: Build ProfilesPage.xaml.cs**

Replace `Optimizer.WinUI/Views/ProfilesPage.xaml.cs`:
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class ProfilesPage : Page
{
    public ProfilesViewModel ViewModel { get; }

    public ProfilesPage()
    {
        ViewModel = App.GetService<ProfilesViewModel>();
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e) => ViewModel.Load();

    private async void SaveSnapshot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Save Current State",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        var input = new TextBox { PlaceholderText = "Snapshot name" };
        dialog.Content = input;

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
        {
            await ViewModel.SaveSnapshotCommand.ExecuteAsync(input.Text);
        }
    }

    private async void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            var preset = ViewModel.Presets.FirstOrDefault(p => p.Id == id);
            if (preset != null)
                await ViewModel.ApplyPresetCommand.ExecuteAsync(preset);
        }
    }

    private async void RestoreSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            var snapshot = ViewModel.Snapshots.FirstOrDefault(s => s.Id == id);
            if (snapshot != null)
                await ViewModel.RestoreSnapshotCommand.ExecuteAsync(snapshot);
        }
    }

    private void UpdateSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            var snapshot = ViewModel.Snapshots.FirstOrDefault(s => s.Id == id);
            if (snapshot != null)
                ViewModel.UpdateSnapshotCommand.Execute(snapshot);
        }
    }

    private void DeleteSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            var snapshot = ViewModel.Snapshots.FirstOrDefault(s => s.Id == id);
            if (snapshot != null)
                ViewModel.DeleteSnapshotCommand.Execute(snapshot);
        }
    }
}
```

- [ ] **Step 4: Verify build and test profiles — apply preset, save/restore/delete snapshots**

Run:
```powershell
dotnet build L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj -c Debug && dotnet run --project L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj
```
Expected: Profiles page shows preset cards and snapshot list. Apply, Save, Restore, Delete all work.

- [ ] **Step 5: Commit**

```
git add Optimizer.WinUI/ViewModels/ProfilesViewModel.cs Optimizer.WinUI/Views/ProfilesPage.xaml Optimizer.WinUI/Views/ProfilesPage.xaml.cs
git commit -m "feat: build profiles page with presets and snapshots"
```

---

### Task 13: Build the History Page

**Files:**
- Modify: `Optimizer.WinUI/ViewModels/HistoryViewModel.cs`
- Modify: `Optimizer.WinUI/Views/HistoryPage.xaml`
- Modify: `Optimizer.WinUI/Views/HistoryPage.xaml.cs`

- [ ] **Step 1: Build HistoryViewModel**

Replace `Optimizer.WinUI/ViewModels/HistoryViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryService _historyService;
    private readonly IWindowsOptimizerService _optimizer;

    [ObservableProperty] private int totalChanges;
    [ObservableProperty] private int reversibleCount;

    public ObservableCollection<HistoryDayGroup> DayGroups { get; } = [];

    public HistoryViewModel(HistoryService historyService, IWindowsOptimizerService optimizer)
    {
        _historyService = historyService;
        _optimizer = optimizer;
    }

    public void Load()
    {
        DayGroups.Clear();
        var entries = _historyService.Entries;
        TotalChanges = entries.Count;
        ReversibleCount = entries.Count(e => e.IsReversible && !e.IsUndone);

        var groups = entries
            .GroupBy(e => e.TimestampUtc.ToLocalTime().Date)
            .OrderByDescending(g => g.Key);

        foreach (var g in groups)
        {
            var label = g.Key.Date == DateTime.Today ? "Today"
                : g.Key.Date == DateTime.Today.AddDays(-1) ? "Yesterday"
                : g.Key.ToString("MMMM d, yyyy");

            DayGroups.Add(new HistoryDayGroup
            {
                DateLabel = label,
                Entries = new ObservableCollection<HistoryEntry>(g.OrderByDescending(e => e.TimestampUtc))
            });
        }
    }

    [RelayCommand]
    public async Task UndoAll()
    {
        await _optimizer.UndoAllOptimizationsAsync();
        Load();
    }
}

public class HistoryDayGroup
{
    public string DateLabel { get; set; } = "";
    public ObservableCollection<HistoryEntry> Entries { get; set; } = [];
}
```

- [ ] **Step 2: Build HistoryPage.xaml**

Replace `Optimizer.WinUI/Views/HistoryPage.xaml`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="Optimizer.WinUI.Views.HistoryPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:vm="using:Optimizer.WinUI.ViewModels"
    xmlns:models="using:Optimizer.WinUI.Models"
    Background="Transparent"
    Loaded="Page_Loaded">

    <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="24">
        <StackPanel Spacing="20">

            <!-- Header -->
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <StackPanel Orientation="Horizontal" Spacing="12">
                    <TextBlock Text="📜" FontSize="24" VerticalAlignment="Center"/>
                    <StackPanel>
                        <TextBlock Text="History" Style="{StaticResource TitleTextBlockStyle}"/>
                        <TextBlock Foreground="{ThemeResource TextFillColorSecondaryBrush}" FontSize="12">
                            <Run Text="{x:Bind ViewModel.TotalChanges, Mode=OneWay}"/>
                            <Run Text=" changes · "/>
                            <Run Text="{x:Bind ViewModel.ReversibleCount, Mode=OneWay}"/>
                            <Run Text=" reversible"/>
                        </TextBlock>
                    </StackPanel>
                </StackPanel>
                <Button Grid.Column="1" Command="{x:Bind ViewModel.UndoAllCommand}"
                        Foreground="{ThemeResource SystemFillColorCriticalBrush}">
                    <StackPanel Orientation="Horizontal" Spacing="4">
                        <TextBlock Text="↶ Undo All"/>
                    </StackPanel>
                </Button>
            </Grid>

            <!-- Change log grouped by day -->
            <ItemsRepeater ItemsSource="{x:Bind ViewModel.DayGroups, Mode=OneWay}">
                <ItemsRepeater.Layout>
                    <StackLayout Spacing="16"/>
                </ItemsRepeater.Layout>
                <ItemsRepeater.ItemTemplate>
                    <DataTemplate x:DataType="vm:HistoryDayGroup">
                        <StackPanel Spacing="6">
                            <TextBlock Text="{x:Bind DateLabel}" FontSize="11" FontWeight="SemiBold"
                                       Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
                            <ItemsRepeater ItemsSource="{x:Bind Entries}">
                                <ItemsRepeater.Layout>
                                    <StackLayout Spacing="4"/>
                                </ItemsRepeater.Layout>
                                <ItemsRepeater.ItemTemplate>
                                    <DataTemplate x:DataType="models:HistoryEntry">
                                        <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                                                CornerRadius="8" Padding="12,10"
                                                BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                                            <Grid ColumnSpacing="10">
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="Auto"/>
                                                    <ColumnDefinition Width="*"/>
                                                    <ColumnDefinition Width="Auto"/>
                                                </Grid.ColumnDefinitions>
                                                <Ellipse Grid.Column="0" Width="6" Height="6" VerticalAlignment="Center"
                                                         Fill="{ThemeResource SystemFillColorSuccessBrush}"/>
                                                <StackPanel Grid.Column="1">
                                                    <TextBlock Text="{x:Bind OptimizationTitle}" FontSize="13"/>
                                                    <TextBlock FontSize="10" Foreground="{ThemeResource TextFillColorDisabledBrush}">
                                                        <Run Text="{x:Bind TimestampUtc}"/>
                                                        <Run Text=" · "/>
                                                        <Run Text="{x:Bind Category}"/>
                                                    </TextBlock>
                                                </StackPanel>
                                                <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="6" VerticalAlignment="Center">
                                                    <Border Background="#1E1B4B" CornerRadius="3" Padding="6,2"
                                                            Visibility="{x:Bind IsReversible}">
                                                        <TextBlock Text="Reversible" FontSize="9" Foreground="#A78BFA"/>
                                                    </Border>
                                                </StackPanel>
                                            </Grid>
                                        </Border>
                                    </DataTemplate>
                                </ItemsRepeater.ItemTemplate>
                            </ItemsRepeater>
                        </StackPanel>
                    </DataTemplate>
                </ItemsRepeater.ItemTemplate>
            </ItemsRepeater>

        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 3: Build HistoryPage.xaml.cs**

Replace `Optimizer.WinUI/Views/HistoryPage.xaml.cs`:
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }

    public HistoryPage()
    {
        ViewModel = App.GetService<HistoryViewModel>();
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e) => ViewModel.Load();
}
```

- [ ] **Step 4: Verify build and run**

Run:
```powershell
dotnet build L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj -c Debug && dotnet run --project L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj
```
Expected: History page shows change log grouped by day. After toggling optimizations on category pages, new entries appear in history.

- [ ] **Step 5: Commit**

```
git add Optimizer.WinUI/ViewModels/HistoryViewModel.cs Optimizer.WinUI/Views/HistoryPage.xaml Optimizer.WinUI/Views/HistoryPage.xaml.cs
git commit -m "feat: build history page with grouped change log"
```

---

### Task 14: Build the Settings Page

**Files:**
- Modify: `Optimizer.WinUI/ViewModels/SettingsViewModel.cs`
- Modify: `Optimizer.WinUI/Views/SettingsPage.xaml`
- Modify: `Optimizer.WinUI/Views/SettingsPage.xaml.cs`

- [ ] **Step 1: Build SettingsViewModel**

Replace `Optimizer.WinUI/ViewModels/SettingsViewModel.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;

    [ObservableProperty] private string theme;
    [ObservableProperty] private string backdropMaterial;
    [ObservableProperty] private string accentColor;
    [ObservableProperty] private int refreshInterval;
    [ObservableProperty] private int chartHistory;
    [ObservableProperty] private bool startWithWindows;

    public string[] Themes { get; } = ["Dark", "Light", "Default"];
    public string[] Backdrops { get; } = ["Mica", "MicaAlt", "Acrylic", "None"];
    public int[] RefreshIntervals { get; } = [1, 2, 5, 10];
    public int[] ChartHistories { get; } = [60, 300, 900, 1800];
    public string ProfileStoragePath { get; }

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        var s = settingsService.Settings;
        theme = s.Theme;
        backdropMaterial = s.BackdropMaterial;
        accentColor = s.AccentColor;
        refreshInterval = s.MetricsRefreshSeconds;
        chartHistory = s.ChartHistorySeconds;
        startWithWindows = s.StartWithWindows;
        ProfileStoragePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Optimizer");
    }

    partial void OnThemeChanged(string value)
    {
        _settingsService.Settings.Theme = value;
        _settingsService.Save();
    }

    partial void OnBackdropMaterialChanged(string value)
    {
        _settingsService.Settings.BackdropMaterial = value;
        _settingsService.Save();
    }

    partial void OnRefreshIntervalChanged(int value)
    {
        _settingsService.Settings.MetricsRefreshSeconds = value;
        _settingsService.Save();
    }

    partial void OnChartHistoryChanged(int value)
    {
        _settingsService.Settings.ChartHistorySeconds = value;
        _settingsService.Save();
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        _settingsService.Settings.StartWithWindows = value;
        _settingsService.Save();
    }

    [RelayCommand]
    public void OpenStorageFolder()
    {
        System.Diagnostics.Process.Start("explorer.exe", ProfileStoragePath);
    }

    [RelayCommand]
    public void ResetSettings()
    {
        _settingsService.Reset();
        var s = _settingsService.Settings;
        Theme = s.Theme;
        BackdropMaterial = s.BackdropMaterial;
        AccentColor = s.AccentColor;
        RefreshInterval = s.MetricsRefreshSeconds;
        ChartHistory = s.ChartHistorySeconds;
        StartWithWindows = s.StartWithWindows;
    }
}
```

- [ ] **Step 2: Build SettingsPage.xaml with SettingsCard layout**

Replace `Optimizer.WinUI/Views/SettingsPage.xaml`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<Page
    x:Class="Optimizer.WinUI.Views.SettingsPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Background="Transparent">

    <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="24">
        <StackPanel Spacing="20" MaxWidth="800">

            <StackPanel Orientation="Horizontal" Spacing="12">
                <TextBlock Text="⚙️" FontSize="24" VerticalAlignment="Center"/>
                <TextBlock Text="Settings" Style="{StaticResource TitleTextBlockStyle}"/>
            </StackPanel>

            <!-- Appearance -->
            <TextBlock Text="APPEARANCE" FontSize="11" Foreground="{ThemeResource TextFillColorDisabledBrush}" CharacterSpacing="100"/>

            <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                    CornerRadius="8" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                <StackPanel>
                    <Grid Padding="16" BorderBrush="{ThemeResource DividerStrokeColorDefaultBrush}">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <StackPanel><TextBlock Text="Theme"/><TextBlock Text="App color scheme" FontSize="11" Foreground="{ThemeResource TextFillColorSecondaryBrush}"/></StackPanel>
                        <ComboBox Grid.Column="1" ItemsSource="{x:Bind ViewModel.Themes}" SelectedItem="{x:Bind ViewModel.Theme, Mode=TwoWay}" MinWidth="120"/>
                    </Grid>
                    <Border Height="1" Background="{ThemeResource DividerStrokeColorDefaultBrush}"/>
                    <Grid Padding="16">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <StackPanel><TextBlock Text="Backdrop Material"/><TextBlock Text="Window background effect" FontSize="11" Foreground="{ThemeResource TextFillColorSecondaryBrush}"/></StackPanel>
                        <ComboBox Grid.Column="1" ItemsSource="{x:Bind ViewModel.Backdrops}" SelectedItem="{x:Bind ViewModel.BackdropMaterial, Mode=TwoWay}" MinWidth="120"/>
                    </Grid>
                </StackPanel>
            </Border>

            <!-- Monitoring -->
            <TextBlock Text="MONITORING" FontSize="11" Foreground="{ThemeResource TextFillColorDisabledBrush}" CharacterSpacing="100"/>

            <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                    CornerRadius="8" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                <StackPanel>
                    <Grid Padding="16">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <StackPanel><TextBlock Text="Refresh Interval"/><TextBlock Text="How often to poll system metrics" FontSize="11" Foreground="{ThemeResource TextFillColorSecondaryBrush}"/></StackPanel>
                        <ComboBox Grid.Column="1" ItemsSource="{x:Bind ViewModel.RefreshIntervals}" SelectedItem="{x:Bind ViewModel.RefreshInterval, Mode=TwoWay}" MinWidth="120"/>
                    </Grid>
                    <Border Height="1" Background="{ThemeResource DividerStrokeColorDefaultBrush}"/>
                    <Grid Padding="16">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <StackPanel><TextBlock Text="Start with Windows"/><TextBlock Text="Launch optimizer on system startup" FontSize="11" Foreground="{ThemeResource TextFillColorSecondaryBrush}"/></StackPanel>
                        <ToggleSwitch IsOn="{x:Bind ViewModel.StartWithWindows, Mode=TwoWay}" MinWidth="0"/>
                    </Grid>
                </StackPanel>
            </Border>

            <!-- Data -->
            <TextBlock Text="DATA" FontSize="11" Foreground="{ThemeResource TextFillColorDisabledBrush}" CharacterSpacing="100"/>

            <Border Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                    CornerRadius="8" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                <StackPanel>
                    <Grid Padding="16">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <StackPanel><TextBlock Text="Profile Storage"/>
                            <TextBlock Text="{x:Bind ViewModel.ProfileStoragePath}" FontSize="11" FontFamily="Cascadia Code"
                                       Foreground="{ThemeResource TextFillColorSecondaryBrush}"/></StackPanel>
                        <Button Grid.Column="1" Content="Open Folder" Command="{x:Bind ViewModel.OpenStorageFolderCommand}"/>
                    </Grid>
                    <Border Height="1" Background="{ThemeResource DividerStrokeColorDefaultBrush}"/>
                    <Grid Padding="16">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <StackPanel><TextBlock Text="Reset All Settings"/><TextBlock Text="Restore app defaults (does not undo optimizations)" FontSize="11" Foreground="{ThemeResource TextFillColorSecondaryBrush}"/></StackPanel>
                        <Button Grid.Column="1" Content="Reset" Foreground="{ThemeResource SystemFillColorCriticalBrush}"
                                Command="{x:Bind ViewModel.ResetSettingsCommand}"/>
                    </Grid>
                </StackPanel>
            </Border>

            <!-- Version -->
            <TextBlock Text="Optimizer v2.0.0 · WinUI 3 · .NET 10" HorizontalAlignment="Center"
                       FontSize="11" Foreground="{ThemeResource TextFillColorDisabledBrush}" Margin="0,12"/>
        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 3: Build SettingsPage.xaml.cs**

Replace `Optimizer.WinUI/Views/SettingsPage.xaml.cs`:
```csharp
using Microsoft.UI.Xaml.Controls;
using Optimizer.WinUI.ViewModels;

namespace Optimizer.WinUI.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
    }
}
```

- [ ] **Step 4: Verify build and run — test all settings persist**

Run:
```powershell
dotnet build L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj -c Debug && dotnet run --project L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj
```
Expected: Settings page shows Appearance, Monitoring, and Data sections. Changing theme/backdrop/interval persists to `app-settings.json`. Reset clears to defaults.

- [ ] **Step 5: Commit**

```
git add Optimizer.WinUI/ViewModels/SettingsViewModel.cs Optimizer.WinUI/Views/SettingsPage.xaml Optimizer.WinUI/Views/SettingsPage.xaml.cs
git commit -m "feat: build settings page with appearance, monitoring, and data sections"
```

---

### Task 15: Integration Test and Polish

**Files:**
- Various fixes across all files

- [ ] **Step 1: Full build verification**

Run:
```powershell
dotnet build L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj -c Debug
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 2: Run the app and verify all navigation paths**

Run:
```powershell
dotnet run --project L:\Projects\Optimizer.WinUI\Optimizer.WinUI.csproj
```

Verify each page:
1. Dashboard — metrics update live, action buttons work
2. Performance — optimization cards render, toggles work, expand shows detail
3. Network — optimization cards render
4. Storage — optimization cards render
5. System — optimization cards render
6. Startup — startup entries list with toggles
7. Profiles — presets show, save/restore/delete snapshots
8. History — entries grouped by day, undo works
9. Settings — all dropdowns and toggles persist
10. Elevation — InfoBar shows correct state (amber if non-elevated)
11. Sidebar — all items navigate correctly, selected state highlights

- [ ] **Step 3: Fix any visual or functional issues found during testing**

Address issues like:
- Alignment problems in card layouts
- Missing data bindings
- Toggle state not reflecting correctly
- Scrolling issues on long pages

- [ ] **Step 4: Final commit**

```
git add -A
git commit -m "feat: complete WinUI 3 optimizer redesign — all pages functional"
```
