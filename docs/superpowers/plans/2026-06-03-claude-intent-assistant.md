# Claude-Powered Intent Assistant + Persistent Console — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in, bring-your-own-key Claude assistant that maps natural language to the app's existing actions via tool-use (propose → confirm → execute with undo), surfaced in a persistent docked console (Activity + Assistant tabs) that can pop out into its own window.

**Architecture:** A `ICommandRegistry` is the single source of truth for invokable actions plus their safety metadata; Claude's tool list is generated from it. `IClaudeClient` wraps the official Anthropic .NET SDK behind project-owned DTOs. `IAssistantService` runs the manual tool-use loop, gating confirmation for mutating commands. A DPAPI-backed `IApiKeyStore` holds the key. The console dock lives in `MainWindow` outside the page `Frame` so it persists across pages; a `ConsoleViewModel` renders the existing `IEventBus` stream.

**Tech Stack:** .NET 10 / WinUI 3, CommunityToolkit.Mvvm 8.4, CommunityToolkit.WinUI SettingsControls, xUnit 2.9 + Moq 4.20, official `Anthropic` NuGet SDK, Windows DPAPI (`System.Security.Cryptography.ProtectedData`).

**Spec:** `docs/superpowers/specs/2026-06-03-claude-intent-assistant-design.md`

---

## Conventions (apply to every task)

- **Build the app project:** `dotnet build Optimizer.WinUI/Optimizer.WinUI.csproj -c Debug`
- **Run tests:** `dotnet test Optimizer.WinUI.Tests/Optimizer.WinUI.Tests.csproj`
- **Tests:** xUnit (`[Fact]`, `Assert.Equal/True/False/NotNull/Single`), Moq (`new Mock<IFoo>()`, `.Setup(...).ReturnsAsync(...)`, `.Object`).
- **MVVM:** `[ObservableProperty]`, `[RelayCommand]`, `ObservableObject` from CommunityToolkit.Mvvm.
- **Data files:** `AppPaths.GetDataFile("name.json")`; call `AppPaths.EnsureFolderExists()` before writing.
- **DI:** register in `Optimizer.WinUI/App.xaml.cs` inside `.ConfigureServices((_, services) => { ... })`; resolve via `App.GetService<T>()`.
- **Namespaces:** services in `Optimizer.WinUI.Services.*`, models in `Optimizer.WinUI.Models.*`, viewmodels in `Optimizer.WinUI.ViewModels`.
- **Commit** after each task with the message shown in its final step.

---

# Batch F1 — Command registry + commands

Produces the action surface Claude will drive. No Claude/UI dependencies yet.

## Task F1.1: Command abstractions + registry

**Files:**
- Create: `Optimizer.WinUI/Services/Commands/IAppCommand.cs`
- Create: `Optimizer.WinUI/Services/Commands/ICommandRegistry.cs`
- Create: `Optimizer.WinUI/Services/Commands/CommandRegistry.cs`
- Test: `Optimizer.WinUI.Tests/CommandRegistryTests.cs`

- [ ] **Step 1: Write the abstractions**

`Optimizer.WinUI/Services/Commands/IAppCommand.cs`:
```csharp
using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

/// <summary>Result of executing an app command. Summary is human-readable and fed back to Claude as the tool_result.</summary>
public record CommandResult(bool Success, string Summary, object? Data = null)
{
    public static CommandResult Ok(string summary, object? data = null) => new(true, summary, data);
    public static CommandResult Fail(string summary) => new(false, summary);
}

/// <summary>A single invokable app capability. Self-describes for Claude tool generation and carries safety metadata.</summary>
public interface IAppCommand
{
    /// <summary>Stable snake_case id; doubles as the Claude tool name (e.g. "apply_profile").</summary>
    string Id { get; }

    /// <summary>Human-readable description used as the Claude tool description.</summary>
    string Description { get; }

    /// <summary>JSON Schema (object) for the tool input. Use an empty-object schema for no parameters.</summary>
    JsonElement ParametersSchema { get; }

    /// <summary>True if the command never changes the system (queries/navigation).</summary>
    bool IsReadOnly { get; }

    /// <summary>True if the command must be confirmed by the user before executing.</summary>
    bool RequiresConfirmation { get; }

    Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct);
}
```

`Optimizer.WinUI/Services/Commands/ICommandRegistry.cs`:
```csharp
namespace Optimizer.WinUI.Services.Commands;

public interface ICommandRegistry
{
    void Register(IAppCommand command);
    IReadOnlyList<IAppCommand> Commands { get; }
    IAppCommand? Find(string id);
}
```

`Optimizer.WinUI/Services/Commands/CommandRegistry.cs`:
```csharp
namespace Optimizer.WinUI.Services.Commands;

public sealed class CommandRegistry : ICommandRegistry
{
    private readonly List<IAppCommand> _commands = [];
    private readonly Dictionary<string, IAppCommand> _byId = new(StringComparer.Ordinal);

    public IReadOnlyList<IAppCommand> Commands => _commands;

    public void Register(IAppCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!_byId.TryAdd(command.Id, command))
            throw new InvalidOperationException($"Duplicate command id '{command.Id}'.");
        _commands.Add(command);
    }

    public IAppCommand? Find(string id) => _byId.GetValueOrDefault(id);
}
```

- [ ] **Step 2: Write the test**

`Optimizer.WinUI.Tests/CommandRegistryTests.cs`:
```csharp
using System.Text.Json;
using Optimizer.WinUI.Services.Commands;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class CommandRegistryTests
{
    private sealed class FakeCommand(string id, bool readOnly = true, bool confirm = false) : IAppCommand
    {
        public string Id => id;
        public string Description => $"desc-{id}";
        public JsonElement ParametersSchema => JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;
        public bool IsReadOnly => readOnly;
        public bool RequiresConfirmation => confirm;
        public Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
            => Task.FromResult(CommandResult.Ok("ran"));
    }

    [Fact]
    public void Register_then_Find_returns_command()
    {
        var r = new CommandRegistry();
        r.Register(new FakeCommand("get_metrics"));
        Assert.NotNull(r.Find("get_metrics"));
        Assert.Single(r.Commands);
    }

    [Fact]
    public void Find_unknown_returns_null()
    {
        var r = new CommandRegistry();
        Assert.Null(r.Find("nope"));
    }

    [Fact]
    public void Register_duplicate_id_throws()
    {
        var r = new CommandRegistry();
        r.Register(new FakeCommand("apply_profile"));
        Assert.Throws<InvalidOperationException>(() => r.Register(new FakeCommand("apply_profile")));
    }

    [Fact]
    public void Metadata_is_preserved()
    {
        var r = new CommandRegistry();
        r.Register(new FakeCommand("apply_profile", readOnly: false, confirm: true));
        var c = r.Find("apply_profile")!;
        Assert.False(c.IsReadOnly);
        Assert.True(c.RequiresConfirmation);
    }
}
```

- [ ] **Step 3: Run tests — expect PASS**

Run: `dotnet test Optimizer.WinUI.Tests/Optimizer.WinUI.Tests.csproj --filter CommandRegistryTests`
Expected: 4 passed.

- [ ] **Step 4: Commit**
```bash
git add Optimizer.WinUI/Services/Commands Optimizer.WinUI.Tests/CommandRegistryTests.cs
git commit -m "feat: F1.1 — command registry + IAppCommand abstraction"
```

---

## Task F1.2: Read-only commands

**Files:**
- Create: `Optimizer.WinUI/Services/Commands/SchemaJson.cs`
- Create: `Optimizer.WinUI/Services/Commands/GetMetricsCommand.cs`
- Create: `Optimizer.WinUI/Services/Commands/GetRecommendationsCommand.cs`
- Create: `Optimizer.WinUI/Services/Commands/RunDiagnosticsScanCommand.cs`
- Create: `Optimizer.WinUI/Services/Commands/GetBottlenecksCommand.cs`
- Create: `Optimizer.WinUI/Services/Commands/ListProfilesCommand.cs`
- Create: `Optimizer.WinUI/Services/Commands/IPageNavigator.cs`
- Create: `Optimizer.WinUI/Services/Commands/NavigateToPageCommand.cs`
- Test: `Optimizer.WinUI.Tests/AppCommandsReadTests.cs`

- [ ] **Step 1: Shared empty-object schema helper**

`Optimizer.WinUI/Services/Commands/SchemaJson.cs`:
```csharp
using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

/// <summary>Small helpers for building tool input schemas as JsonElement.</summary>
public static class SchemaJson
{
    /// <summary>A no-parameter object schema.</summary>
    public static JsonElement Empty { get; } =
        JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;

    /// <summary>Parse a JSON-schema string into a JsonElement (kept alive for the process).</summary>
    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}
```

- [ ] **Step 2: GetMetricsCommand** — `GetMetricsCommand.cs`:
```csharp
using System.Globalization;
using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class GetMetricsCommand(ISystemMonitorService monitor) : IAppCommand
{
    public string Id => "get_metrics";
    public string Description => "Get current CPU, memory, and GPU usage for this PC.";
    public JsonElement ParametersSchema => SchemaJson.Empty;
    public bool IsReadOnly => true;
    public bool RequiresConfirmation => false;

    public Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var s = monitor.CollectSnapshot();
        long usedMb = (s.TotalPhysicalMemory - s.AvailablePhysicalMemory) / (1024 * 1024);
        long totalMb = s.TotalPhysicalMemory / (1024 * 1024);
        var summary = string.Create(CultureInfo.InvariantCulture,
            $"CPU {s.CpuUsagePercentage:F0}%, GPU {s.GpuUsagePercentage:F0}%, memory {usedMb}/{totalMb} MB used.");
        return Task.FromResult(CommandResult.Ok(summary, new { cpu = s.CpuUsagePercentage, gpu = s.GpuUsagePercentage, usedMb, totalMb }));
    }
}
```
> Property names (`CollectSnapshot`, `CpuUsagePercentage`, `TotalPhysicalMemory`, `AvailablePhysicalMemory`, `GpuUsagePercentage`) are confirmed against `Services/ApiHostService.cs` `/api/metrics`.

- [ ] **Step 3: GetRecommendationsCommand** — `GetRecommendationsCommand.cs`:
```csharp
using System.Text;
using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class GetRecommendationsCommand(IRecommendationsService recs) : IAppCommand
{
    public string Id => "get_recommendations";
    public string Description => "List the current optimization and health recommendations for this PC.";
    public JsonElement ParametersSchema => SchemaJson.Empty;
    public bool IsReadOnly => true;
    public bool RequiresConfirmation => false;

    public async Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var list = await recs.GenerateAsync();
        if (list.Count == 0) return CommandResult.Ok("No recommendations right now — the system looks healthy.");
        var sb = new StringBuilder($"{list.Count} recommendation(s):");
        foreach (var r in list)
            sb.Append($"\n- [{r.Severity}] {r.Title} (id: {r.Id})");
        return CommandResult.Ok(sb.ToString(), list.Select(r => new { r.Id, r.Title, severity = r.Severity.ToString() }));
    }
}
```

- [ ] **Step 4: RunDiagnosticsScanCommand** — `RunDiagnosticsScanCommand.cs`:
```csharp
using System.Text;
using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class RunDiagnosticsScanCommand(IDiagnosticsService diagnostics) : IAppCommand
{
    public string Id => "run_diagnostics_scan";
    public string Description => "Run a full diagnostics scan and summarize the findings.";
    public JsonElement ParametersSchema => SchemaJson.Empty;
    public bool IsReadOnly => true;
    public bool RequiresConfirmation => false;

    public async Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var findings = await diagnostics.RunFullScanAsync();
        if (findings.Count == 0) return CommandResult.Ok("Diagnostics scan complete — no issues found.");
        var sb = new StringBuilder($"Diagnostics found {findings.Count} item(s):");
        foreach (var f in findings)
            sb.Append($"\n- [{f.Severity}] {f.Title}: {f.Recommendation}");
        return CommandResult.Ok(sb.ToString());
    }
}
```
> `IDiagnosticsService.RunFullScanAsync()` and finding fields (`Severity`, `Title`, `Recommendation`) are confirmed against `Services/RecommendationsService.cs` usage.

