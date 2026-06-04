# Contributing to Optimizer

This is the practical guide. If you're new to the project, read it before opening files.
Companion docs (don't duplicate, link):

- [`README.md`](README.md) — feature inventory and what the app does.
- [`docs/REDESIGN-IA.md`](docs/REDESIGN-IA.md) — the information-architecture ideology
  (1 home + 5 hubs + Settings). When in doubt about *where* a feature belongs, this wins.
- [`docs/REDESIGN-COMMAND-CENTER.md`](docs/REDESIGN-COMMAND-CENTER.md) — design-token + HUD
  visual system spec. Tokens, brushes, motion.
- [`docs/BACKLOG.md`](docs/BACKLOG.md) — parked work.
- [`CLAUDE.md`](CLAUDE.md) — accumulated gotchas and conventions. Read end-to-end once.

---

## 1. Quick start

```powershell
# Build (Win2D fails on AnyCPU — x64 is mandatory)
dotnet build Optimizer.WinUI/Optimizer.WinUI.csproj -c Debug -p:Platform=x64

# Run the app
Optimizer.WinUI/bin/x64/Debug/net10.0-windows10.0.22621.0/Optimizer.WinUI.exe

# Tests (xUnit, ~580+ tests)
dotnet test Optimizer.WinUI.Tests/Optimizer.WinUI.Tests.csproj -c Debug -p:Platform=x64
```

Requirements: Windows 10 22H2+ or Windows 11, .NET 10 SDK, x64. Some features need admin
(the app handles relaunching).

DEBUG builds also start a REST API on `http://127.0.0.1:8765`. If launch fails with a bind
error, a stale process is holding the port — see [`CLAUDE.md`](CLAUDE.md) gotchas.

Crash logs land at `%LocalAppData%\Optimizer\crash.log` (plain text, append-only).

---

## 2. Project layout

```
Optimizer.WinUI/
├── Views/                  Pages (one per hub-section + standalone)
│   ├── HubRegistry.cs      The 5 hubs + HubRouting.Resolve(tag)
│   ├── HubPage.xaml.cs     Hub host: holds a Segmented sub-nav
│   └── *Page.xaml(.cs)     Individual pages (auto-discovered, no .csproj edits)
├── Controls/Hud/           HudBackdrop, HudPageHeader, HudCard, StatusPill, ...
├── Styles/Tokens.xaml      Brushes, type ramp, geometry, motion easings
├── Services/               Backend (Cleanup, Power, Network, Profile, ...)
│   ├── Optimizations/      IOptimizationHandler implementations
│   ├── Assistant/          AssistantService, IElevationService, IPageNavigator
│   └── Commands/           NavigationService, IRecommendationsService, ...
├── ViewModels/             MVVM viewmodels (CommunityToolkit.Mvvm)
└── Models/                 DTOs, settings, enums (FindingCategory, etc.)

Optimizer.WinUI.Tests/      xUnit suite. ImplicitUsings is OFF — add explicit `using`s.
```

---

## 3. The four patterns you must follow

These are non-negotiable invariants. Tests and the build will catch most violations, but
saving a CI round trip is faster than waiting.

### 3.1 Page layout

Every page wraps content in `<hud:HudBackdrop>` and groups with `<hud:HudCard>`. A header
uses `<hud:HudPageHeader Icon Title Description>`. Reference: any *Page.xaml. The merged
host pattern (Performance+Tuning etc.) uses `tk:Segmented` + a `Section_Changed` handler
that toggles `Visibility` on named `StackPanel` panes. Canonical example:
[`PerformancePage.xaml.cs`](Optimizer.WinUI/Views/PerformancePage.xaml.cs).

### 3.2 Hub-aware navigation

Code-behinds and ViewModels navigate **through the hub router**, never directly:

```csharp
// YES — routes to the right hub + section + sub-section, syncs the slim rail
App.GetService<IPageNavigator>().NavigateTo("Storage");

// NO — dumps the user on a standalone page outside its hub
nav.NavigateTo(typeof(StoragePage));
```

The only legitimate caller of `NavigationService.NavigateTo(typeof(...))` is
[`MainWindow.xaml.cs`](Optimizer.WinUI/MainWindow.xaml.cs) (the shell). Everywhere else
goes through [`HubRouting.Resolve(tag)`](Optimizer.WinUI/Views/HubRegistry.cs) →
[`IPageNavigator`](Optimizer.WinUI/Services/IPageNavigator.cs). Tag list is
`HubRouting.KnownTags`.

### 3.3 Design tokens (brushes especially)

Defined in [`Styles/Tokens.xaml`](Optimizer.WinUI/Styles/Tokens.xaml).

- **Surfaces** use `ThemeResource`: `HudSurfaceBrush`, `HudSurfaceAltBrush`, `HudHairlineBrush`.
  These change with theme.
- **Semantic + accent brushes** use `StaticResource`: `MutedBrush`, `AccentCyanBrush`,
  `SuccessBrush`, `DangerBrush`, `WarningBrush`, `InfoBrush`, `VioletBrush`. They're
  intentionally fixed.

Mixing this up is the most common review nit.

### 3.4 Services log their work

Any service doing user-invokable work calls `EngineLog.Write("[ServiceName] What it's doing")`
at the entry point. `ConsoleViewModel.Lines` subscribes to `EngineLog.LineWritten` and the
Activity console feeds off that. Bypass this and the Activity tab goes silent for your
feature.

`IOptimizationHandler` gets this for free via `OptimizationHandlerBase.SetRegistryValue` —
don't bypass the base class (see §4.1).

---

## 4. How to add things

### 4.1 A new optimization handler (safest entry point)

1. Create `Optimizer.WinUI/Services/Optimizations/MyTweakHandler.cs` inheriting
   [`OptimizationHandlerBase`](Optimizer.WinUI/Services/Optimizations/OptimizationHandlerBase.cs).
2. Implement `Info` (Id, Title, Summary, RequiresAdmin, Changes — non-empty) and `ApplyAsync`.
3. Use `SetRegistryValue(root, subKey, valueName, value, description)` from the base —
   it captures undo state via `IUndoService.CaptureRegistry` and emits an `EngineLog` line.
4. Register the handler type in DI (typically transient, same place other handlers register).

**No per-handler test required.** [`OptimizationHandlersSmokeTests`](Optimizer.WinUI.Tests/OptimizationHandlersSmokeTests.cs)
uses xUnit `MemberData` to reflect every concrete `IOptimizationHandler` in the assembly
and exercises five architectural rules:

1. Constructs without throwing.
2. Non-empty Id/Title/Summary + `Id == Info.Id`.
3. Inherits `OptimizationHandlerBase`.
4. If `RequiresAdmin`, returns `Success=false` with zero undo captures when unelevated.
5. `Info.Changes` non-empty.

Your handler is tested automatically on the next test run. Write a parallel per-handler
test *only* if you're testing behavior (idempotence, specific value semantics) beyond the
architectural surface.

### 4.2 A new hub-section page

1. Create the .xaml(.cs) under `Optimizer.WinUI/Views/`. WinUI SDK auto-discovers it — no
   .csproj edits needed.
2. Wrap content in `<hud:HubBackdrop>` and a `<hud:HudPageHeader>`. Group with `<hud:HudCard>`.
3. Add a `HubSection` entry to the relevant `HubConfig` in
   [`HubRegistry.cs`](Optimizer.WinUI/Views/HubRegistry.cs).
4. Add the tag (or any aliases) to `HubRouting._routes` so `IPageNavigator.NavigateTo("YourTag")`
   resolves. Add a row to [`HubRoutingTests`](Optimizer.WinUI.Tests/HubRoutingTests.cs) pinning
   the expected hub + section + sub-section.

### 4.3 Merging two pages under one host

Pattern: one host page owns both viewmodels, a `tk:Segmented` toggles visibility on named
`<StackPanel>` panes. Pass the desired starting sub-section through navigation:

```csharp
protected override void OnNavigatedTo(NavigationEventArgs e)
{
    base.OnNavigatedTo(e);
    if (e.Parameter is int idx && SectionSeg is not null
        && idx >= 0 && idx < SectionSeg.Items.Count)
        SectionSeg.SelectedIndex = idx;
}
```

Canonical examples: PerformancePage (Performance+Tuning), StartupPage (Startup+Services),
MarketplacePage (Marketplace+Plugins), ProfilesPage (Profiles+Templates).

### 4.4 Extending a service interface

When you add a member to an existing interface (`IAssistantSettings`, `IContextDetectionService`,
etc.), grep `: IInterfaceName` across `Optimizer.WinUI.Tests/`. There are typically 2–4 test
fakes that need the new member or the test build breaks late.

---

## 5. Testing

- xUnit, ~580 tests, runs on Debug/x64. CI matches what you should run locally.
- Architectural rules covered automatically: handler smoke surface (§4.1), HubRouting
  resolution, the assistant elevation gate + console-log emission.
- Add tests for *behavior* you can't derive from the architectural surface: idempotence,
  specific value semantics, race conditions, error paths.
- Test files need explicit `using` statements — `ImplicitUsings` is **off** in
  `Optimizer.WinUI.Tests.csproj`.

---

## 6. Pull request flow

1. Branch off the active integration branch (`feature/winui3-redesign` at the time of
   writing — check `git branch -r` for what's current).
2. After XAML edits, build with `-p:Platform=x64` before claiming green. XAML markup
   errors surface only at build, not at edit-time.
3. Make sure the four invariants in §3 are satisfied. The most common review nit is
   `ThemeResource` vs `StaticResource` confusion.
4. Run the test suite. If you added a service that does user-invokable work, confirm an
   `EngineLog.Write` line shows in the Activity console while exercising it.
5. Use [GitHub Flavored Markdown](https://docs.github.com/en/get-started/writing-on-github)
   in the PR body. Summarize the *why*, not the *what* (the diff shows what).

---

## 7. Verifying UI changes

Visual verification on this codebase is tricky because `computer-use` / Claude-in-Chrome
don't work on unpacked WinUI. Use `mcp__Windows-MCP__Screenshot` to capture, but expect
that **UIA-content-island clicking is unreliable**. To land on a specific page from
a fresh launch, set `LastNavigationItem` (plus `HasCompletedOnboarding: true` to skip
onboarding) in `%LocalAppData%\Optimizer\app-settings.json`, then start the app.

---

## 8. Where to ask

- Architecture / IA decisions: [`docs/REDESIGN-IA.md`](docs/REDESIGN-IA.md) is the source
  of truth. If your change deviates, open the conversation in the PR before writing code.
- Design tokens, motion, surfaces: [`docs/REDESIGN-COMMAND-CENTER.md`](docs/REDESIGN-COMMAND-CENTER.md).
- Build / test / launch / known foot-guns: [`CLAUDE.md`](CLAUDE.md) gotchas section.
- Parked features: [`docs/BACKLOG.md`](docs/BACKLOG.md) before proposing new ones.
