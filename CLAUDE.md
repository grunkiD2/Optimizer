# Optimizer

WinUI 3 desktop app for Windows. Active project: `Optimizer.WinUI/`. Tests: `Optimizer.WinUI.Tests/` (588 xUnit tests).

## ⛔ This machine: Fancontrol federation
This machine runs a live autonomous machine-control system (`L:\Users\Fancontrol`). **Read [`docs/MACHINE-OWNERSHIP.md`](docs/MACHINE-OWNERSHIP.md) before touching sensors, power plans, profiles, or automation** — Optimizer is the UI/diagnostics shell in that federation, never a competing controller. Sensors come from the external LHM server (`http://localhost:8085/data.json`), power plans belong to Process Lasso, profile switching belongs to fgwatch/`ctl.ps1`.

## Build & run
- Build: `dotnet build Optimizer.WinUI/Optimizer.WinUI.csproj -c Debug -p:Platform=x64`. **Always pass `-p:Platform=x64`** — Win2D fails on AnyCPU.
- Tests: `dotnet test Optimizer.WinUI.Tests/Optimizer.WinUI.Tests.csproj -c Debug -p:Platform=x64`.
- Every `IOptimizationHandler` is auto-exercised by `OptimizationHandlersSmokeTests` (xUnit `MemberData` enumerates types via reflection) against 5 rules: ctor doesn't throw, Info has non-empty metadata + Id matches Info.Id, inherits `OptimizationHandlerBase`, returns `Success=false` + zero undo captures when `Info.RequiresAdmin=true` against an unelevated fake, `Info.Changes` non-empty. A new handler tomorrow is tested automatically — don't write a parallel per-handler test.
- Exe: `Optimizer.WinUI/bin/x64/Debug/net10.0-windows10.0.22621.0/Optimizer.WinUI.exe`.
- DEBUG builds auto-start the REST API on port 8765 (`ApiEnabled` is **not** persisted). `/openapi/v1.json` is unauth; `/api/*` is auth-gated.
- For visual verification use `mcp__Windows-MCP__Screenshot`. `computer-use`/Claude-in-Chrome don't work on unpacked WinUI; Windows-MCP can screenshot but **can't click** WinUI controls (UIA content-island gap; bridge stringifies list params). To land on a specific page, set `LastNavigationItem` (+ `HasCompletedOnboarding: true` to skip onboarding) in `%LocalAppData%\Optimizer\app-settings.json`, then launch.

## Design system
- Product intent + durable constraints in [`docs/VISION.md`](docs/VISION.md) (single-user, local-only, AI-for-analysis-not-chat, everything toggleable/reversible). IA ideology locked in [`docs/REDESIGN-IA.md`](docs/REDESIGN-IA.md). Parked work in [`docs/BACKLOG.md`](docs/BACKLOG.md).
- Hub IA (1 home + 5 hubs + Settings) lives in `Optimizer.WinUI/Views/HubRegistry.cs`. Navigation goes through `HubRouting.Resolve(tag)` (same file), which routes any tag to the right hub + section + sub-section so the slim rail stays in sync. Code-behinds + ViewModels should call `App.GetService<IPageNavigator>().NavigateTo(tag)` — never `NavigationService.NavigateTo(typeof(X))` directly, that bypasses the hub.
- Tokens in `Optimizer.WinUI/Styles/Tokens.xaml`. **Surfaces use `ThemeResource`** (`HudSurfaceBrush`, `HudSurfaceAltBrush`, `HudHairlineBrush`). **Semantic + accent brushes use `StaticResource`** (`MutedBrush`, `AccentCyanBrush`, `SuccessBrush`, `DangerBrush`, `WarningBrush`, `InfoBrush`, `VioletBrush`).
- Calm page pattern: wrap content in `<hud:HudBackdrop>` (xmlns: `using:Optimizer.WinUI.Controls.Hud`); use `<hud:HudPageHeader Icon Title Description>` and group with `<hud:HudCard>`.
- Merged-page pattern (Performance+Tuning, Startup+Services, Marketplace+Plugins, Profiles+Templates): host page owns both VMs, a `tk:Segmented` + `Section_Changed` handler toggles `Visibility` on named `StackPanel` panes. Canonical example: `Optimizer.WinUI/Views/PerformancePage.xaml.cs`.

## Gotchas
- Power Insights (`Services/Power/`) attributes MEASURED package watts by CPU-time share — deliberately NOT the ETW Energy-Estimation-Engine path (no per-process energy on battery-less desktops, and ETW needs elevation). It is read-only by contract; `PpiReadOnlyTests` audits the source for mutating calls — keep new Power code clean of `.Kill(`/`SetValue(`/priority/affinity/powercfg.
- The correct ease class is `<SineEase EasingMode="EaseInOut"/>` — there is no `SineEaseInOut`.
- `Cursor=` is **not** a XAML attribute. For draggable splitters use `tk:GridSplitter` (it sets the cursor itself).
- `Optimizer.WinUI.Tests/Optimizer.WinUI.Tests.csproj` has `ImplicitUsings` off — test files need explicit `using` statements.
- After XAML edits, build with the x64 flag before claiming green — XAML markup errors surface only at build.
- Toolkit packages already referenced: `Segmented`, `Sizers` (GridSplitter), `Animations`, `SettingsControls`, `Converters` — no new package needed for these.
- Anthropic .NET SDK streaming: TryPick methods drop the `Message`/`Raw` prefix — use `TryPickStart`/`TryPickDelta`/`TryPickStop` (not `TryPickMessageStart`/Delta/Stop) plus the `ContentBlock` variants. Tool-use blocks **can** be accumulated from streaming alone: `ContentBlockStart` carries the tool name+id, `ContentBlockDelta` with `InputJsonDelta` streams the input JSON, `MessageDelta`/`Stop` carries `stop_reason`. No follow-up non-streaming `Messages.Create` needed — calling both per turn double-bills.
- DPAPI alone doesn't restrict file access — combine with explicit ACLs (`FileInfo.GetAccessControl` + `SetAccessRuleProtection(true, false)`) to prevent other users from reading the encrypted blob.
- When extending an interface (`IAssistantSettings`, `IContextDetectionService`, etc.), grep `: IInterfaceName` across `Optimizer.WinUI.Tests/` — there are typically 2-4 test fakes that need the new member or the build breaks late.
- PowerShell `$pid` is a read-only automatic variable. Use `$ownerPid` / `$procId` instead when capturing a PID from `Get-NetTCPConnection` etc.
- WinUI launch-time crashes write to `%LocalAppData%\Optimizer\crash.log` (plain text, latest at the bottom). Check there before re-launching when the process dies in < 3 s.
- The repeated "my settings got wiped" mystery (2026-06-12) was the TEST SUITE: SettingsService tests used the parameterless ctor = the real `%LocalAppData%` file, and `Reset()`/`Save()` on an unloaded instance wrote full defaults (incl. a fresh ApiToken) on every `dotnet test`. Fixed: path-injectable ctor + isolated temp files in tests + a `.rejected` forensics copy on parse failure. NEVER construct `new SettingsService()` in a test.

## Conventions
- Session handoff buffer at `.remember/remember.md`.
- Hub-section page files are auto-discovered by the WinUI SDK — no `.csproj` edits needed when adding or removing pages.
- Any service that does user-invokable work should `EngineLog.Write("[ServiceName] What it's doing")` at entry — `ConsoleViewModel.Lines` subscribes to `EngineLog.LineWritten` and the Activity console feeds off that. New `IOptimizationHandler`s get this for free via `OptimizationHandlerBase.SetRegistryValue`; bypass the base and undo + visibility both break.
