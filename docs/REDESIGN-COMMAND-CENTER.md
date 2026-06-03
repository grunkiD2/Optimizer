# Optimizer — "Command Center" Redesign Spec

**Platform:** WinUI 3 (evolve, not migrate — keep all native integration + the .NET/SQLite learning engine).
**Aesthetic:** Command-center HUD — dark, electric-cyan accent, glass/Mica surfaces, glowing live
telemetry, monospace numerics, data-dense but with strong hierarchy.

> Goal: fix the three real problems — *no overview, no coherency between pages, bolted-on consoles* —
> by introducing (1) a hub-based information architecture, (2) one owned design-token + component
> layer, and (3) a single persistent Console dock. The framework stays; the *system* is new.

---

## 1. Design tokens (`Styles/Tokens.xaml`, merged in `App.xaml`)

All values below become real resources. They replace 634 hardcoded font sizes, 349 spacings,
171 radii, and 65 status hex literals found in the audit.

### Color — surfaces (dark-first, over Mica)
| Token | Value | Use |
|-------|-------|-----|
| `HudBackdrop` | Mica (system) | window base |
| `HudSurfaceBrush` | `#CC121724` (80%) | card / panel fill (translucent over Mica) |
| `HudSurfaceAltBrush` | `#E60E121C` | console dock, raised panels |
| `HudHairlineBrush` | `#1AFFFFFF` (10% white) | 1px card borders, dividers |
| `HudGridLineBrush` | `#0DFFFFFF` (5%) | optional background grid texture |

### Color — accent ramp (electric cyan)
| Token | Value | Use |
|-------|-------|-----|
| `AccentCyan` | `#38BDF8` | primary accent, active state, focus |
| `AccentCyanBright` | `#7DD3FC` | hover, glow highlight |
| `AccentCyanDeep` | `#0EA5E9` | pressed |
| `AccentGlowBrush` | `#6638BDF8` (40%) | drop-shadow / glow on live + active |
| `OnAccentBrush` | `#06121A` | text/icon on accent fills |

*(Keeps the existing `#3B82F6` as a secondary "Fluent blue" for non-HUD chrome if needed.)*

### Color — semantic status (formalizes the implicit palette already hardcoded everywhere)
| Token | Value | Meaning |
|-------|-------|---------|
| `SuccessBrush` | `#34D399` | good / healthy / pro |
| `WarningBrush` | `#F59E0B` | caution / admin-required |
| `DangerBrush` | `#F87171` | bad / failing / con |
| `InfoBrush` | `#60A5FA` | informational |
| `MutedBrush` | `#9CA3AF` | secondary text, code, disabled |
| Each also has a `…SoftBrush` (~15% alpha) for pill backgrounds. |

### Typography (extend WinUI text styles; monospace for telemetry)
| Token | Size / Weight | Use |
|-------|---------------|-----|
| `HudDisplayStyle` | 40 / SemiBold | Command Center hero numerics |
| `HudTitleStyle` | 28 / SemiBold | page titles |
| `HudSubtitleStyle` | 20 / SemiBold | card headers |
| `HudBodyStyle` | 14 / Regular | body text |
| `HudCaptionStyle` | 12 / Regular, `MutedBrush` | labels |
| `HudMicroStyle` | 10 / SemiBold, +100 char-spacing | badges, section eyebrows |
| `HudMetricStyle` | 24 / SemiBold, **Cascadia Mono** | live telemetry values (HUD signature) |
| `HudMonoSmallStyle` | 12 / **Cascadia Mono** | console output, code, raw numbers |

### Spacing (8pt grid) & geometry
| Token | Value |
|-------|-------|
| `SpacingXs / S / M / L / XL / 2XL` | `4 / 8 / 12 / 16 / 24 / 32` |
| `CardCornerRadius` | `10` |
| `PillCornerRadius` | `999` (full) |
| `InnerCornerRadius` | `6` |

### Motion
| Token | Value | Use |
|-------|-------|-----|
| `MotionFast` | 120ms ease-out | hover / press |
| `MotionStd` | 220ms ease-out | state change, dock expand |
| `MotionPage` | 280ms slide+fade | page / hub transitions |
| live "pulse" | 1.6s loop on accent glow | "monitoring active" indicators |

---

## 2. Information architecture: 25 flat items → 5 hubs + home

| Hub | Pages absorbed |
|-----|----------------|
| **⌂ Command Center** *(new)* | health, vitals, context, top recommendations, activity |
| **◉ Monitor** | Performance, Hardware, Network, Storage, Devices, Event Logs |
| **⚡ Optimize** | Recommendations, Tuning, Profiles, Startup, Services, System, Updates, Security |
| **🧠 Automate** | Learning, Templates, Schedules, History/Undo, Reports |
| **⚙ Manage** | Marketplace, Plugins, Fleet, Compliance, Settings |

**Shell:** slim icon rail (5 hubs, expand-on-hover) · top **context bar** (current context + confidence,
⌘K command palette, automation kill-switch, global health pill) · center hub content · **Console dock**
docked bottom/right.

---

## 3. Core components (everything composes from these)

