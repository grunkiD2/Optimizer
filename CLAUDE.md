# Optimizer

WinUI 3 desktop app for Windows. Active project: `Optimizer.WinUI/`. Tests: `Optimizer.WinUI.Tests/` (466 xUnit tests).

## Build & run
- Build: `dotnet build Optimizer.WinUI/Optimizer.WinUI.csproj -c Debug -p:Platform=x64`. **Always pass `-p:Platform=x64`** — Win2D fails on AnyCPU.
- Tests: `dotnet test Optimizer.WinUI.Tests/Optimizer.WinUI.Tests.csproj -c Debug -p:Platform=x64`.
- Exe: `Optimizer.WinUI/bin/x64/Debug/net10.0-windows10.0.22621.0/Optimizer.WinUI.exe`.
- DEBUG builds auto-start the REST API on port 8765 (`ApiEnabled` is **not** persisted). `/openapi/v1.json` is unauth; `/api/*` is auth-gated.
- For visual verification use `mcp__Windows-MCP__Screenshot`. `computer-use`/Claude-in-Chrome don't work on unpacked WinUI; Windows-MCP can screenshot but **can't click** WinUI controls (UIA content-island gap; bridge stringifies list params). To land on a specific page, set `LastNavigationItem` (+ `HasCompletedOnboarding: true` to skip onboarding) in `%LocalAppData%\Optimizer\app-settings.json`, then launch.

## Design system
- IA ideology locked in [`docs/REDESIGN-IA.md`](docs/REDESIGN-IA.md). Parked work in [`docs/BACKLOG.md`](docs/BACKLOG.md).
- Hub IA (1 home + 5 hubs + Settings) lives in `Optimizer.WinUI/Views/HubRegistry.cs`. Direct-nav targets (with back-compat redirects for retired tags) live in `MainWindow.PageMap`.
- Tokens in `Optimizer.WinUI/Styles/Tokens.xaml`. **Surfaces use `ThemeResource`** (`HudSurfaceBrush`, `HudSurfaceAltBrush`, `HudHairlineBrush`). **Semantic + accent brushes use `StaticResource`** (`MutedBrush`, `AccentCyanBrush`, `SuccessBrush`, `DangerBrush`, `WarningBrush`, `InfoBrush`, `VioletBrush`).
- Calm page pattern: wrap content in `<hud:HudBackdrop>` (xmlns: `using:Optimizer.WinUI.Controls.Hud`); use `<hud:HudPageHeader Icon Title Description>` and group with `<hud:HudCard>`.
- Merged-page pattern (Performance+Tuning, Startup+Services, Marketplace+Plugins, Profiles+Templates): host page owns both VMs, a `tk:Segmented` + `Section_Changed` handler toggles `Visibility` on named `StackPanel` panes. Canonical example: `Optimizer.WinUI/Views/PerformancePage.xaml.cs`.

## Gotchas
- The correct ease class is `<SineEase EasingMode="EaseInOut"/>` — there is no `SineEaseInOut`.
- `Cursor=` is **not** a XAML attribute. For draggable splitters use `tk:GridSplitter` (it sets the cursor itself).
- `Optimizer.WinUI.Tests/Optimizer.WinUI.Tests.csproj` has `ImplicitUsings` off — test files need explicit `using` statements.
- After XAML edits, build with the x64 flag before claiming green — XAML markup errors surface only at build.
- Toolkit packages already referenced: `Segmented`, `Sizers` (GridSplitter), `Animations`, `SettingsControls`, `Converters` — no new package needed for these.
- Anthropic .NET SDK streaming: TryPick methods drop the `Message`/`Raw` prefix — use `TryPickStart`/`TryPickDelta`/`TryPickStop` (not `TryPickMessageStart`/Delta/Stop) plus the `ContentBlock` variants. Tool-use blocks **can** be accumulated from streaming alone: `ContentBlockStart` carries the tool name+id, `ContentBlockDelta` with `InputJsonDelta` streams the input JSON, `MessageDelta`/`Stop` carries `stop_reason`. No follow-up non-streaming `Messages.Create` needed — calling both per turn double-bills.
- DPAPI alone doesn't restrict file access — combine with explicit ACLs (`FileInfo.GetAccessControl` + `SetAccessRuleProtection(true, false)`) to prevent other users from reading the encrypted blob.

## Conventions
- Session handoff buffer at `.remember/remember.md`.
- Hub-section page files are auto-discovered by the WinUI SDK — no `.csproj` edits needed when adding or removing pages.
