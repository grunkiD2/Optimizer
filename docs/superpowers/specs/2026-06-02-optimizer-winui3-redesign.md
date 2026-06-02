# Optimizer WinUI 3 Redesign — Design Spec

## Overview

Full redesign of the Windows Optimizer app: migrate from WPF (Syncfusion ChromelessWindow) to WinUI 3 (Windows App SDK), rethink the information architecture from flat tabs to sidebar navigation with dedicated category pages, upgrade to a dark utility visual language, and add full system monitoring capabilities.

**Target audience:** Power users who understand system optimizations and want granular control with full transparency into what each optimization changes.

## Tech Stack

| Component | Current | New |
|-----------|---------|-----|
| UI Framework | WPF (.NET 10) | WinUI 3 / Windows App SDK (.NET 10) |
| Controls | Syncfusion WPF | Syncfusion WinUI 3 |
| Window | Syncfusion ChromelessWindow | WinUI 3 Window + Mica backdrop |
| Navigation | TabControl (5 tabs) | NavigationView (sidebar) |
| Theme | Light default, Syncfusion FluentLight | Dark-first, WinUI dark theme + Mica |
| Packaging | MSIX via .wapproj (DesktopBridge) | MSIX via Windows App SDK (single-project) |
| Elevation | `requireAdministrator` manifest | `asInvoker` + in-app relaunch flow |
| Architecture | MVVM (manual DI) | MVVM (Microsoft.Extensions.Hosting) |

## Navigation Architecture

WinUI 3 `NavigationView` in Left (expanded/collapsible) mode:

```
[O] Optimizer                     ← NavigationView header
─────────────────────────────
◆ Dashboard                       ← Home: full system monitoring
─────────────────────────────
  OPTIMIZE                        ← Header (non-clickable)
  ⚡ Performance                  ← Category page
  🌐 Network                     ← Category page
  💾 Storage                     ← Category page
  🖥 System                      ← Category page
  🚀 Startup                    ← Category page
─────────────────────────────
  MANAGE                          ← Header
  📋 Profiles                    ← Presets + snapshots
  📜 History                     ← Undo log + impact charts
─────────────────────────────
  ⚙ Settings                    ← FooterMenuItems
```

Page transitions use `DrillInNavigationTransitionInfo` for sidebar items.

## Dashboard Page

The home view — a full system monitoring page.

### Layout (top to bottom)

**1. Header Row**
- Left: "System Dashboard" title (22pt semibold) + "Updated HH:MM:SS" timestamp (12pt gray)
- Right: Action buttons with clear visual hierarchy:
  - "Refresh" — subtle/ghost button (gray border)
  - "Apply Safe Tune" — primary/accent button (blue fill). Applies the Light preset (safe optimizations only). Shows confirmation listing what will change.
  - "Undo All (N)" — cautionary button (red text, subtle border)

**2. Health Banner**
- WinUI 3 `InfoBar` style — full-width rounded card
- Green dot indicator + "System Health: Good" + large score number (monospace, 20pt bold)
- Right side: quick stats ("5 optimizations active · 14 changes undoable")
- Color shifts by score: green (70-100), yellow (40-69), red (0-39)
- Health score calculation is inherited from the current `WindowsOptimizerService` engine (weighted composite of active optimizations and system metrics)