- [ ] **Step 5: GetBottlenecksCommand** — `GetBottlenecksCommand.cs`:
```csharp
using System.Text;
using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class GetBottlenecksCommand(IBottleneckDetectorService detector) : IAppCommand
{
    public string Id => "get_bottlenecks";
    public string Description => "Find the processes or subsystems currently bottlenecking this PC (what's eating CPU/RAM/disk).";
    public JsonElement ParametersSchema => SchemaJson.Empty;
    public bool IsReadOnly => true;
    public bool RequiresConfirmation => false;

    public async Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var report = await detector.DetectAsync();
        if (report.TopOffenders.Count == 0) return CommandResult.Ok("No significant bottlenecks detected.");
        var sb = new StringBuilder(report.Summary);
        foreach (var o in report.TopOffenders)
            sb.Append($"\n- {o.ProcessName} (pid {o.Pid}): {o.BottleneckType} {o.DisplayValue} [{o.Severity}]");
        return CommandResult.Ok(sb.ToString());
    }
}
```
> `BottleneckReport.TopOffenders` / `ProcessBottleneck` fields confirmed against `Models/BottleneckReport.cs`.

- [ ] **Step 6: ListProfilesCommand** — `ListProfilesCommand.cs`:
```csharp
using System.Text;
using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class ListProfilesCommand(IWindowsOptimizerService optimizer) : IAppCommand
{
    public string Id => "list_profiles";
    public string Description => "List the built-in optimization profiles/presets the user can apply (with their ids).";
    public JsonElement ParametersSchema => SchemaJson.Empty;
    public bool IsReadOnly => true;
    public bool RequiresConfirmation => false;

    public Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var presets = optimizer.GetBuiltInPresets();
        var sb = new StringBuilder($"{presets.Count} profile(s):");
        foreach (var p in presets)
            sb.Append($"\n- {p.Name} (id: {p.Id}) — {p.Description}");
        return Task.FromResult(CommandResult.Ok(sb.ToString(),
            presets.Select(p => new { p.Id, p.Name, p.Description })));
    }
}
```
> `GetBuiltInPresets()` and `SettingsProfile.{Id,Name,Description}` confirmed against `Services/ApiHostService.cs` `/api/profiles`.

- [ ] **Step 7: Page navigation abstraction + command**

`Optimizer.WinUI/Services/Commands/IPageNavigator.cs`:
```csharp
namespace Optimizer.WinUI.Services.Commands;

/// <summary>Abstraction over shell navigation so commands can navigate without depending on the Frame.</summary>
public interface IPageNavigator
{
    /// <summary>The user-facing page tags that can be navigated to (e.g. "Dashboard", "Diagnostics").</summary>
    IReadOnlyList<string> Pages { get; }

    /// <summary>Navigate to the page with the given tag (case-insensitive). Returns false if unknown.</summary>
    bool NavigateTo(string tag);
}
```

`Optimizer.WinUI/Services/Commands/NavigateToPageCommand.cs`:
```csharp
using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class NavigateToPageCommand(IPageNavigator navigator) : IAppCommand
{
    public string Id => "navigate_to_page";
    public string Description => "Open one of the app's pages so the user can see it.";
    public JsonElement ParametersSchema { get; } = SchemaJson.Parse("""
        {"type":"object",
         "properties":{"page":{"type":"string","description":"Page tag to open, e.g. Dashboard, Diagnostics, Updates, Security, Tuning, Profiles."}},
         "required":["page"]}
        """);
    public bool IsReadOnly => true;
    public bool RequiresConfirmation => false;

    public Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var page = args.TryGetProperty("page", out var p) ? p.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(page))
            return Task.FromResult(CommandResult.Fail("No page specified."));
        var ok = navigator.NavigateTo(page);
        return Task.FromResult(ok
            ? CommandResult.Ok($"Opened the {page} page.")
            : CommandResult.Fail($"Unknown page '{page}'. Known pages: {string.Join(", ", navigator.Pages)}"));
    }
}
```

- [ ] **Step 8: Write tests** — `Optimizer.WinUI.Tests/AppCommandsReadTests.cs`:
```csharp
using System.Text.Json;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Commands;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class AppCommandsReadTests
{
    private static JsonElement NoArgs => SchemaJson.Empty;

    [Fact]
    public async Task ListProfiles_summarizes_presets()
    {
        var opt = new Mock<IWindowsOptimizerService>();
        opt.Setup(o => o.GetBuiltInPresets()).Returns(new List<SettingsProfile>
        {
            new() { Id = "preset-gaming", Name = "Gaming", Description = "Fast" }
        });
        var cmd = new ListProfilesCommand(opt.Object);
        var result = await cmd.ExecuteAsync(NoArgs, default);
        Assert.True(result.Success);
        Assert.Contains("preset-gaming", result.Summary);
        Assert.True(cmd.IsReadOnly);
        Assert.False(cmd.RequiresConfirmation);
    }

    [Fact]
    public async Task GetRecommendations_handles_empty()
    {
        var recs = new Mock<IRecommendationsService>();
        recs.Setup(r => r.GenerateAsync()).ReturnsAsync(new List<Recommendation>());
        var cmd = new GetRecommendationsCommand(recs.Object);
        var result = await cmd.ExecuteAsync(NoArgs, default);
        Assert.True(result.Success);
        Assert.Contains("No recommendations", result.Summary);
    }

    [Fact]
    public async Task NavigateToPage_unknown_page_fails_with_known_list()
    {
        var nav = new Mock<IPageNavigator>();
        nav.Setup(n => n.Pages).Returns(new[] { "Dashboard", "Diagnostics" });
        nav.Setup(n => n.NavigateTo("Banana")).Returns(false);
        var cmd = new NavigateToPageCommand(nav.Object);
        var args = SchemaJson.Parse("""{"page":"Banana"}""");
        var result = await cmd.ExecuteAsync(args, default);
        Assert.False(result.Success);
        Assert.Contains("Dashboard", result.Summary);
    }

    [Fact]
    public async Task NavigateToPage_known_page_succeeds()
    {
        var nav = new Mock<IPageNavigator>();
        nav.Setup(n => n.NavigateTo("Diagnostics")).Returns(true);
        var cmd = new NavigateToPageCommand(nav.Object);
        var args = SchemaJson.Parse("""{"page":"Diagnostics"}""");
        var result = await cmd.ExecuteAsync(args, default);
        Assert.True(result.Success);
    }
}
```
> If `SettingsProfile` uses different property setters than `{ Id, Name, Description }`, adjust the test initializer to match `Models/SettingsProfile.cs` (read it first).

- [ ] **Step 9: Build + run tests**

Run: `dotnet build Optimizer.WinUI/Optimizer.WinUI.csproj -c Debug` then `dotnet test Optimizer.WinUI.Tests/Optimizer.WinUI.Tests.csproj --filter AppCommandsReadTests`
Expected: build succeeds; 4 passed. Fix any property-name mismatches by reading the referenced model/service file.

- [ ] **Step 10: Commit**
```bash
git add Optimizer.WinUI/Services/Commands Optimizer.WinUI.Tests/AppCommandsReadTests.cs
git commit -m "feat: F1.2 — read-only app commands (metrics, recommendations, diagnostics, bottlenecks, profiles, navigate)"
```

---

## Task F1.3: Confirm (mutating) commands

**Files:**
- Create: `Optimizer.WinUI/Services/Commands/ApplyProfileCommand.cs`
- Create: `Optimizer.WinUI/Services/Commands/ApplyOptimizationCommand.cs`
- Create: `Optimizer.WinUI/Services/Commands/RunCleanupCommand.cs`
- Create: `Optimizer.WinUI/Services/Commands/UndoLastCommand.cs`
- Test: `Optimizer.WinUI.Tests/AppCommandsMutatingTests.cs`

- [ ] **Step 1: ApplyProfileCommand** — `ApplyProfileCommand.cs`:
```csharp
using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class ApplyProfileCommand(IWindowsOptimizerService optimizer) : IAppCommand
{
    public string Id => "apply_profile";
    public string Description => "Apply a built-in optimization profile by its id (use list_profiles first to get ids). Changes are reversible.";
    public JsonElement ParametersSchema { get; } = SchemaJson.Parse("""
        {"type":"object",
         "properties":{"profile_id":{"type":"string","description":"The profile id, e.g. preset-privacy."}},
         "required":["profile_id"]}
        """);
    public bool IsReadOnly => false;
    public bool RequiresConfirmation => true;

    public async Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var id = args.TryGetProperty("profile_id", out var p) ? p.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(id)) return CommandResult.Fail("No profile_id supplied.");
        var ok = await optimizer.ApplyProfileAsync(id);
        return ok
            ? CommandResult.Ok($"Applied profile '{id}'. You can undo it from the History page or by asking me to undo.")
            : CommandResult.Fail($"Failed to apply profile '{id}'.");
    }
}
```

- [ ] **Step 2: ApplyOptimizationCommand** — `ApplyOptimizationCommand.cs`:
```csharp
using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class ApplyOptimizationCommand(IWindowsOptimizerService optimizer) : IAppCommand
{
    public string Id => "apply_optimization";
    public string Description => "Apply a single named optimization by its id. Changes are reversible.";
    public JsonElement ParametersSchema { get; } = SchemaJson.Parse("""
        {"type":"object",
         "properties":{"optimization_id":{"type":"string","description":"The optimization id."}},
         "required":["optimization_id"]}
        """);
    public bool IsReadOnly => false;
    public bool RequiresConfirmation => true;

    public async Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var id = args.TryGetProperty("optimization_id", out var p) ? p.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(id)) return CommandResult.Fail("No optimization_id supplied.");
        var result = await optimizer.ApplyOptimizationAsync(id);
        return result.Success
            ? CommandResult.Ok(string.IsNullOrWhiteSpace(result.Message) ? $"Applied '{id}'." : result.Message)
            : CommandResult.Fail(string.IsNullOrWhiteSpace(result.Message) ? $"Failed to apply '{id}'." : result.Message);
    }
}
```
> `ApplyOptimizationAsync` returns `OptimizationResult { Success, Message }` — confirmed in `Services/IWindowsOptimizerService.cs`.

- [ ] **Step 3: RunCleanupCommand** — `RunCleanupCommand.cs`:
```csharp
using System.Text.Json;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Commands;

public sealed class RunCleanupCommand(IWindowsOptimizerService optimizer) : IAppCommand
{
    public string Id => "run_cleanup";
    public string Description => "Clear temporary files to free disk space.";
    public JsonElement ParametersSchema => SchemaJson.Empty;
    public bool IsReadOnly => false;
    public bool RequiresConfirmation => true;

    public async Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var result = await optimizer.ApplyOptimizationAsync(OptimizationIds.ClearTemporaryFiles);
        return result.Success
            ? CommandResult.Ok(string.IsNullOrWhiteSpace(result.Message) ? "Temporary files cleared." : result.Message)
            : CommandResult.Fail(string.IsNullOrWhiteSpace(result.Message) ? "Cleanup failed." : result.Message);
    }
}
```
> `OptimizationIds.ClearTemporaryFiles` is the same constant `ApiHostService` `/api/cleanup` uses — confirmed in `Services/ApiHostService.cs`.

- [ ] **Step 4: UndoLastCommand** — `UndoLastCommand.cs`:
```csharp
using System.Text.Json;

namespace Optimizer.WinUI.Services.Commands;

public sealed class UndoLastCommand(IWindowsOptimizerService optimizer) : IAppCommand
{
    public string Id => "undo_last";
    public string Description => "Undo the most recent reversible change made by the app.";
    public JsonElement ParametersSchema => SchemaJson.Empty;
    public bool IsReadOnly => false;
    public bool RequiresConfirmation => true;

    public async Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var entries = optimizer.GetUndoEntries();
        if (entries.Count == 0) return CommandResult.Ok("Nothing to undo.");
        var last = entries[0];
        var ok = await optimizer.UndoEntryAsync(last);
        return ok
            ? CommandResult.Ok($"Reverted: {last.Description}")
            : CommandResult.Fail($"Could not revert: {last.Description}");
    }
}
```
> `GetUndoEntries()` is documented as "most-recent first when displayed" in `IWindowsOptimizerService`; `UndoEntry.Description` confirmed in `Services/IUndoService.cs`. Index `[0]` = most recent.