| Control | Spec |
|---------|------|
| `HudCard` | glass surface (`HudSurfaceBrush`), 1px `HudHairlineBrush`, `CardCornerRadius`, header slot + content. Variants: `Default`, `Accent` (cyan top-bracket + glow when active), `Warning`, `Danger` (status-framed). |
| `StatTile` | label (`HudCaptionStyle`) · big value (`HudMetricStyle`, mono) · unit · sparkline · delta arrow colored by status. The Monitor/Command-Center workhorse. |
| `StatusPill` | full-radius badge, semantic color + soft bg, text + optional glyph. **Replaces every inline badge** (ACTIVE, Requires Admin, Reversible, pros/cons…). |
| `SectionHeader` | eyebrow (`HudMicroStyle`) + title + optional action link. One consistent header everywhere. |
| `HealthRing` | circular progress, accent gradient + `AccentGlowBrush`, big mono center value. The Command-Center centerpiece. |
| `ContextBadge` | Gaming/Work/Plex pill with icon + confidence %, subtle pulse when auto-switching. |
| `ConsoleDock` | collapsible dock, tabs **Activity · Assistant · Output**, `HudMonoSmallStyle` text, present on every page. Replaces the panel + the separate console window. |

---

## 4. Command Center — layout mock

```
┌─ Context: ◗ GAMING 94%   ⌘ Search…           ⏻ Automation: ARMED   ◉ Health 87 ─┐
│                                                                                  │
│   ┌────────────────────┐   ┌── CPU ─────┐ ┌── GPU ─────┐ ┌── RAM ─────┐         │
│   │      ╭───────╮     │   │  22.4 %    │ │  12.4 %    │ │  21.3 GB   │         │
│   │     │   87   │     │   │ ▁▂▃▅▃▂▁ ▲  │ │ ▁▁▂▂▁▁▁    │ │ ▃▃▄▄▃▃▃    │         │
│   │      ╰───────╯     │   │ 46°C  4.3GB│ │ 48°C 2152  │ │ 31 % used  │         │
│   │   SYSTEM HEALTH    │   └────────────┘ └────────────┘ └────────────┘         │
│   └────────────────────┘                                                         │
│                                                                                  │
│   NEEDS ATTENTION                                AUTOMATION                       │
│   ┌──────────────────────────────┐   ┌────────────────────────────┐            │
│   │ ⚠ Drive E: nearly full   →Fix │   │ ◗ Gaming profile  · armed  │            │
│   │ ⓘ Secure Boot disabled  →View │   │ 🧠 2 rules learning        │            │
│   │ ⚡ Boost privacy        →Apply │   │ ⏱ Nightly cleanup · 02:00  │            │
│   └──────────────────────────────┘   └────────────────────────────┘            │
│                                                                                  │
├─ Console  [ Activity ·  Assistant ·  Output ]                            ▲ / ▼ ─┤
│  22:38  Rule suggestion pass complete                                            │
│  22:35  NvApiGpuBackend: found NVIDIA GeForce RTX 5080                            │
└──────────────────────────────────────────────────────────────────────────────┘
```

---

## 5. Platform capabilities adopted (Windows App SDK toolbox)

From the capability review against the current stack (WinUI 3 · WinApp SDK 1.7 · .NET 10 · **unpackaged**):

| Capability | Today | Redesign |
|-----------|-------|----------|
| **Custom title bar** (`ExtendsContentIntoTitleBar` + `AppWindowTitleBar`) | ❌ standard caption | ✅ the **context bar** becomes the title bar |
| **Backdrops** | Mica only | Mica base + **DesktopAcrylic** Console dock + Mica Alt surfaces |
| **Composition / Toolkit Animations** | ❌ | ✅ accent glow/pulse, animated telemetry, page transitions |
| **Win2D** (`Microsoft.Graphics.Win2D`) | ❌ | ✅ custom glowing **HealthRing**, richer sparklines/gauges |
| **Community Toolkit (owned, underused)** | SettingsControls only on Settings; Converters bypassed | ✅ SettingsControls across Optimize/System; toolkit Converters replace ~25 hand-rolled dups |
| **New Toolkit packages** | — | `…Animations`, `…Controls.Sizers` (resizable dock), `…Controls.Segmented` (hub/console tabs) |
| **Power APIs** (`PowerManager`) | ❌ | ✅ real AC/battery/energy-saver signal → context + power tile |

## 6. Phased roadmap (on WinUI, low-risk first)

| Phase | Scope | Risk |
|-------|-------|------|
| **1 — Foundation** | NuGet (Animations, Sizers, Segmented, Win2D); `Styles/Tokens.xaml`; centralize converters into `App.xaml`; **title-bar extension + Acrylic dock backdrop**; build the 7 core controls (HealthRing via Win2D, with glow/motion); ship **Command Center** as the reference page. *Additive — existing pages untouched.* | Low–Med |
| **2 — Shell** | hub-based `NavigationView` (5 hubs), top context bar, `ConsoleDock` replacing the panel + console window | Medium |
| **3 — Migrate** | move pages onto tokens + components, hub by hub (Monitor first); strip hardcoded font/spacing/color | Medium (incremental) |
| **4 — Polish** | motion depth, full a11y pass (contrast, keyboard, screen-reader on new controls) | Low |
| **5 — Docs** | `DESIGN-SYSTEM.md` — tokens, controls, patterns | Low |
| **6 — (optional, later) Packaging** | MSIX identity → Start-menu, auto-update, a vitals **Widget**, Copilot-invokable **App Actions**. Stays off the critical path; revisit after the shell lands. | Med |

Phase 1 is self-contained and buildable/screenshot-able without disturbing today's app.