**3. Metric Cards (5-column grid)**
- Five cards in a responsive `Grid` (wraps to 3+2 at narrow widths):
  - **CPU** (blue #60A5FA) — percentage, core count, progress bar
  - **Memory** (green #34D399) — percentage, total RAM, progress bar
  - **GPU** (amber #F59E0B) — percentage, GPU name, progress bar
  - **Disk** (purple #A78BFA) — percentage, drive type, progress bar
  - **Network** (pink #F472B6) — percentage, connection type, progress bar
- All values in `Cascadia Code` monospace, 26pt bold
- Labels: 11pt uppercase with 0.5px letter-spacing
- Cards are clickable — navigate to the corresponding category page
- Progress bars: 4px height, rounded, colored per-metric

**4. Charts Row (2:1 split)**
- Left (2fr): Syncfusion `SfCartesianChart` — 60-second rolling area chart with CPU (blue), Memory (green), GPU (amber) overlaid. Y-axis 0-100%, X-axis with 15-second marks.
- Right (1fr): Per-core CPU — vertical bar grid (4 columns), each core as a labeled bar filling from bottom. Collapsible "show all cores" if >12 cores.

**5. Bottom Row (1:1 split)**
- Left: Top Processes — sortable table (Name, CPU%, Memory MB) with "End Process" action per row. Uses `ListView` with `GridView` columns.
- Right: I/O Activity — Disk Read/Write and Network Down/Up with throughput values (monospace) and thin progress bars. Purple and pink color family.

### Data Source
- `SystemMonitorService` polls on a configurable timer (default 1 second)
- Uses `System.Diagnostics.PerformanceCounter` for CPU/Memory, WMI for GPU/Disk/Network
- Chart data stored as a rolling `Queue<T>` of data points (configurable length, default 60)

## Category Pages

Each of the five category pages follows a consistent template.

### Template Layout (top to bottom)

**1. Page Header**
- Left: Category icon (24px) + category name (22pt semibold) + "N of M optimizations active" subtitle
- Right: Scoped action buttons:
  - "Apply All" — accent button, applies all optimizations in this category
  - "Undo Category" — subtle button, reverts all changes in this category

**2. Local Metrics Strip (3-column grid)**
- Only metrics relevant to this category, with inline mini sparklines
- Each metric card: label (uppercase, 10pt) + value (22pt monospace bold) + sparkline (32px height)

**3. Optimization Cards (vertical stack)**
- Each optimization is a card with:
  - **Icon** (36px rounded square) — colored background when active, gray when inactive
  - **Title** — human-readable name (14pt semibold), bright when active, dimmed when inactive
  - **Description** — one-line summary (12pt gray)
  - **Status badge** — "ACTIVE" (green background) when on, hidden when off
  - **Shield icon** 🛡️ — shown if optimization requires admin privileges
  - **Toggle switch** — WinUI 3 `ToggleSwitch`, blue when on, gray when off
  - **Expandable detail panel** (WinUI 3 `Expander` or click-to-expand):
    - Left column: "What changes" — registry keys or commands in monospace code block
    - Right column: "Impact" — pros (green ▲) and cons (red ▼) list
    - Bottom: Requirement badges — "Reversible" (purple), "Requires Admin" (yellow with shield), "Requires Restart" (pink)

### Category Content Map

| Category | Local Metrics | Optimizations |
|----------|--------------|---------------|
| ⚡ Performance | CPU usage + sparkline, Memory usage + sparkline, Current power plan | Disable Background Apps, Disable Animations, Disable Visual Effects, Optimize Power Settings, Adjust Page File Size |
| 🌐 Network | Throughput ↑↓, Latency, DNS server | Optimize Network Settings, Flush DNS Cache |
| 💾 Storage | Disk usage %, Temp file size, Update cache size | Clear Temporary Files, Clear Windows Update Cache |
| 🖥 System | Telemetry status, Privacy score | Disable Telemetry, Disable Consumer Features, Disable Hibernation |
| 🚀 Startup | Estimated boot impact, Startup program count | Per-app startup enable/disable list (using `StartupService`) |

### Visual States
- **Active optimization:** Bright text (#E0E0E0), blue icon background (#1E3A5F), green "ACTIVE" badge, blue toggle
- **Inactive optimization:** Dimmed text (#9CA3AF), gray icon background (#1F2937, 50% opacity), no badge, gray toggle
- **Admin-required (non-elevated):** Dimmed + shield icon, clicking toggle triggers relaunch prompt instead of toggling

## Profiles Page

Two sections: built-in presets and user-saved snapshots.

### Built-in Presets (2x2 card grid)

| Preset | Risk Level | Description | Optimizations |
|--------|-----------|-------------|---------------|
| 🌿 Light | Safe (green badge) | Safe optimizations only — background apps, animations, clear temp files | 3 |
| ⚖️ Medium | Moderate (yellow badge) | Light + visual effects, power settings, network settings, flush DNS | 7 |
| 🔥 Heavy | Aggressive (red badge) | All optimizations — Medium + telemetry, consumer features, hibernation, page file, WU cache, startup | 13 |
| 🧹 Cleanup | Safe (green badge) | Storage recovery only — clear temp files, clear WU cache, flush DNS | 3 |

Each preset card shows: icon + name, risk badge, description, optimization count, and "Apply" button. Clicking "Apply" shows a confirmation listing which optimizations will be toggled.

### Saved Snapshots (vertical list)

- Each snapshot: name (user-editable), save date, active optimization count
- Actions: Restore, Update (overwrite with current state), Delete
- "Save Current State" button in page header — opens a naming dialog, captures the full optimization state across all categories
- Import/Export buttons at the bottom — JSON file format for sharing profiles

### Storage
- Profiles persisted to `%LocalAppData%\Optimizer\profiles.json`
- Same format as current `SettingsProfile` model, extended with snapshot metadata (name, date, source: preset vs. user)

## History Page

### Impact Chart
- Syncfusion `SfCartesianChart` — time-series line/area chart showing CPU average, Memory usage, and Health score over time
- Vertical annotation markers at points where optimizations were applied (labeled with profile/optimization name)
- Visually proves that changes improved system performance

### Change Log
- Chronological list grouped by day ("Today", "Yesterday", date headers)
- Each entry: green dot (active) or gray dot (undone), optimization name, timestamp, category label, reversibility badge
- Per-item "Undo" button on each reversible entry
- One-time actions (like clearing temp files) show result text ("Freed 2.1 GB") and "One-time" badge (non-reversible)
- "Undo All" button in page header for bulk revert

### Data Source
- `UndoService` maintains the undo stack with captured original values
- History entries persisted to `%LocalAppData%\Optimizer\history.json`

## Settings Page

WinUI 3 `SettingsCard` layout (from WinUI Community Toolkit) with three sections:

### Appearance
- **Theme** — dropdown: Dark / Light / System default
- **Backdrop Material** — dropdown: Mica / Mica Alt / Acrylic / None
- **Accent Color** — color picker with preset swatches (blue, purple, cyan, green)

### Monitoring
- **Refresh Interval** — dropdown: 1s / 2s / 5s / 10s
- **Chart History** — dropdown: 60s / 5m / 15m / 30m
- **Start with Windows** — toggle switch

### Data
- **Profile Storage** — shows path with "Open Folder" button
- **Reset All Settings** — destructive action (red), confirms via dialog. Resets app config only, does not undo optimizations.
- **Version info** — "Optimizer v2.0.0 · WinUI 3 · .NET 10"

## Elevation UX

The MSIX-packaged app launches as `asInvoker` (no `requireAdministrator` manifest entry) to ensure reliable MSIX launch and Store compatibility.

### Four-state flow

**State 1: Non-elevated (default)**
- Persistent amber `InfoBar` at the top of every page: "Running without administrator privileges — Some optimizations require admin access"
- "Relaunch as Admin" button in the InfoBar

**State 2: Shield indicators**
- Admin-required optimization cards show a 🛡️ shield icon next to the title
- Toggle switch is visually dimmed (50% opacity)
- Clicking a shielded toggle does NOT toggle — instead opens the relaunch confirmation dialog

**State 3: Relaunch confirmation**
- WinUI 3 `ContentDialog`: "Relaunch as Administrator?"
- Body: "This optimization requires administrator privileges. The app will close and reopen with elevated permissions. Your current state will be preserved."
- Buttons: "Cancel" (secondary) / "🛡️ Relaunch" (primary/accent)
- On confirm: `ElevationService.TryRelaunchElevated()` — saves current state, launches elevated process, exits current process

**State 4: Elevated**
- Green `InfoBar` (dismissable): "Running as Administrator — All optimizations are available"
- All shield icons removed, all toggles fully enabled

## Visual Design System

### Color Palette

**Backgrounds:**
- Window: Mica backdrop (system-managed dark surface)
- Primary surface: #0D1117
- Card/panel: #111827
- Border: #1E2A3A
- Hover: #1E3A5F

**Text:**
- Primary: #F0F0F0 / #E0E0E0
- Secondary: #9CA3AF
- Tertiary: #6B7280
- Disabled: #4B5563

**Accent colors (per-metric):**
- CPU: Blue #60A5FA
- Memory: Green #34D399
- GPU: Amber #F59E0B
- Disk: Purple #A78BFA
- Network: Pink #F472B6

**Status colors:**
- Active/success: Green #4ADE80 (dot), #065F46 (background), #6EE7B7 (text)
- Warning/admin: Amber #FBBF24 (text), #422006 (background)
- Danger/undo: Red #F87171 (text), #7F1D1D (background)
- Info/reversible: Purple #A78BFA (text), #1E1B4B (background)

### Typography

- **Page titles:** Segoe UI, 22pt, SemiBold
- **Card titles:** Segoe UI, 14pt, SemiBold
- **Body text:** Segoe UI, 12-13pt, Regular
- **Labels:** Segoe UI, 10-11pt, Regular, uppercase, letter-spacing 0.5-1px
- **Metric values:** Cascadia Code (monospace), 22-26pt, Bold
- **Code/registry paths:** Cascadia Code, 11-12pt, Regular

### Spacing

- Page padding: 24px
- Card padding: 14-16px
- Card gap: 8-12px
- Section gap: 20px
- Border radius: 8px (cards), 6px (buttons), 4px (badges), 12px (toggles/dialogs)

### Icons
- Segoe Fluent Icons font for sidebar navigation icons
- Emoji for category page headers and optimization card icons (consistent cross-platform rendering)
- Shield emoji 🛡️ for admin-required indicators

## Project Structure (WinUI 3)

```
Optimizer/
├── App.xaml(.cs)                    ← Application entry, theme setup, DI container
├── MainWindow.xaml(.cs)             ← Window + NavigationView shell
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
├── ViewModels/
│   ├── DashboardViewModel.cs
│   ├── PerformanceCategoryViewModel.cs
│   ├── NetworkCategoryViewModel.cs
│   ├── StorageCategoryViewModel.cs
│   ├── SystemCategoryViewModel.cs
│   ├── StartupCategoryViewModel.cs
│   ├── ProfilesViewModel.cs
│   ├── HistoryViewModel.cs
│   └── SettingsViewModel.cs
├── Models/
│   ├── OptimizationInfo.cs          ← reuse + extend from current
│   ├── SystemMetric.cs              ← new: metric data point
│   ├── SettingsProfile.cs           ← reuse + extend with snapshot metadata
│   ├── HistoryEntry.cs              ← new: change log entry
│   ├── ProcessInfo.cs               ← reuse from current
│   └── AppSettings.cs               ← new: app configuration model
├── Services/
│   ├── IWindowsOptimizerService.cs  ← reuse core interface
│   ├── WindowsOptimizerService.cs   ← reuse core engine
│   ├── SystemMonitorService.cs      ← reuse + extend (GPU, disk I/O, network)
│   ├── UndoService.cs               ← reuse
│   ├── ElevationService.cs          ← reuse (adapted for WinUI 3 process model)
│   ├── StartupService.cs            ← reuse
│   ├── ProfileService.cs            ← new: preset + snapshot management
│   ├── HistoryService.cs            ← new: change log persistence
│   ├── SettingsService.cs           ← reuse + extend
│   └── NavigationService.cs         ← new: NavigationView page routing
├── Controls/
│   └── OptimizationCard.xaml(.cs)   ← reusable custom control for optimization cards
├── Helpers/
│   ├── ThemeHelper.cs               ← theme/backdrop switching
│   └── AnimationHelper.cs           ← page transition helpers
├── Converters/
│   └── (value converters as needed)
├── Assets/
│   └── (app icon, splash)
└── app.manifest                     ← asInvoker (no requireAdministrator)
```

## Migration Strategy

The redesign is a **new WinUI 3 project** that reuses core services from the existing WPF app:

**Reuse directly (copy + minimal adaptation):**
- `IWindowsOptimizerService` / `WindowsOptimizerService` — core optimization engine (registry, powercfg, etc.)
- `UndoService` — undo stack with captured values
- `ElevationService` — process relaunch logic (adapt process startup for WinUI 3)
- `StartupService` — startup program enumeration
- `SystemMonitorService` — extend with GPU/Disk I/O/Network monitoring
- `SettingsService` — extend with new settings model
- All models under `Optimization/Models/`

**Build new:**
- `MainWindow` + `NavigationView` shell
- All 9 pages (Views + ViewModels)
- `ProfileService` — preset definitions + snapshot save/restore
- `HistoryService` — change log persistence with impact tracking
- `NavigationService` — page routing
- `OptimizationCard` custom control
- Theme/backdrop infrastructure

**Retire:**
- Syncfusion WPF `ChromelessWindow` and `MenuAdv`
- `ShellWindow.xaml` / `ShellViewModel`
- `OnboardingWindow.xaml`
- `MenuAdvPage.xaml` / `MenuAdvViewModel`
- WPF-specific services (`ApplicationHostService`, `TrayIconService` if not needed)

## Non-Goals

- Onboarding wizard — power users don't need hand-holding
- System tray icon — can be added later if needed
- Scheduled optimization — future feature, not in scope for the redesign
- Light theme polish — dark-first; light theme will work via WinUI theming but won't be optimized in v1