- [ ] **Step 5: Write tests** — `Optimizer.WinUI.Tests/AppCommandsMutatingTests.cs`:
```csharp
using Moq;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Commands;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class AppCommandsMutatingTests
{
    [Fact]
    public void Mutating_commands_require_confirmation_and_are_not_readonly()
    {
        var opt = new Mock<IWindowsOptimizerService>().Object;
        IAppCommand[] cmds =
        [
            new ApplyProfileCommand(opt),
            new ApplyOptimizationCommand(opt),
            new RunCleanupCommand(opt),
            new UndoLastCommand(opt),
        ];
        Assert.All(cmds, c => Assert.True(c.RequiresConfirmation));
        Assert.All(cmds, c => Assert.False(c.IsReadOnly));
    }

    [Fact]
    public async Task ApplyProfile_routes_id_to_service()
    {
        var opt = new Mock<IWindowsOptimizerService>();
        opt.Setup(o => o.ApplyProfileAsync("preset-privacy")).ReturnsAsync(true);
        var cmd = new ApplyProfileCommand(opt.Object);
        var args = SchemaJson.Parse("""{"profile_id":"preset-privacy"}""");
        var result = await cmd.ExecuteAsync(args, default);
        Assert.True(result.Success);
        opt.Verify(o => o.ApplyProfileAsync("preset-privacy"), Times.Once);
    }

    [Fact]
    public async Task ApplyProfile_missing_id_fails_without_calling_service()
    {
        var opt = new Mock<IWindowsOptimizerService>();
        var cmd = new ApplyProfileCommand(opt.Object);
        var result = await cmd.ExecuteAsync(SchemaJson.Empty, default);
        Assert.False(result.Success);
        opt.Verify(o => o.ApplyProfileAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UndoLast_with_no_entries_is_noop_ok()
    {
        var opt = new Mock<IWindowsOptimizerService>();
        opt.Setup(o => o.GetUndoEntries()).Returns(new List<UndoEntry>());
        var cmd = new UndoLastCommand(opt.Object);
        var result = await cmd.ExecuteAsync(SchemaJson.Empty, default);
        Assert.True(result.Success);
        Assert.Contains("Nothing to undo", result.Summary);
    }
}
```

- [ ] **Step 6: Build + run tests**

Run: `dotnet test Optimizer.WinUI.Tests/Optimizer.WinUI.Tests.csproj --filter AppCommandsMutatingTests`
Expected: 4 passed.

- [ ] **Step 7: Commit**
```bash
git add Optimizer.WinUI/Services/Commands Optimizer.WinUI.Tests/AppCommandsMutatingTests.cs
git commit -m "feat: F1.3 — mutating app commands (apply profile/optimization, cleanup, undo) with confirm metadata"
```

---

# Batch F2 — Claude SDK client + DPAPI key store

## Task F2.1: Add NuGet packages

**Files:**
- Modify: `Optimizer.WinUI/Optimizer.WinUI.csproj`

- [ ] **Step 1: Add the package references**

Add to the existing `<ItemGroup>` of `PackageReference`s in `Optimizer.WinUI/Optimizer.WinUI.csproj`:
```xml
    <PackageReference Include="Anthropic" Version="0.10.0" />
    <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="9.0.5" />
```
> Use the latest stable `Anthropic` version `dotnet add` resolves; pin whatever it writes. If `0.10.0` is unavailable, run `dotnet add Optimizer.WinUI/Optimizer.WinUI.csproj package Anthropic` and `dotnet add Optimizer.WinUI/Optimizer.WinUI.csproj package System.Security.Cryptography.ProtectedData` and keep the versions it picks.

- [ ] **Step 2: Restore + build**

Run: `dotnet build Optimizer.WinUI/Optimizer.WinUI.csproj -c Debug`
Expected: restore pulls `Anthropic` + `System.Security.Cryptography.ProtectedData`; build succeeds.

- [ ] **Step 3: Commit**
```bash
git add Optimizer.WinUI/Optimizer.WinUI.csproj
git commit -m "build: F2.1 — add Anthropic SDK + DPAPI ProtectedData packages"
```

---

## Task F2.2: DPAPI API-key store

**Files:**
- Create: `Optimizer.WinUI/Services/Assistant/IApiKeyStore.cs`
- Create: `Optimizer.WinUI/Services/Assistant/DpapiApiKeyStore.cs`
- Test: `Optimizer.WinUI.Tests/ApiKeyStoreTests.cs`

- [ ] **Step 1: Interface** — `Optimizer.WinUI/Services/Assistant/IApiKeyStore.cs`:
```csharp
namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Encrypted-at-rest storage for the user's Anthropic API key.</summary>
public interface IApiKeyStore
{
    bool HasKey { get; }
    void SetKey(string apiKey);
    string? GetKey();
    void Clear();
}
```

- [ ] **Step 2: Write the test first** — `Optimizer.WinUI.Tests/ApiKeyStoreTests.cs`:
```csharp
using System.IO;
using Optimizer.WinUI.Services.Assistant;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class ApiKeyStoreTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"optimizer-key-test-{Guid.NewGuid():N}.bin");

    [Fact]
    public void Set_Get_roundtrips_the_key()
    {
        var path = TempFile();
        try
        {
            var store = new DpapiApiKeyStore(path);
            Assert.False(store.HasKey);
            store.SetKey("sk-ant-test-123");
            Assert.True(store.HasKey);
            Assert.Equal("sk-ant-test-123", store.GetKey());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Stored_bytes_are_not_plaintext()
    {
        var path = TempFile();
        try
        {
            new DpapiApiKeyStore(path).SetKey("sk-ant-secret");
            var raw = File.ReadAllText(path);
            Assert.DoesNotContain("sk-ant-secret", raw);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Clear_removes_the_key()
    {
        var path = TempFile();
        try
        {
            var store = new DpapiApiKeyStore(path);
            store.SetKey("sk-ant-x");
            store.Clear();
            Assert.False(store.HasKey);
            Assert.Null(store.GetKey());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
```

- [ ] **Step 3: Run test — expect FAIL (type missing)**

Run: `dotnet test Optimizer.WinUI.Tests/Optimizer.WinUI.Tests.csproj --filter ApiKeyStoreTests`
Expected: compile error / fail — `DpapiApiKeyStore` not defined.

- [ ] **Step 4: Implementation** — `Optimizer.WinUI/Services/Assistant/DpapiApiKeyStore.cs`:
```csharp
using System.Security.Cryptography;
using System.Text;
using Optimizer.WinUI.Helpers;

namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Stores the Anthropic API key encrypted with Windows DPAPI (CurrentUser scope).</summary>
public sealed class DpapiApiKeyStore : IApiKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Optimizer.Assistant.ApiKey.v1");
    private readonly string _file;

    /// <summary>Production ctor — stores under %LocalAppData%\Optimizer\.</summary>
    public DpapiApiKeyStore() : this(AppPaths.GetDataFile("assistant-api-key.bin")) { }

    /// <summary>Test ctor — explicit path.</summary>
    public DpapiApiKeyStore(string file) => _file = file;

    public bool HasKey => File.Exists(_file);

    public void SetKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) { Clear(); return; }
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(apiKey), Entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, Convert.ToBase64String(protectedBytes));
    }

    public string? GetKey()
    {
        try
        {
            if (!File.Exists(_file)) return null;
            var protectedBytes = Convert.FromBase64String(File.ReadAllText(_file));
            var clear = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clear);
        }
        catch { return null; }
    }

    public void Clear()
    {
        try { if (File.Exists(_file)) File.Delete(_file); } catch { }
    }
}
```

- [ ] **Step 5: Run test — expect PASS**

Run: `dotnet test Optimizer.WinUI.Tests/Optimizer.WinUI.Tests.csproj --filter ApiKeyStoreTests`
Expected: 3 passed.

- [ ] **Step 6: Commit**
```bash
git add Optimizer.WinUI/Services/Assistant Optimizer.WinUI.Tests/ApiKeyStoreTests.cs
git commit -m "feat: F2.2 — DPAPI-encrypted API key store"
```

---

## Task F2.3: Claude DTOs + tool generator + mapper

**Files:**
- Create: `Optimizer.WinUI/Services/Assistant/ClaudeModels.cs`
- Create: `Optimizer.WinUI/Services/Assistant/ToolCatalog.cs`
- Test: `Optimizer.WinUI.Tests/ToolCatalogTests.cs`

- [ ] **Step 1: Project-owned DTOs** — `Optimizer.WinUI/Services/Assistant/ClaudeModels.cs`:
```csharp
using System.Text.Json;

namespace Optimizer.WinUI.Services.Assistant;

/// <summary>A tool definition handed to Claude (generated from the command registry).</summary>
public sealed record ClaudeToolDef(string Name, string Description, JsonElement InputSchema);

public enum ClaudeBlockKind { Text, ToolUse, ToolResult }

/// <summary>One content block in a Claude message (our own shape, decoupled from the SDK).</summary>
public sealed record ClaudeBlock(
    ClaudeBlockKind Kind,
    string? Text = null,
    string? ToolUseId = null,
    string? ToolName = null,
    JsonElement ToolInput = default,
    string? ToolResultContent = null,
    bool ToolResultIsError = false);

public sealed record ClaudeMessage(string Role, IReadOnlyList<ClaudeBlock> Content);

/// <summary>Outcome of one Claude turn.</summary>
public sealed record ClaudeTurn(string StopReason, IReadOnlyList<ClaudeBlock> Content);

public enum ClaudeErrorKind { None, Auth, RateLimit, Network, Other }
```

- [ ] **Step 2: Write the test first** — `Optimizer.WinUI.Tests/ToolCatalogTests.cs`:
```csharp
using System.Text.Json;
using Optimizer.WinUI.Services.Commands;
using Optimizer.WinUI.Services.Assistant;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class ToolCatalogTests
{
    private sealed class FakeCommand(string id, bool confirm) : IAppCommand
    {
        public string Id => id;
        public string Description => $"desc-{id}";
        public JsonElement ParametersSchema => SchemaJson.Empty;
        public bool IsReadOnly => !confirm;
        public bool RequiresConfirmation => confirm;
        public Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct) => Task.FromResult(CommandResult.Ok("x"));
    }

    [Fact]
    public void Build_maps_every_command_to_a_tool_def()
    {
        var reg = new CommandRegistry();
        reg.Register(new FakeCommand("get_metrics", confirm: false));
        reg.Register(new FakeCommand("apply_profile", confirm: true));

        var tools = ToolCatalog.Build(reg, allowActions: true);

        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, t => t.Name == "get_metrics" && t.Description == "desc-get_metrics");
        Assert.Contains(tools, t => t.Name == "apply_profile");
    }

    [Fact]
    public void Build_excludes_confirm_commands_when_actions_disabled()
    {
        var reg = new CommandRegistry();
        reg.Register(new FakeCommand("get_metrics", confirm: false));
        reg.Register(new FakeCommand("apply_profile", confirm: true));

        var tools = ToolCatalog.Build(reg, allowActions: false);

        Assert.Single(tools);
        Assert.Equal("get_metrics", tools[0].Name);
    }
}
```

- [ ] **Step 3: Implementation** — `Optimizer.WinUI/Services/Assistant/ToolCatalog.cs`:
```csharp
using Optimizer.WinUI.Services.Commands;

namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Generates Claude tool definitions from the command registry, honoring the allow-actions toggle.</summary>
public static class ToolCatalog
{
    public static IReadOnlyList<ClaudeToolDef> Build(ICommandRegistry registry, bool allowActions)
    {
        var tools = new List<ClaudeToolDef>();
        foreach (var c in registry.Commands)
        {
            // When the user has disabled actions, drop everything that would change the system.
            if (!allowActions && c.RequiresConfirmation) continue;
            tools.Add(new ClaudeToolDef(c.Id, c.Description, c.ParametersSchema));
        }
        return tools;
    }
}
```

