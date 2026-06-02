# Optimizer

A comprehensive Windows system optimization, diagnostics, and management platform built with WinUI 3.

## Features

### Optimization & Tuning
- 12 optimizations across Performance, Network, Storage, System categories
- CPU power management presets (Stock, Mild Tune, Maximum Performance, Quiet/Cool)
- Built-in CPU stress test with thermal watchdog (auto-revert on overheat)
- Profile marketplace with 7 curated profiles
- Custom profile creation + snapshot management
- Smart profile auto-switching by time or running process

### Live System Monitoring
- Real-time CPU/Memory/GPU/Disk/Network metrics with sparkline charts
- Live sensor readings via LibreHardwareMonitor (CPU/GPU temps, fan RPMs, voltages, power)
- Per-core usage histogram
- Top processes with priority/affinity controls
- Single polling coordinator (ISystemDataBus) — eliminates timer races

### Diagnostics
- One-click Quick Scan (7 checks) and Full Scan (10 checks)
- SMART disk health (temperature, wear, predicted failure)
- Driver conflict and outdated driver detection
- Performance bottleneck detection (30s memory leak / high-CPU analysis)
- Network deep diagnostics: traceroute, MTU optimization, 100-ping packet loss
- Display test page (W/K/R/G/B fullscreen for dead pixel/color check)

### Security & Privacy
- Privacy dashboard with 15 toggles + composite Privacy Score
- Windows Defender / Firewall / BitLocker status
- DNS configuration (Cloudflare, Google, Quad9, OpenDNS, AdGuard, custom)
- Service Manager with safety recommendations

### Hardware & Updates
- Complete system inventory (CPU/GPU/RAM/Mobo/Storage/Network/Display/OS)
- Windows Update history + winget app updates
- BIOS/firmware information
- GPU vendor tool detection (MSI Afterburner, NVIDIA App, AMD Adrenalin)

### Enterprise
- Fleet roster (CSV import/export)
- Configuration templates with DSC/Intune/WinGet export
- Compliance frameworks (CIS Benchmark, NIST 800-171, HIPAA, SOC 2)

### Intelligence
- ML.NET-backed recommendation acceptance prediction
- Statistical anomaly detection on system metrics
- Smart Insights (CPU bottleneck, battery health, service bloat, etc.)
- Personalization tracking (accept/dismiss preferences)

### Cross-Platform
- **Desktop:** Native WinUI 3 (this app)
- **Remote API:** Embedded REST API + OpenAPI/Swagger UI
- **CLI:** `optimizer` console tool (status / apply / scan / cleanup)
- **Mobile companion:** Installable PWA (works on iOS/Android home screens)
- **Native mobile:** .NET MAUI project (iOS + Android targets)

## Requirements

- Windows 10 22H2+ or Windows 11
- .NET 10 Runtime (Desktop)
- Administrator privileges
- ~200 MB disk space

## Project Structure

```
Optimizer/
├── Optimizer.WinUI/        Main desktop app (22 pages, ~40 services)
├── Optimizer.Cli/          Command-line tool
├── Optimizer.Mobile/       .NET MAUI iOS/Android companion
├── Optimizer.WinUI.Tests/  Test suite (190+ tests, xUnit)
├── Optimizer.Packaging/    MSIX packaging project
├── Optimizer.WinUI/WebDashboard/  PWA (HTML/JS/SW)
└── docs/                   Roadmap, cross-platform plan, V4-V7 vision
```

## Quick Start

### Build

```powershell
git clone https://github.com/grunkiD2/Optimizer.git
cd Optimizer
dotnet build Optimizer.WinUI/Optimizer.WinUI.csproj -c Release -r win-x64
```

### Run

```powershell
.\Optimizer.WinUI\bin\Release\net10.0-windows10.0.22621.0\win-x64\Optimizer.WinUI.exe
```

### Run tests

```powershell
dotnet test Optimizer.WinUI.Tests/Optimizer.WinUI.Tests.csproj
```

### CLI

```powershell
# Build
dotnet build Optimizer.Cli/Optimizer.Cli.csproj

# Use (requires desktop app running with API enabled)
$env:OPTIMIZER_TOKEN = "your-token-from-settings"
.\Optimizer.Cli\bin\Debug\net10.0\optimizer.exe status
.\Optimizer.Cli\bin\Debug\net10.0\optimizer.exe apply preset-gaming
.\Optimizer.Cli\bin\Debug\net10.0\optimizer.exe scan
```

### Mobile (PWA)

1. Start desktop app, enable Remote API in Settings
2. From your phone (same Wi-Fi) browse to `http://<desktop-IP>:8765`
3. iOS Safari: Share > Add to Home Screen
4. Android Chrome: menu > Install App
5. Paste the API token to connect

## Architecture

- **MVVM** via CommunityToolkit.Mvvm (ObservableObject, RelayCommand)
- **DI** via Microsoft.Extensions.Hosting (38+ services registered)
- **Plugin patterns:**
  - `IOptimizationHandler` — 13 handlers for individual optimizations
  - `IDiagnosticPlugin` — 10 plugins for diagnostic checks
- **Coordinator services:**
  - `ISystemDataBus` — single polling coordinator (replaces 5 timers)
  - `IWmiQueryService` — WMI cache with TTL
  - `IPowerShellRunner` — unified PowerShell execution
- **Hosted services:** `BackgroundMonitorService`, `ProfileAutomationService`, `SystemDataBus`

## License

[MIT](LICENSE) — Open source

## Roadmap

See [docs/](docs/) for the full roadmap. Major milestones:
- V1: WinUI 3 migration ✓
- V2: 14 Tier 1 features ✓
- V3: Overclocking, onboarding, localization ✓
- V4-V7: Ecosystem (marketplace), AI (ML.NET), Enterprise (compliance), Cross-platform ✓
- V8+: Real native mobile, server-side marketplace, certified code signing
