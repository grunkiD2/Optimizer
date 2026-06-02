# Cross-Platform Migration Plan

This document outlines how to port Optimizer to macOS, Linux, and mobile platforms.

## Current State

- **Target:** Windows 10 22H2+ via WinUI 3 + Windows App SDK 1.7
- **Hard Windows dependencies:**
  - WMI / Win32 APIs (hardware info, installed services, device queries)
  - PowerShell cmdlets (`powercfg`, `Get-PhysicalDisk`, `netsh`, etc.)
  - Windows-specific paths (`%LocalAppData%\Optimizer\`)
  - `LibreHardwareMonitorLib` — Windows-only
  - `System.Diagnostics.PerformanceCounter` — Windows-only
  - Registry-based settings (Privacy Dashboard tweaks)
  - `System.Management` (WMI) — Windows-only
  - `TaskScheduler` library — Windows Task Scheduler API
  - `System.ServiceProcess.ServiceController` — Windows Services

---

## Migration Strategy

### Phase 1 — Extract platform-agnostic Core

Create `Optimizer.Core` targeting `net10.0` (no `-windows` suffix).

Move to Core (no Windows deps):
- `Models/` — all POCOs are already portable
- `HistoryService` — file I/O only
- `ProfileService` — file I/O only
- `SettingsService` — file I/O only, swap registry path to cross-platform `AppData`
- `ByteFormatter` and other pure helpers
- ML model inference wrappers (`AnomalyDetectionService`, `SmartProfileService`)

Keep in `Optimizer.Platform.Windows`:
- WMI hardware queries (`HardwareInfoService`, `SmartDiskService`)
- Registry-based privacy tweaks (`PrivacyDashboardService`)
- Performance counter polling (`MetricsService`)
- PowerShell process spawning
- LibreHardwareMonitor sensor reading
- Boot-time analysis (Windows event log)
- Service manager (Windows SCM)

Define interfaces in Core that each platform must implement:
- `IHardwareInfoProvider` — CPU, GPU, RAM, motherboard
- `IMetricsProvider` — real-time CPU/GPU/memory/disk percentages
- `ISensorProvider` — temperatures, fan speeds, voltages
- `IOptimizationExecutor` — applies tweaks (registry on Win, `sysctl` on Mac, etc.)
- `IPowerManagementProvider` — power plans / profiles
- `IStorageProvider` — disk health, SMART data
- `IServiceManager` — start/stop background services

### Phase 2 — Choose UI framework

#### Option A: Avalonia UI (recommended)

- Cross-platform: Windows, macOS, Linux, Android, iOS, WebAssembly
- XAML-based — migration from WinUI XAML is mechanical
- MIT-licensed, mature ecosystem
- Pros: single codebase, closest to WinUI paradigm
- Cons: control set differs from WinUI (no `NavigationView` equivalent — use `SplitView`)

Migration notes:
- `x:Bind` → Avalonia compiled bindings or community binding extensions
- `VisualStateManager` → Avalonia `Classes` + style triggers
- `WinUI.Controls.SettingsControls` → re-implement or use community templates
- `H.NotifyIcon` → Hardcodet.NotifyIcon (same author, cross-platform)

#### Option B: .NET MAUI

- Microsoft-supported, mobile-first
- Targets iOS, Android, Windows (via WinUI), macOS Catalyst
- Pros: official, good mobile-native feel
- Cons: heavier, desktop experience is thinner, less mature on Linux

#### Option C: Uno Platform

- Runs WinUI XAML on other platforms by reimplementing the WinUI API
- Pros: reuses existing XAML almost unchanged
- Cons: smaller community, incomplete WinUI coverage, large dependency

**Recommendation:** Avalonia for maximum desktop quality; MAUI if mobile parity is the primary goal.

### Phase 3 — Per-platform optimization providers

#### macOS (`Optimizer.Platform.Mac`)

| Feature | macOS approach |
|---------|---------------|
| CPU/memory metrics | `sysctl`, `/proc`-equivalent via `host_statistics` |
| Temperatures | `IOKit` (via P/Invoke or `osx-libc`) |
| Power management | `pmset` command, `IOPMLib` |
| Services | `launchctl` (launchd) |
| Disk health | `diskutil info`, `smartmontools` |
| Startup items | Login items API |
| Privacy tweaks | `defaults write`, TCC database |

Required entitlements: `com.apple.security.temporary-exception.sbpl` for some system queries.

#### Linux (`Optimizer.Platform.Linux`)

| Feature | Linux approach |
|---------|---------------|
| CPU/memory metrics | `/proc/stat`, `/proc/meminfo` |
| Temperatures | `lm-sensors` (`libsensors`) |
| GPU metrics | NVML (NVIDIA), ROCm-smi (AMD), `intel-gpu-tools` |
| Power management | `cpufreq`, `TLP`, `power-profiles-daemon` |
| Services | `systemctl` (systemd) |
| Disk health | `smartmontools` (`smartctl`) |
| Startup items | systemd user units, XDG autostart |
| Privacy tweaks | D-Bus settings, `/etc/sysctl.conf` |

Distribution support targets: Ubuntu 22.04 LTS, Fedora 38+, Arch (rolling).

### Phase 4 — Mobile companion apps

The REST API (Batch 41, port 8765) is already the bridge to mobile.

- **iOS companion** (SwiftUI): reads `/api/metrics`, `/api/sensors`, triggers `/api/apply/*`
- **Android companion** (Jetpack Compose): same endpoints
- Communication over local LAN or Tailscale/VPN
- Authentication: existing Bearer token from GUI Settings

No .NET code needed on mobile — the REST surface is the contract.

---

## Effort Estimate

| Phase | Scope | Estimate |
|-------|-------|----------|
| 1 — Extract Core | Interface definitions + move POJOs | 2 weeks |
| 2 — Avalonia desktop | Feature parity on Win/Mac/Linux | 6 weeks |
| 3 — macOS provider | Hardware + power + services | 3 weeks |
| 3 — Linux provider | Hardware + power + services | 3 weeks |
| 4 — iOS companion | Read-only dashboard + apply | 4 weeks |
| 4 — Android companion | Read-only dashboard + apply | 4 weeks |

**Total to full multi-platform parity: ~22 weeks (single developer)**

---

## Recommended Roadmap

| Quarter | Milestone |
|---------|-----------|
| Now | Ship Windows v2.0 (current feature/winui3-redesign) |
| Q3 | Extract `Optimizer.Core` + interface contracts |
| Q4 | Avalonia desktop port (Win/Mac/Linux) |
| Q1 next year | macOS + Linux platform providers |
| Q2 next year | Mobile companion apps |

---

## Open Questions

1. **Telemetry consent** — GDPR/CCPA requirements differ per platform; need per-region consent flow
2. **Code signing** — macOS requires Developer ID ($99/yr); Linux packages need GPG signing
3. **Distribution** — MSIX (Windows Store), Homebrew cask (macOS), Flatpak/Snap (Linux)
4. **Marketplace catalog** — currently bundled JSON; move to CDN for cross-platform delivery
5. **Enterprise license model** — per-seat vs. per-fleet pricing for multi-platform?
6. **Admin elevation** — `sudo` on Unix vs. UAC on Windows; UX differs significantly