- [ ] **Step 4: Run test — expect PASS**

Run: `dotnet test Optimizer.WinUI.Tests/Optimizer.WinUI.Tests.csproj --filter ToolCatalogTests`
Expected: 2 passed.

- [ ] **Step 5: Commit**
```bash
git add Optimizer.WinUI/Services/Assistant Optimizer.WinUI.Tests/ToolCatalogTests.cs
git commit -m "feat: F2.3 — Claude DTOs + registry→tool-def catalog (honors allow-actions)"
```

---

## Task F2.4: IClaudeClient over the Anthropic SDK

**Files:**
- Create: `Optimizer.WinUI/Services/Assistant/IClaudeClient.cs`
- Create: `Optimizer.WinUI/Services/Assistant/ClaudeClient.cs`

> The SDK glue is integration code (needs a real key + network), so it is **not** unit-tested. Keep it thin: translate DTOs → SDK params, stream, accumulate, translate back. The orchestration that *uses* it (Task F3) is fully tested against a fake `IClaudeClient`.

- [ ] **Step 1: Interface** — `Optimizer.WinUI/Services/Assistant/IClaudeClient.cs`:
```csharp
namespace Optimizer.WinUI.Services.Assistant;

public sealed record ClaudeResult(ClaudeTurn? Turn, ClaudeErrorKind Error, string? ErrorMessage);

public interface IClaudeClient
{
    /// <summary>True if an API key is configured (so the UI can show a setup prompt).</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Run one Claude turn. Streams assistant text via <paramref name="onText"/> as it arrives,
    /// then returns the full turn (including any tool_use blocks) or a mapped error.
    /// </summary>
    Task<ClaudeResult> SendAsync(
        string system,
        IReadOnlyList<ClaudeMessage> messages,
        IReadOnlyList<ClaudeToolDef> tools,
        string model,
        Action<string> onText,
        CancellationToken ct);
}
```

- [ ] **Step 2: Implementation** — `Optimizer.WinUI/Services/Assistant/ClaudeClient.cs`:
```csharp
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Wraps the official Anthropic .NET SDK behind <see cref="IClaudeClient"/>.</summary>
public sealed class ClaudeClient(IApiKeyStore keyStore) : IClaudeClient
{
    public bool IsConfigured => keyStore.HasKey;

    public async Task<ClaudeResult> SendAsync(
        string system,
        IReadOnlyList<ClaudeMessage> messages,
        IReadOnlyList<ClaudeToolDef> tools,
        string model,
        Action<string> onText,
        CancellationToken ct)
    {
        var key = keyStore.GetKey();
        if (string.IsNullOrWhiteSpace(key))
            return new ClaudeResult(null, ClaudeErrorKind.Auth, "No Anthropic API key is configured.");

        try
        {
            var client = new AnthropicClient { ApiKey = key };

            var parameters = new MessageCreateParams
            {
                Model = model,
                MaxTokens = 4096,
                // System prompt cached so repeated turns are cheaper/faster.
                System = new List<TextBlockParam>
                {
                    new() { Text = system, CacheControl = new CacheControlEphemeral() }
                },
                Tools = BuildTools(tools),
                Messages = BuildMessages(messages),
            };

            var collected = new List<ClaudeBlock>();
            var textBuf = new StringBuilder();
            string stopReason = "end_turn";

            // Stream text deltas to the UI; accumulate the full message for tool handling.
            await foreach (var ev in client.Messages.CreateStreaming(parameters).WithCancellation(ct))
            {
                if (ev.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var t))
                {
                    textBuf.Append(t.Text);
                    onText(t.Text);
                }
            }

            // Re-issue non-streaming to get the structured final message (tool_use blocks + stop_reason).
            // (Streaming gives us deltas; a single Create gives the typed blocks we route on.)
            var final = await client.Messages.Create(parameters);
            stopReason = final.StopReason ?? "end_turn";
            foreach (var block in final.Content)
            {
                if (block.TryPickText(out var txt))
                    collected.Add(new ClaudeBlock(ClaudeBlockKind.Text, Text: txt.Text));
                else if (block.TryPickToolUse(out var tu))
                    collected.Add(new ClaudeBlock(ClaudeBlockKind.ToolUse,
                        ToolUseId: tu.ID, ToolName: tu.Name,
                        ToolInput: JsonSerializer.SerializeToElement(tu.Input)));
            }

            return new ClaudeResult(new ClaudeTurn(stopReason, collected), ClaudeErrorKind.None, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var kind = ex.Message.Contains("401", StringComparison.Ordinal) ? ClaudeErrorKind.Auth
                     : ex.Message.Contains("429", StringComparison.Ordinal) ? ClaudeErrorKind.RateLimit
                     : ex is HttpRequestException ? ClaudeErrorKind.Network
                     : ClaudeErrorKind.Other;
            EngineLog.Error("Claude request failed", ex);   // key is never logged
            return new ClaudeResult(null, kind, FriendlyError(kind));
        }
    }

    private static string FriendlyError(ClaudeErrorKind kind) => kind switch
    {
        ClaudeErrorKind.Auth => "Your Anthropic API key was rejected. Check it in Settings → AI Assistant.",
        ClaudeErrorKind.RateLimit => "Anthropic rate limit hit. Wait a moment and try again.",
        ClaudeErrorKind.Network => "Couldn't reach Anthropic. Check your internet connection.",
        _ => "The assistant request failed. Please try again."
    };

    private static List<ToolUnion> BuildTools(IReadOnlyList<ClaudeToolDef> tools)
    {
        var list = new List<ToolUnion>();
        foreach (var t in tools)
        {
            var props = new Dictionary<string, JsonElement>();
            string[] required = [];
            if (t.InputSchema.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object)
                foreach (var prop in p.EnumerateObject())
                    props[prop.Name] = prop.Value;
            if (t.InputSchema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
                required = req.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToArray();

            list.Add(new Tool
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = new() { Properties = props, Required = required },
            });
        }
        return list;
    }

    private static List<MessageParam> BuildMessages(IReadOnlyList<ClaudeMessage> messages)
    {
        var result = new List<MessageParam>();
        foreach (var m in messages)
        {
            var blocks = new List<ContentBlockParam>();
            foreach (var b in m.Content)
            {
                switch (b.Kind)
                {
                    case ClaudeBlockKind.Text:
                        blocks.Add(new TextBlockParam { Text = b.Text ?? "" });
                        break;
                    case ClaudeBlockKind.ToolUse:
                        blocks.Add(new ToolUseBlockParam { ID = b.ToolUseId!, Name = b.ToolName!, Input = b.ToolInput });
                        break;
                    case ClaudeBlockKind.ToolResult:
                        blocks.Add(new ToolResultBlockParam
                        {
                            ToolUseID = b.ToolUseId!,
                            Content = b.ToolResultContent ?? "",
                            IsError = b.ToolResultIsError,
                        });
                        break;
                }
            }
            result.Add(new MessageParam { Role = m.Role == "assistant" ? Role.Assistant : Role.User, Content = blocks });
        }
        return result;
    }
}
```
> **Integration caveat:** exact SDK symbol names (`MessageCreateParams`, `Tool.InputSchema`, `ToolResultBlockParam.ToolUseID`, `TryPickToolUse`, `CreateStreaming`) are from the `claude-api` skill's C# reference. If the resolved `Anthropic` package version differs, fix the names against the package's IntelliSense — do not guess. This file does not need a unit test; it compiles as the contract check. The double call (stream for deltas, then `Create` for typed blocks) is a deliberate simplicity trade for a low-traffic desktop assistant; a later optimization can accumulate the streamed message instead.

- [ ] **Step 3: Build**

Run: `dotnet build Optimizer.WinUI/Optimizer.WinUI.csproj -c Debug`
Expected: compiles. Resolve any SDK symbol mismatches here against the installed package.

- [ ] **Step 4: Commit**
```bash
git add Optimizer.WinUI/Services/Assistant/IClaudeClient.cs Optimizer.WinUI/Services/Assistant/ClaudeClient.cs
git commit -m "feat: F2.4 — IClaudeClient over the official Anthropic .NET SDK (streaming + tool-use + caching)"
```

---

# Batch F3 — Assistant orchestration (tool-use loop + confirmation gating)

## Task F3.1: Conversation orchestration

**Files:**
- Create: `Optimizer.WinUI/Services/Assistant/IAssistantService.cs`
- Create: `Optimizer.WinUI/Services/Assistant/AssistantService.cs`
- Test: `Optimizer.WinUI.Tests/AssistantServiceTests.cs`

- [ ] **Step 1: Interface + callback contract** — `Optimizer.WinUI/Services/Assistant/IAssistantService.cs`:
```csharp
namespace Optimizer.WinUI.Services.Assistant;

/// <summary>UI callbacks the orchestrator uses to stream output and request confirmation.</summary>
public sealed class AssistantCallbacks
{
    /// <summary>Called with each streamed assistant text delta.</summary>
    public Action<string> OnAssistantText { get; init; } = _ => { };

    /// <summary>Called once per turn with a short status (e.g. "Running get_metrics…").</summary>
    public Action<string> OnStatus { get; init; } = _ => { };

    /// <summary>
    /// Called before a confirm-required command runs. Return true to execute, false to decline.
    /// Args summary is human-readable (command id + arguments).
    /// </summary>
    public Func<string, string, Task<bool>> ConfirmAsync { get; init; } = (_, _) => Task.FromResult(false);
}

public interface IAssistantService
{
    /// <summary>
    /// Send a user message and drive the tool-use loop to completion.
    /// Maintains conversation state across calls. Returns the final assistant text.
    /// </summary>
    Task<string> SendAsync(string userText, AssistantCallbacks callbacks, CancellationToken ct);

    /// <summary>Clear the conversation history.</summary>
    void Reset();
}
```

- [ ] **Step 2: Write the test first** — `Optimizer.WinUI.Tests/AssistantServiceTests.cs`:
```csharp
using System.Text.Json;
using Optimizer.WinUI.Services.Assistant;
using Optimizer.WinUI.Services.Commands;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class AssistantServiceTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────
    private sealed class ScriptedClaude(Queue<ClaudeResult> script) : IClaudeClient
    {
        public List<IReadOnlyList<ClaudeToolDef>> ToolsSeen { get; } = [];
        public bool IsConfigured => true;
        public Task<ClaudeResult> SendAsync(string system, IReadOnlyList<ClaudeMessage> messages,
            IReadOnlyList<ClaudeToolDef> tools, string model, Action<string> onText, CancellationToken ct)
        {
            ToolsSeen.Add(tools);
            var next = script.Dequeue();
            foreach (var b in next.Turn?.Content ?? [])
                if (b.Kind == ClaudeBlockKind.Text && b.Text is { } t) onText(t);
            return Task.FromResult(next);
        }
    }

    private sealed class RecordingCommand(string id, bool confirm) : IAppCommand
    {
        public int Executions { get; private set; }
        public string Id => id;
        public string Description => "d";
        public JsonElement ParametersSchema => SchemaJson.Empty;
        public bool IsReadOnly => !confirm;
        public bool RequiresConfirmation => confirm;
        public Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
        { Executions++; return Task.FromResult(CommandResult.Ok($"ran {id}")); }
    }

    private static ClaudeResult Text(string s) =>
        new(new ClaudeTurn("end_turn", [new ClaudeBlock(ClaudeBlockKind.Text, Text: s)]), ClaudeErrorKind.None, null);

    private static ClaudeResult ToolUse(string id, string toolName) =>
        new(new ClaudeTurn("tool_use",
            [new ClaudeBlock(ClaudeBlockKind.ToolUse, ToolUseId: id, ToolName: toolName, ToolInput: SchemaJson.Empty)]),
            ClaudeErrorKind.None, null);

    private static (AssistantService svc, ScriptedClaude claude, RecordingCommand cmd, CommandRegistry reg)
        Build(bool confirm, Queue<ClaudeResult> script, bool allowActions = true)
    {
        var reg = new CommandRegistry();
        var cmd = new RecordingCommand("apply_profile", confirm);
        reg.Register(cmd);
        var claude = new ScriptedClaude(script);
        var settings = new FakeAssistantSettings { AllowActions = allowActions, Model = "claude-sonnet-4-6" };
        return (new AssistantService(claude, reg, settings), claude, cmd, reg);
    }

    private sealed class FakeAssistantSettings : IAssistantSettings
    {
        public bool AllowActions { get; set; } = true;
        public string Model { get; set; } = "claude-sonnet-4-6";
    }

    // ── Tests ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Plain_text_turn_returns_text_and_streams()
    {
        var (svc, _, _, _) = Build(confirm: false, new Queue<ClaudeResult>([Text("Hello there")]));
        var streamed = "";
        var cb = new AssistantCallbacks { OnAssistantText = s => streamed += s };
        var final = await svc.SendAsync("hi", cb, default);
        Assert.Equal("Hello there", final);
        Assert.Equal("Hello there", streamed);
    }

    [Fact]
    public async Task Readonly_tool_executes_without_confirmation()
    {
        var script = new Queue<ClaudeResult>([ToolUse("t1", "apply_profile"), Text("done")]);
        var (svc, _, cmd, _) = Build(confirm: false, script);
        bool asked = false;
        var cb = new AssistantCallbacks { ConfirmAsync = (_, _) => { asked = true; return Task.FromResult(true); } };
        var final = await svc.SendAsync("go", cb, default);
        Assert.Equal(1, cmd.Executions);
        Assert.False(asked);
        Assert.Equal("done", final);
    }

    [Fact]
    public async Task Confirm_tool_runs_only_after_approval()
    {
        var script = new Queue<ClaudeResult>([ToolUse("t1", "apply_profile"), Text("applied")]);
        var (svc, _, cmd, _) = Build(confirm: true, script);
        var cb = new AssistantCallbacks { ConfirmAsync = (_, _) => Task.FromResult(true) };
        await svc.SendAsync("apply privacy", cb, default);
        Assert.Equal(1, cmd.Executions);
    }

    [Fact]
    public async Task Confirm_tool_skipped_when_declined()
    {
        var script = new Queue<ClaudeResult>([ToolUse("t1", "apply_profile"), Text("ok, skipped")]);
        var (svc, _, cmd, _) = Build(confirm: true, script);
        var cb = new AssistantCallbacks { ConfirmAsync = (_, _) => Task.FromResult(false) };
        await svc.SendAsync("apply privacy", cb, default);
        Assert.Equal(0, cmd.Executions);
    }

    [Fact]
    public async Task AllowActions_off_hides_confirm_tools_from_Claude()
    {
        var (svc, claude, _, _) = Build(confirm: true, new Queue<ClaudeResult>([Text("hi")]), allowActions: false);
        await svc.SendAsync("hi", new AssistantCallbacks(), default);
        Assert.Empty(claude.ToolsSeen[0]); // confirm-required tool filtered out
    }
}
```

- [ ] **Step 3: Settings abstraction** — add `Optimizer.WinUI/Services/Assistant/IAssistantSettings.cs`:
```csharp
namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Read access to the assistant-related user settings.</summary>
public interface IAssistantSettings
{
    bool AllowActions { get; }
    string Model { get; }
}
```

- [ ] **Step 4: Implementation** — `Optimizer.WinUI/Services/Assistant/AssistantService.cs`:
```csharp
using System.Text.Json;
using Optimizer.WinUI.Services.Commands;

namespace Optimizer.WinUI.Services.Assistant;

public sealed class AssistantService(
    IClaudeClient claude,
    ICommandRegistry registry,
    IAssistantSettings settings) : IAssistantService
{
    private const int MaxToolRounds = 8;

    private const string SystemPrompt =
        "You are the assistant inside Optimizer, a Windows PC optimization app. " +
        "Use the provided tools to answer questions about the user's PC and to perform actions they request. " +
        "Read-only tools run immediately. Tools that change the system require user confirmation, which the app handles — " +
        "call them normally and the app will prompt the user. Be concise. If a tool returns an error, explain it plainly. " +
        "Prefer calling list_profiles before apply_profile so you use a real id.";

    private readonly List<ClaudeMessage> _history = [];

    public void Reset() => _history.Clear();

    public async Task<string> SendAsync(string userText, AssistantCallbacks cb, CancellationToken ct)
    {
        _history.Add(new ClaudeMessage("user", [new ClaudeBlock(ClaudeBlockKind.Text, Text: userText)]));
        var tools = ToolCatalog.Build(registry, settings.AllowActions);

        for (int round = 0; round < MaxToolRounds; round++)
        {
            var result = await claude.SendAsync(SystemPrompt, _history, tools, settings.Model, cb.OnAssistantText, ct);
            if (result.Error != ClaudeErrorKind.None || result.Turn is null)
            {
                var msg = result.ErrorMessage ?? "The assistant request failed.";
                cb.OnAssistantText(msg);
                return msg;
            }

            var turn = result.Turn;
            _history.Add(new ClaudeMessage("assistant", turn.Content));

            var toolUses = turn.Content.Where(b => b.Kind == ClaudeBlockKind.ToolUse).ToList();
            if (turn.StopReason != "tool_use" || toolUses.Count == 0)
                return JoinText(turn.Content);

            var toolResults = new List<ClaudeBlock>();
            foreach (var use in toolUses)
            {
                var cmd = registry.Find(use.ToolName!);
                if (cmd is null)
                {
                    toolResults.Add(ToolError(use.ToolUseId!, $"Unknown command '{use.ToolName}'."));
                    continue;
                }

                if (cmd.RequiresConfirmation)
                {
                    var summary = $"{cmd.Id} {RenderArgs(use.ToolInput)}".Trim();
                    var approved = await cb.ConfirmAsync(cmd.Id, summary);
                    if (!approved)
                    {
                        toolResults.Add(ToolError(use.ToolUseId!, "User declined this action.", isError: false));
                        continue;
                    }
                }

                cb.OnStatus($"Running {cmd.Id}…");
                try
                {
                    var r = await cmd.ExecuteAsync(use.ToolInput, ct);
                    toolResults.Add(new ClaudeBlock(ClaudeBlockKind.ToolResult,
                        ToolUseId: use.ToolUseId!, ToolResultContent: r.Summary, ToolResultIsError: !r.Success));
                }
                catch (Exception ex)
                {
                    toolResults.Add(ToolError(use.ToolUseId!, $"Command threw: {ex.Message}"));
                }
            }

            _history.Add(new ClaudeMessage("user", toolResults));
            // loop: feed results back to Claude
        }

        var fallback = "Stopped after too many tool rounds.";
        cb.OnAssistantText(fallback);
        return fallback;
    }

    private static ClaudeBlock ToolError(string id, string text, bool isError = true) =>
        new(ClaudeBlockKind.ToolResult, ToolUseId: id, ToolResultContent: text, ToolResultIsError: isError);

    private static string JoinText(IEnumerable<ClaudeBlock> blocks) =>
        string.Join("", blocks.Where(b => b.Kind == ClaudeBlockKind.Text).Select(b => b.Text)).Trim();

    private static string RenderArgs(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object) return "";
        var parts = input.EnumerateObject().Select(p => $"{p.Name}={p.Value}");
        var joined = string.Join(", ", parts);
        return joined.Length == 0 ? "" : $"({joined})";
    }
}
```

- [ ] **Step 5: Run tests — expect PASS**

Run: `dotnet test Optimizer.WinUI.Tests/Optimizer.WinUI.Tests.csproj --filter AssistantServiceTests`
Expected: 5 passed.

- [ ] **Step 6: Commit**
```bash
git add Optimizer.WinUI/Services/Assistant/IAssistantService.cs Optimizer.WinUI/Services/Assistant/IAssistantSettings.cs Optimizer.WinUI/Services/Assistant/AssistantService.cs Optimizer.WinUI.Tests/AssistantServiceTests.cs
git commit -m "feat: F3.1 — assistant orchestration: tool-use loop with confirmation gating + allow-actions enforcement"
```

---

# Batch F4 — UI: console dock, tabs, pop-out, omnibox, settings, DI

## Task F4.1: EngineLog line event + ConsoleViewModel

**Files:**
- Modify: `Optimizer.WinUI/Services/EngineLog.cs`
- Create: `Optimizer.WinUI/Models/ConsoleLine.cs`
- Create: `Optimizer.WinUI/ViewModels/ConsoleViewModel.cs`
- Test: `Optimizer.WinUI.Tests/ConsoleViewModelTests.cs`

- [ ] **Step 1: Add a non-invasive line event to EngineLog**

In `Optimizer.WinUI/Services/EngineLog.cs`, add an event raised alongside the existing `_sink`. Edit `Write` and `Error`:
```csharp
public static event Action<string, Exception?>? LineWritten;

public static void Write(string message)
{
    System.Diagnostics.Debug.WriteLine(message);
    _sink?.Invoke(message, null);
    LineWritten?.Invoke(message, null);
}

public static void Error(string message, Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"{message}: {ex.Message}");
    _sink?.Invoke(message, ex);
    LineWritten?.Invoke(message, ex);
}
```
> Leaves the existing `Configure(...)` Serilog path untouched; the console just adds a second listener.

- [ ] **Step 2: ConsoleLine model** — `Optimizer.WinUI/Models/ConsoleLine.cs`:
```csharp
namespace Optimizer.WinUI.Models;

public sealed class ConsoleLine
{
    public DateTime TimestampLocal { get; init; } = DateTime.Now;
    public string Glyph { get; init; } = "•";
    public string Text { get; init; } = "";
    public string Color { get; init; } = "#9CA3AF";

    public string TimeText => TimestampLocal.ToString("HH:mm:ss");
}
```

- [ ] **Step 3: Write the test first** — `Optimizer.WinUI.Tests/ConsoleViewModelTests.cs`:
```csharp
using Moq;
using Optimizer.WinUI.Services.Events;
using Optimizer.WinUI.ViewModels;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class ConsoleViewModelTests
{
    private sealed class FakeBus : IEventBus
    {
        private readonly List<Action<OptimizerEvent>> _subs = [];
        public List<OptimizerEvent> Recent { get; } = [];
        public IReadOnlyList<OptimizerEvent> RecentEvents => Recent;
        public void Publish(OptimizerEvent evt) { foreach (var s in _subs) s(evt); }
        public IDisposable Subscribe(Action<OptimizerEvent> h) { _subs.Add(h); return new Noop(); }
        public IDisposable Subscribe(OptimizerEventType t, Action<OptimizerEvent> h)
            => Subscribe(e => { if (e.Type == t) h(e); });
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    [Fact]
    public void Seeds_from_recent_events_on_construction()
    {
        var bus = new FakeBus();
        bus.Recent.Add(OptimizerEvent.Create(OptimizerEventType.ProfileApplied, "Applied Gaming", "ok"));
        var vm = new ConsoleViewModel(bus, dispatch: a => a());
        Assert.Single(vm.Lines);
        Assert.Contains("Applied Gaming", vm.Lines[0].Text);
    }

    [Fact]
    public void Appends_a_line_when_an_event_is_published()
    {
        var bus = new FakeBus();
        var vm = new ConsoleViewModel(bus, dispatch: a => a());
        bus.Publish(OptimizerEvent.Create(OptimizerEventType.OptimizationApplied, "Disabled telemetry", "done"));
        Assert.Single(vm.Lines);
        Assert.Contains("Disabled telemetry", vm.Lines[0].Text);
    }

    [Fact]
    public void Clear_empties_the_log()
    {
        var bus = new FakeBus();
        var vm = new ConsoleViewModel(bus, dispatch: a => a());
        bus.Publish(OptimizerEvent.Create(OptimizerEventType.OptimizationApplied, "x", "y"));
        vm.ClearCommand.Execute(null);
        Assert.Empty(vm.Lines);
    }
}
```

- [ ] **Step 4: Implementation** — `Optimizer.WinUI/ViewModels/ConsoleViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Events;

namespace Optimizer.WinUI.ViewModels;

public partial class ConsoleViewModel : ObservableObject
{
    private readonly Action<Action> _dispatch;   // marshal onto the UI thread (or run inline in tests)

    [ObservableProperty] private bool showVerboseLogs;

    public ObservableCollection<ConsoleLine> Lines { get; } = [];

    /// <param name="dispatch">Runs an action on the UI thread. In tests, pass <c>a =&gt; a()</c>.</param>
    public ConsoleViewModel(IEventBus bus, Action<Action> dispatch)
    {
        _dispatch = dispatch;

        foreach (var e in bus.RecentEvents)
            Lines.Add(ToLine(e));

        bus.Subscribe(e => _dispatch(() => Lines.Add(ToLine(e))));
        EngineLog.LineWritten += (msg, ex) =>
        {
            if (!ShowVerboseLogs) return;
            _dispatch(() => Lines.Add(new ConsoleLine
            {
                Glyph = ex is null ? "›" : "⨯",
                Text = ex is null ? msg : $"{msg}: {ex.Message}",
                Color = ex is null ? "#9CA3AF" : "#EF4444",
            }));
        };
    }

    [RelayCommand]
    private void Clear() => Lines.Clear();

    private static ConsoleLine ToLine(OptimizerEvent e) => new()
    {
        TimestampLocal = e.TimestampUtc.ToLocalTime(),
        Glyph = GlyphFor(e.Type),
        Text = string.IsNullOrWhiteSpace(e.Detail) ? e.Title : $"{e.Title} — {e.Detail}",
        Color = ColorFor(e.Type),
    };

    private static string GlyphFor(OptimizerEventType t) => t switch
    {
        OptimizerEventType.OptimizationApplied => "✓",
        OptimizerEventType.OptimizationUndone => "↶",
        OptimizerEventType.ProfileApplied => "▣",
        OptimizerEventType.PluginInstalled or OptimizerEventType.PluginEnabled => "⊕",
        OptimizerEventType.AnomalyDetected => "⚠",
        OptimizerEventType.ThresholdCrossed => "▲",
        OptimizerEventType.DiagnosticCompleted => "✓",
        OptimizerEventType.CloudSyncCompleted => "☁",
        _ => "•"
    };

    private static string ColorFor(OptimizerEventType t) => t switch
    {
        OptimizerEventType.AnomalyDetected => "#EF4444",
        OptimizerEventType.ThresholdCrossed => "#F59E0B",
        _ => "#9CA3AF"
    };
}
```

- [ ] **Step 5: Run tests — expect PASS**

Run: `dotnet test Optimizer.WinUI.Tests/Optimizer.WinUI.Tests.csproj --filter ConsoleViewModelTests`
Expected: 3 passed.

- [ ] **Step 6: Commit**
```bash
git add Optimizer.WinUI/Services/EngineLog.cs Optimizer.WinUI/Models/ConsoleLine.cs Optimizer.WinUI/ViewModels/ConsoleViewModel.cs Optimizer.WinUI.Tests/ConsoleViewModelTests.cs
git commit -m "feat: F4.1 — ConsoleViewModel on the event bus + EngineLog line event"
```

---

## Task F4.2: AssistantViewModel

**Files:**
- Create: `Optimizer.WinUI/Models/ChatMessage.cs`
- Create: `Optimizer.WinUI/ViewModels/AssistantViewModel.cs`
- Test: `Optimizer.WinUI.Tests/AssistantViewModelTests.cs`

- [ ] **Step 1: ChatMessage model** — `Optimizer.WinUI/Models/ChatMessage.cs`:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace Optimizer.WinUI.Models;

public enum ChatRole { User, Assistant, Status }

public partial class ChatMessage : ObservableObject
{
    [ObservableProperty] private string text = "";
    public ChatRole Role { get; init; }
    public bool IsUser => Role == ChatRole.User;
    public bool IsAssistant => Role == ChatRole.Assistant;
    public bool IsStatus => Role == ChatRole.Status;
}
```

- [ ] **Step 2: Write the test first** — `Optimizer.WinUI.Tests/AssistantViewModelTests.cs`:
```csharp
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services.Assistant;
using Optimizer.WinUI.ViewModels;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class AssistantViewModelTests
{
    private sealed class FakeAssistant : IAssistantService
    {
        public string Reply { get; set; } = "hi";
        public Func<AssistantCallbacks, Task>? Behavior { get; set; }
        public Task<string> SendAsync(string userText, AssistantCallbacks cb, CancellationToken ct)
        {
            Behavior?.Invoke(cb);
            cb.OnAssistantText(Reply);
            return Task.FromResult(Reply);
        }
        public void Reset() { }
    }

    private sealed class FakeKeyStore(bool hasKey) : IApiKeyStore
    {
        public bool HasKey => hasKey;
        public void SetKey(string apiKey) { }
        public string? GetKey() => hasKey ? "k" : null;
        public void Clear() { }
    }

    [Fact]
    public async Task Send_adds_user_and_assistant_messages()
    {
        var vm = new AssistantViewModel(new FakeAssistant { Reply = "Hello!" }, new FakeKeyStore(true), a => a());
        vm.Input = "hi";
        await vm.SendCommand.ExecuteAsync(null);
        Assert.Contains(vm.Messages, m => m.IsUser && m.Text == "hi");
        Assert.Contains(vm.Messages, m => m.IsAssistant && m.Text == "Hello!");
        Assert.Equal("", vm.Input);
    }

    [Fact]
    public void NoKey_state_is_exposed_when_store_is_empty()
    {
        var vm = new AssistantViewModel(new FakeAssistant(), new FakeKeyStore(false), a => a());
        Assert.True(vm.NeedsApiKey);
    }

    [Fact]
    public async Task Empty_input_does_not_send()
    {
        var assistant = new FakeAssistant();
        var vm = new AssistantViewModel(assistant, new FakeKeyStore(true), a => a());
        vm.Input = "   ";
        await vm.SendCommand.ExecuteAsync(null);
        Assert.Empty(vm.Messages);
    }
}
```

- [ ] **Step 3: Implementation** — `Optimizer.WinUI/ViewModels/AssistantViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services.Assistant;

namespace Optimizer.WinUI.ViewModels;

public partial class AssistantViewModel : ObservableObject
{
    private readonly IAssistantService _assistant;
    private readonly IApiKeyStore _keyStore;
    private readonly Action<Action> _dispatch;

    [ObservableProperty] private string input = "";
    [ObservableProperty] private bool isBusy;

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public bool NeedsApiKey => !_keyStore.HasKey;

    /// <summary>Set by the View to render a confirmation prompt; returns the user's choice.</summary>
    public Func<string, string, Task<bool>> ConfirmHandler { get; set; } = (_, _) => Task.FromResult(false);

    public AssistantViewModel(IAssistantService assistant, IApiKeyStore keyStore, Action<Action> dispatch)
    {
        _assistant = assistant;
        _keyStore = keyStore;
        _dispatch = dispatch;
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = Input.Trim();
        if (text.Length == 0 || IsBusy) return;

        Input = "";
        Messages.Add(new ChatMessage { Role = ChatRole.User, Text = text });
        var reply = new ChatMessage { Role = ChatRole.Assistant, Text = "" };
        Messages.Add(reply);
        IsBusy = true;

        var cb = new AssistantCallbacks
        {
            OnAssistantText = chunk => _dispatch(() => reply.Text += chunk),
            OnStatus = status => _dispatch(() => Messages.Add(new ChatMessage { Role = ChatRole.Status, Text = status })),
            ConfirmAsync = (id, summary) => ConfirmHandler(id, summary),
        };

        try { await _assistant.SendAsync(text, cb, default); }
        catch (Exception ex) { _dispatch(() => reply.Text = $"Error: {ex.Message}"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Clear()
    {
        Messages.Clear();
        _assistant.Reset();
    }
}
```
> The streaming reply pattern (add an empty assistant message, then append deltas to its `Text`) mirrors how a real WinUI binding updates live. In tests `dispatch` runs inline.

- [ ] **Step 4: Run tests — expect PASS**

Run: `dotnet test Optimizer.WinUI.Tests/Optimizer.WinUI.Tests.csproj --filter AssistantViewModelTests`
Expected: 3 passed.

- [ ] **Step 5: Commit**
```bash
git add Optimizer.WinUI/Models/ChatMessage.cs Optimizer.WinUI/ViewModels/AssistantViewModel.cs Optimizer.WinUI.Tests/AssistantViewModelTests.cs
git commit -m "feat: F4.2 — AssistantViewModel (streaming chat, confirm hook, no-key state)"
```

---

## Task F4.3: Settings — AI Assistant section + IAssistantSettings impl

**Files:**
- Modify: `Optimizer.WinUI/Models/AppSettings.cs`
- Modify: `Optimizer.WinUI/ViewModels/SettingsViewModel.cs`
- Modify: `Optimizer.WinUI/Views/SettingsPage.xaml`
- Create: `Optimizer.WinUI/Services/Assistant/AssistantSettings.cs`

> Read `Models/AppSettings.cs`, `ViewModels/SettingsViewModel.cs`, and `Views/SettingsPage.xaml` before editing to match their exact existing shape (the `_isLoading` guard pattern and `settingsControls` XML namespace are already established — see the explorer notes in the spec).

- [ ] **Step 1: Add settings fields** to `Models/AppSettings.cs` (add properties alongside existing ones):
```csharp
    public bool AssistantEnabled { get; set; }
    public bool AssistantAllowActions { get; set; } = true;
    public string AssistantModel { get; set; } = "claude-sonnet-4-6";
```

- [ ] **Step 2: IAssistantSettings implementation** — `Optimizer.WinUI/Services/Assistant/AssistantSettings.cs`:
```csharp
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Reads assistant settings from the app settings service.</summary>
public sealed class AssistantSettings(ISettingsService settings) : IAssistantSettings
{
    public bool AllowActions => settings.Settings.AssistantAllowActions;
    public string Model => string.IsNullOrWhiteSpace(settings.Settings.AssistantModel)
        ? "claude-sonnet-4-6"
        : settings.Settings.AssistantModel;
}
```
> Confirm `ISettingsService` exposes `.Settings` (an `AppSettings`) — established pattern in `SettingsViewModel`.

- [ ] **Step 3: SettingsViewModel additions** — in `ViewModels/SettingsViewModel.cs`, inject `IApiKeyStore`, add observable properties + persistence (mirror the existing `_isLoading`/`OnXChanged`/`Load()` pattern):
```csharp
    // ctor: add IApiKeyStore parameter, store as _apiKeyStore

    public List<string> AssistantModelOptions { get; } =
        ["claude-sonnet-4-6", "claude-haiku-4-5-20251001", "claude-opus-4-8"];

    [ObservableProperty] private bool assistantEnabled;
    [ObservableProperty] private bool assistantAllowActions = true;
    [ObservableProperty] private string assistantModel = "claude-sonnet-4-6";
    [ObservableProperty] private string apiKeyInput = "";
    [ObservableProperty] private bool hasApiKey;

    partial void OnAssistantEnabledChanged(bool value)
    {
        if (_isLoading) return;
        _settingsService.Settings.AssistantEnabled = value;
        _settingsService.Save();
    }

    partial void OnAssistantAllowActionsChanged(bool value)
    {
        if (_isLoading) return;
        _settingsService.Settings.AssistantAllowActions = value;
        _settingsService.Save();
    }

    partial void OnAssistantModelChanged(string value)
    {
        if (_isLoading) return;
        _settingsService.Settings.AssistantModel = value;
        _settingsService.Save();
    }

    [RelayCommand]
    private void SaveApiKey()
    {
        _apiKeyStore.SetKey(ApiKeyInput);
        ApiKeyInput = "";
        HasApiKey = _apiKeyStore.HasKey;
    }

    [RelayCommand]
    private void ClearApiKey()
    {
        _apiKeyStore.Clear();
        HasApiKey = _apiKeyStore.HasKey;
    }
```
And in the existing `Load()` body (inside the `_isLoading` block), add:
```csharp
        AssistantEnabled = s.AssistantEnabled;
        AssistantAllowActions = s.AssistantAllowActions;
        AssistantModel = string.IsNullOrWhiteSpace(s.AssistantModel) ? "claude-sonnet-4-6" : s.AssistantModel;
        HasApiKey = _apiKeyStore.HasKey;
```

- [ ] **Step 4: SettingsPage.xaml — add an AI ASSISTANT section** before the closing of the settings `StackPanel` (mirror the existing `settingsControls:SettingsCard` usage):
```xml
<TextBlock Text="AI ASSISTANT" FontSize="11" FontWeight="SemiBold"
           Foreground="{ThemeResource TextFillColorSecondaryBrush}" Margin="0,16,0,0"/>

<settingsControls:SettingsCard Header="Enable AI Assistant"
                               Description="Chat with Claude to control and inspect your PC (opt-in; uses your Anthropic API key).">
    <ToggleSwitch IsOn="{x:Bind ViewModel.AssistantEnabled, Mode=TwoWay}" OnContent="" OffContent="" VerticalAlignment="Center"/>
</settingsControls:SettingsCard>

<settingsControls:SettingsCard Header="Allow the assistant to perform actions"
                               Description="When off, the assistant can only answer questions and navigate — it cannot change the system.">
    <ToggleSwitch IsOn="{x:Bind ViewModel.AssistantAllowActions, Mode=TwoWay}" OnContent="" OffContent="" VerticalAlignment="Center"/>
</settingsControls:SettingsCard>

<settingsControls:SettingsCard Header="Model" Description="Claude model used for the assistant.">
    <ComboBox ItemsSource="{x:Bind ViewModel.AssistantModelOptions}"
              SelectedItem="{x:Bind ViewModel.AssistantModel, Mode=TwoWay}"
              MinWidth="220" VerticalAlignment="Center"/>
</settingsControls:SettingsCard>

<settingsControls:SettingsCard Header="Anthropic API key"
                               Description="Stored encrypted on this PC (Windows DPAPI). Your messages and a short system-metrics summary are sent to Anthropic.">
    <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
        <PasswordBox Password="{x:Bind ViewModel.ApiKeyInput, Mode=TwoWay}" PlaceholderText="sk-ant-…" MinWidth="240"/>
        <Button Content="Save" Command="{x:Bind ViewModel.SaveApiKeyCommand}"/>
        <Button Content="Clear" Command="{x:Bind ViewModel.ClearApiKeyCommand}"/>
        <FontIcon Glyph="&#xE73E;" Foreground="#22C55E" Visibility="{x:Bind ViewModel.HasApiKey, Mode=OneWay}"/>
    </StackPanel>
</settingsControls:SettingsCard>
```
> `PasswordBox.Password` two-way binding to `ApiKeyInput` is fine for capture. If the project's `PasswordBox` binding needs a code-behind helper, fall back to a `TextBox` or a `PasswordChanged` handler that sets `ViewModel.ApiKeyInput` — verify against how other secret-ish fields are handled, if any.

- [ ] **Step 5: Build**

Run: `dotnet build Optimizer.WinUI/Optimizer.WinUI.csproj -c Debug`
Expected: compiles (XAML + VM). Fix binding/namespace issues against the existing SettingsPage.

- [ ] **Step 6: Commit**
```bash
git add Optimizer.WinUI/Models/AppSettings.cs Optimizer.WinUI/Services/Assistant/AssistantSettings.cs Optimizer.WinUI/ViewModels/SettingsViewModel.cs Optimizer.WinUI/Views/SettingsPage.xaml
git commit -m "feat: F4.3 — Settings AI Assistant section (key, model, enable, allow-actions) + IAssistantSettings"
```

---

## Task F4.4: PageNavigator implementation

**Files:**
- Create: `Optimizer.WinUI/Services/Commands/PageNavigator.cs`
- Modify: `Optimizer.WinUI/MainWindow.xaml.cs` (wire the navigator to the real navigation)

> Read `MainWindow.xaml.cs` first — it already has `PageMap` (tag→Type) and `_navigationService.NavigateTo(pageType)`.

- [ ] **Step 1: Implementation** — `Optimizer.WinUI/Services/Commands/PageNavigator.cs`:
```csharp
namespace Optimizer.WinUI.Services.Commands;

/// <summary>
/// Resolves page tags to navigation. The actual navigation delegate + tag list are injected
/// by MainWindow at startup so this stays free of WinUI types (and testable).
/// </summary>
public sealed class PageNavigator : IPageNavigator
{
    private Func<string, bool> _navigate = _ => false;
    private IReadOnlyList<string> _pages = [];

    public IReadOnlyList<string> Pages => _pages;
    public bool NavigateTo(string tag) => _navigate(tag);

    /// <summary>Called once by MainWindow after the Frame is ready.</summary>
    public void Configure(IReadOnlyList<string> pages, Func<string, bool> navigate)
    {
        _pages = pages;
        _navigate = navigate;
    }
}
```

- [ ] **Step 2: Wire it in `MainWindow.xaml.cs`** — after the navigation Frame/service is initialized (in the existing `NavView_Loaded` or constructor, after `_navigationService.Frame` is set), configure the singleton navigator. Add:
```csharp
        var navigator = (PageNavigator)App.GetService<IPageNavigator>();
        navigator.Configure(
            PageMap.Keys.ToList(),
            tag =>
            {
                if (!PageMap.TryGetValue(tag, out var pageType)) return false;
                DispatcherQueue.TryEnqueue(() => _navigationService.NavigateTo(pageType));
                return true;
            });
```
> Add `using Optimizer.WinUI.Services.Commands;` and `using System.Linq;` if not present. `PageMap` is the existing static tag→Type dictionary.

- [ ] **Step 3: Build**

Run: `dotnet build Optimizer.WinUI/Optimizer.WinUI.csproj -c Debug`
Expected: compiles (DI for `IPageNavigator` is added in F4.6 — if build fails only on resolution, proceed to F4.6 then rebuild).

- [ ] **Step 4: Commit**
```bash
git add Optimizer.WinUI/Services/Commands/PageNavigator.cs Optimizer.WinUI/MainWindow.xaml.cs
git commit -m "feat: F4.4 — PageNavigator wired to shell navigation"
```

---

## Task F4.5: Console dock UI (ConsolePanel + MainWindow restructure + pop-out)

**Files:**
- Create: `Optimizer.WinUI/Views/ConsolePanel.xaml` + `.xaml.cs`
- Create: `Optimizer.WinUI/Views/ConsoleWindow.xaml` + `.xaml.cs`
- Modify: `Optimizer.WinUI/MainWindow.xaml` + `.xaml.cs`

> This task is UI-only (no unit tests); verify by building + launching. Read `MainWindow.xaml` (the row 0/1 grid shown in the spec) before editing.

- [ ] **Step 1: ConsolePanel.xaml** — a TabView with Activity + Assistant tabs and dock controls:
```xml
<?xml version="1.0" encoding="utf-8"?>
<UserControl
    x:Class="Optimizer.WinUI.Views.ConsolePanel"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:vm="using:Optimizer.WinUI.ViewModels">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Header: title + dock controls -->
        <Grid Grid.Row="0" Padding="8,4">
            <TextBlock Text="Console" FontWeight="SemiBold" VerticalAlignment="Center"/>
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="4">
                <Button x:Name="PopOutButton" Click="PopOut_Click" ToolTipService.ToolTip="Pop out">
                    <FontIcon Glyph="&#xE8A7;" FontSize="14"/>
                </Button>
                <Button x:Name="CollapseButton" Click="Collapse_Click" ToolTipService.ToolTip="Hide">
                    <FontIcon Glyph="&#xE8BB;" FontSize="14"/>
                </Button>
            </StackPanel>
        </Grid>

        <TabView Grid.Row="1" IsAddTabButtonVisible="False" x:Name="Tabs">
            <TabViewItem Header="Activity" IsClosable="False">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>
                    <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="8" Padding="8,4">
                        <ToggleSwitch OnContent="Verbose" OffContent="Verbose"
                                      IsOn="{x:Bind ConsoleVM.ShowVerboseLogs, Mode=TwoWay}"/>
                        <Button Content="Clear" Command="{x:Bind ConsoleVM.ClearCommand}"/>
                    </StackPanel>
                    <ListView Grid.Row="1" ItemsSource="{x:Bind ConsoleVM.Lines}" x:Name="ActivityList"
                              FontFamily="Consolas" SelectionMode="None">
                        <ListView.ItemTemplate>
                            <DataTemplate xmlns:m="using:Optimizer.WinUI.Models" x:DataType="m:ConsoleLine">
                                <StackPanel Orientation="Horizontal" Spacing="8">
                                    <TextBlock Text="{x:Bind TimeText}" Foreground="#6B7280"/>
                                    <TextBlock Text="{x:Bind Glyph}"/>
                                    <TextBlock Text="{x:Bind Text}" TextWrapping="Wrap"/>
                                </StackPanel>
                            </DataTemplate>
                        </ListView.ItemTemplate>
                    </ListView>
                </Grid>
            </TabViewItem>

            <TabViewItem Header="Assistant" IsClosable="False" x:Name="AssistantTab">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="*"/>
                        <RowDefinition Height="Auto"/>
                    </Grid.RowDefinitions>
                    <ListView Grid.Row="0" ItemsSource="{x:Bind AssistantVM.Messages}" SelectionMode="None">
                        <ListView.ItemTemplate>
                            <DataTemplate xmlns:m="using:Optimizer.WinUI.Models" x:DataType="m:ChatMessage">
                                <TextBlock Text="{x:Bind Text, Mode=OneWay}" TextWrapping="Wrap" Margin="0,2"/>
                            </DataTemplate>
                        </ListView.ItemTemplate>
                    </ListView>
                    <Grid Grid.Row="1" Padding="8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <TextBox Grid.Column="0" x:Name="InputBox"
                                 Text="{x:Bind AssistantVM.Input, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                 PlaceholderText="Ask the assistant…" KeyDown="Input_KeyDown"/>
                        <Button Grid.Column="1" Content="Send" Margin="8,0,0,0"
                                Command="{x:Bind AssistantVM.SendCommand}"/>
                    </Grid>
                </Grid>
            </TabViewItem>
        </TabView>
    </Grid>
</UserControl>
```

- [ ] **Step 2: ConsolePanel.xaml.cs** — bind VMs from DI, wire confirm dialog + actions:
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Optimizer.WinUI.ViewModels;
using Windows.System;

namespace Optimizer.WinUI.Views;

public sealed partial class ConsolePanel : UserControl
{
    public ConsoleViewModel ConsoleVM { get; } = App.GetService<ConsoleViewModel>();
    public AssistantViewModel AssistantVM { get; } = App.GetService<AssistantViewModel>();

    /// <summary>Raised when the user clicks pop-out / collapse so the host (MainWindow) can react.</summary>
    public event EventHandler? PopOutRequested;
    public event EventHandler? CollapseRequested;

    public ConsolePanel()
    {
        InitializeComponent();
        AssistantVM.ConfirmHandler = ConfirmAsync;
    }

    public void FocusAssistant() { Tabs.SelectedItem = AssistantTab; InputBox.Focus(FocusState.Programmatic); }

    private async Task<bool> ConfirmAsync(string id, string summary)
    {
        var dialog = new ContentDialog
        {
            Title = "Confirm action",
            Content = $"The assistant wants to run:\n\n{summary}\n\nThis will change your system (reversible via undo). Allow it?",
            PrimaryButtonText = "Allow",
            CloseButtonText = "Decline",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void Input_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && AssistantVM.SendCommand.CanExecute(null))
        {
            AssistantVM.SendCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void PopOut_Click(object sender, RoutedEventArgs e) => PopOutRequested?.Invoke(this, EventArgs.Empty);
    private void Collapse_Click(object sender, RoutedEventArgs e) => CollapseRequested?.Invoke(this, EventArgs.Empty);
}
```
> Because `ConsoleViewModel`/`AssistantViewModel` are DI singletons, the docked panel and a popped-out panel share state automatically.

- [ ] **Step 3: ConsoleWindow.xaml** (pop-out host):
```xml
<?xml version="1.0" encoding="utf-8"?>
<Window
    x:Class="Optimizer.WinUI.Views.ConsoleWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Optimizer Console">
    <Grid x:Name="HostGrid"/>
</Window>
```

- [ ] **Step 4: ConsoleWindow.xaml.cs** — host a ConsolePanel, raise an event on close so MainWindow can re-dock:
```csharp
using Microsoft.UI.Xaml;

namespace Optimizer.WinUI.Views;

public sealed partial class ConsoleWindow : Window
{
    public event EventHandler? ReDockRequested;

    public ConsoleWindow()
    {
        InitializeComponent();
        var panel = new ConsolePanel();
        panel.CollapseRequested += (_, _) => Close();
        HostGrid.Children.Add(panel);
        Closed += (_, _) => ReDockRequested?.Invoke(this, EventArgs.Empty);
    }
}
```

- [ ] **Step 5: MainWindow.xaml — add rows 2–3 + the dock**

Change the outer `Grid.RowDefinitions` to four rows and append the splitter + dock host after the `NavigationView` (which stays on row 1). Replace the `RowDefinitions` block and add at the end of the outer grid:
```xml
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
```
…and immediately before `</Grid>` (after the `NavigationView`):
```xml
        <controls:GridSplitter Grid.Row="2" Height="6" HorizontalAlignment="Stretch"
                               x:Name="ConsoleSplitter" Visibility="Collapsed"
                               xmlns:controls="using:CommunityToolkit.WinUI.Controls"/>

        <Border Grid.Row="3" x:Name="ConsoleDockHost" Height="260"
                BorderThickness="0,1,0,0" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
                Visibility="Collapsed"/>
```
> The CommunityToolkit `GridSplitter` lives in `CommunityToolkit.WinUI.Controls` (the SettingsControls package family is already referenced; if `GridSplitter` is in a different installed package, use that namespace, or substitute a `Thumb`-based splitter). Set the splitter's `ResizeBehavior`/target per the toolkit version if needed; a fixed `Height="260"` dock is acceptable for v1 if the splitter API differs.

- [ ] **Step 6: MainWindow.xaml.cs — dock host + toggle + pop-out hand-off**

Add fields + a toggle method + Ctrl+` accelerator. After `InitializeComponent()` (and after navigation setup):
```csharp
    private ConsolePanel? _dockPanel;
    private ConsoleWindow? _popOut;

    private void EnsureDockPanel()
    {
        if (_dockPanel != null) return;
        _dockPanel = new ConsolePanel();
        _dockPanel.CollapseRequested += (_, _) => SetConsoleVisible(false);
        _dockPanel.PopOutRequested += (_, _) => PopOutConsole();
        ConsoleDockHost.Child = _dockPanel;
    }

    public void SetConsoleVisible(bool visible)
    {
        EnsureDockPanel();
        ConsoleDockHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ConsoleSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ToggleConsole() =>
        SetConsoleVisible(ConsoleDockHost.Visibility != Visibility.Visible);

    public void FocusAssistant()
    {
        SetConsoleVisible(true);
        _dockPanel?.FocusAssistant();
    }

    private void PopOutConsole()
    {
        SetConsoleVisible(false);
        _popOut = new ConsoleWindow();
        _popOut.ReDockRequested += (_, _) => { _popOut = null; SetConsoleVisible(true); };
        _popOut.Activate();
    }
```
`ConsoleDockHost` is a `Border`; set its `Child`. Add a keyboard accelerator in `MainWindow.xaml` on the root grid (or register in code):
```xml
    <Grid.KeyboardAccelerators>
        <KeyboardAccelerator Modifiers="Control" Key="192" Invoked="ConsoleAccel_Invoked"/>
    </Grid.KeyboardAccelerators>
```
and handler:
```csharp
    private void ConsoleAccel_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    { ToggleConsole(); args.Handled = true; }
```
> Key `192` is the backtick/tilde VK. Adjust if the project prefers a different toggle key.

- [ ] **Step 7: Build + launch to verify**

Run: `dotnet build Optimizer.WinUI/Optimizer.WinUI.csproj -c Debug`. Then launch the app (existing run flow). Press **Ctrl+`** → dock appears with Activity + Assistant tabs; apply an optimization → a line appears in Activity; click pop-out → dock detaches into its own window; close it → re-docks.

- [ ] **Step 8: Commit**
```bash
git add Optimizer.WinUI/Views/ConsolePanel.xaml Optimizer.WinUI/Views/ConsolePanel.xaml.cs Optimizer.WinUI/Views/ConsoleWindow.xaml Optimizer.WinUI/Views/ConsoleWindow.xaml.cs Optimizer.WinUI/MainWindow.xaml Optimizer.WinUI/MainWindow.xaml.cs
git commit -m "feat: F4.5 — persistent console dock (Activity + Assistant tabs) with pop-out window + Ctrl+\` toggle"
```

---

## Task F4.6: Omnibox + DI wiring (everything composes)

**Files:**
- Modify: `Optimizer.WinUI/MainWindow.xaml` + `.xaml.cs` (Ctrl+K omnibox)
- Modify: `Optimizer.WinUI/App.xaml.cs` (register all new services/VMs)

- [ ] **Step 1: DI registration** — in `App.xaml.cs` `.ConfigureServices(...)`, add:
```csharp
        // ── Assistant: key store, Claude client, settings, orchestration ──
        services.AddSingleton<Optimizer.WinUI.Services.Assistant.IApiKeyStore,
                              Optimizer.WinUI.Services.Assistant.DpapiApiKeyStore>();
        services.AddSingleton<Optimizer.WinUI.Services.Assistant.IAssistantSettings,
                              Optimizer.WinUI.Services.Assistant.AssistantSettings>();
        services.AddSingleton<Optimizer.WinUI.Services.Assistant.IClaudeClient,
                              Optimizer.WinUI.Services.Assistant.ClaudeClient>();
        services.AddSingleton<Optimizer.WinUI.Services.Assistant.IAssistantService,
                              Optimizer.WinUI.Services.Assistant.AssistantService>();

        // ── Command registry + commands ──
        services.AddSingleton<Optimizer.WinUI.Services.Commands.PageNavigator>();
        services.AddSingleton<Optimizer.WinUI.Services.Commands.IPageNavigator>(
            sp => sp.GetRequiredService<Optimizer.WinUI.Services.Commands.PageNavigator>());

        services.AddSingleton<Optimizer.WinUI.Services.Commands.IAppCommand, Optimizer.WinUI.Services.Commands.GetMetricsCommand>();
        services.AddSingleton<Optimizer.WinUI.Services.Commands.IAppCommand, Optimizer.WinUI.Services.Commands.GetRecommendationsCommand>();
        services.AddSingleton<Optimizer.WinUI.Services.Commands.IAppCommand, Optimizer.WinUI.Services.Commands.RunDiagnosticsScanCommand>();
        services.AddSingleton<Optimizer.WinUI.Services.Commands.IAppCommand, Optimizer.WinUI.Services.Commands.GetBottlenecksCommand>();
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

        // ── ViewModels (UI-thread dispatch helper) ──
        services.AddSingleton(sp => new Optimizer.WinUI.ViewModels.ConsoleViewModel(
            sp.GetRequiredService<Optimizer.WinUI.Services.Events.IEventBus>(),
            a => Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() => a())));
        services.AddSingleton(sp => new Optimizer.WinUI.ViewModels.AssistantViewModel(
            sp.GetRequiredService<Optimizer.WinUI.Services.Assistant.IAssistantService>(),
            sp.GetRequiredService<Optimizer.WinUI.Services.Assistant.IApiKeyStore>(),
            a => Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() => a())));
```
Add `using Microsoft.Extensions.DependencyInjection;` if not already present (needed for `GetServices`/`GetRequiredService`).
Update `SettingsViewModel`'s registration if it's explicitly constructed — ensure it can resolve the new `IApiKeyStore` ctor parameter (constructor injection works automatically with `AddSingleton<SettingsViewModel>()`).

> **Dispatch caveat:** `DispatcherQueue.GetForCurrentThread()` resolves correctly only if these VMs are first resolved on the UI thread. They are (the dock is created in `MainWindow`). If a VM is ever resolved off-thread, capture the main window's `DispatcherQueue` at startup and use that instead.

- [ ] **Step 2: Omnibox (Ctrl+K)** — in `MainWindow.xaml`, add to the `Grid.KeyboardAccelerators`:
```xml
        <KeyboardAccelerator Modifiers="Control" Key="K" Invoked="OmniboxAccel_Invoked"/>
```
and in `MainWindow.xaml.cs`:
```csharp
    private async void OmniboxAccel_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        var box = new TextBox { PlaceholderText = "Ask the assistant…", MinWidth = 360 };
        var dialog = new ContentDialog
        {
            Title = "Assistant",
            Content = box,
            PrimaryButtonText = "Ask",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.Content.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text))
        {
            var text = box.Text;
            FocusAssistant();
            var vm = App.GetService<Optimizer.WinUI.ViewModels.AssistantViewModel>();
            vm.Input = text;
            if (vm.SendCommand.CanExecute(null)) vm.SendCommand.Execute(null);
        }
    }
```
> Reuses the dock's Assistant tab (`FocusAssistant`) and the shared singleton VM, so the conversation shows up in the dock.

- [ ] **Step 3: Build + launch**

Run: `dotnet build Optimizer.WinUI/Optimizer.WinUI.csproj -c Debug`, then launch.
Verify end-to-end: Settings → AI Assistant → paste a real Anthropic key → Save. Press **Ctrl+K**, type "what's using my CPU?" → Assistant tab opens, streams an answer (calls `get_bottlenecks`). Ask "apply the privacy preset" → a confirmation dialog appears; Allow → it runs and an Activity line appears.

- [ ] **Step 4: Full test sweep**

Run: `dotnet test Optimizer.WinUI.Tests/Optimizer.WinUI.Tests.csproj`
Expected: all prior suites pass (registry, read commands, mutating commands, tool catalog, key store, assistant service, console VM, assistant VM) — ~26–30 new tests green, plus the existing suite.

- [ ] **Step 5: Commit**
```bash
git add Optimizer.WinUI/App.xaml.cs Optimizer.WinUI/MainWindow.xaml Optimizer.WinUI/MainWindow.xaml.cs
git commit -m "feat: F4.6 — DI wiring for assistant + commands + console VMs; Ctrl+K omnibox into the dock"
```

---

## Task F4.7: Roadmap note

**Files:**
- Modify: `docs/ROADMAP-V8-IMPLEMENTATION.md`

- [ ] **Step 1:** Under Phase F, add a short note that the assistant shipped as a **cloud (Claude API) intent path** — opt-in, BYO-key, DPAPI-stored — with the local Phi-3/ONNX runtime preserved as a future fully-offline option, and that the data leaving the machine is the user's messages plus a short system-metrics summary. Mention the new persistent console dock (Activity + Assistant tabs, pop-out).

- [ ] **Step 2: Commit**
```bash
git add docs/ROADMAP-V8-IMPLEMENTATION.md
git commit -m "docs: record Claude-API assistant path + console dock under Phase F"
```

---

## Final completion

After all tasks: announce use of **superpowers:finishing-a-development-branch** and follow it (verify full build + test, then present merge/PR options).

## Known follow-ups (out of scope)

- Migrate `ApiHostService` REST endpoints onto `ICommandRegistry` (remove duplication).
- `get_top_processes` command (needs a verified process-enumeration service API).
- Accumulate the streamed message in `ClaudeClient` to drop the second non-streaming `Create` call.
- Windows speech-to-text voice input (roadmap F4).
- Auto-scroll the Activity/Assistant `ListView`s to the newest item.
