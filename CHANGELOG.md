# Changelog

All notable changes to Optimizer are documented in this file.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versioning follows [Semantic Versioning](https://semver.org/).

---

## [Unreleased]

### Added
- OpenAPI/Swagger documentation served at `/openapi/v1.json` and `/swagger.html`
- Swagger UI page at `/swagger.html` with Bearer auth token injection
- Expanded test suite: 121 tests across 9 test classes (was 22)
- New test classes: ProfileServiceTests, HistoryServiceTests, MarketplaceServiceTests,
  DiskHealthServiceTests, IntelligenceServiceTests, ModelTests, ByteFormatterExtendedTests,
  PowerShellRunnerTests
- GitHub Actions release workflow (`release.yml`) triggered on version tags
- Release artifacts: WinUI zip, CLI zip, Web Dashboard zip, SHA256 checksums
- Self-signed code signing in release pipeline (documented SmartScreen behavior)

---

## [2.0.0] — 2026-06 (current development state)

### Added — V3 Features
- LibreHardwareMonitor integration for hardware telemetry (CPU/GPU temp, power, fan RPM)
- Advanced overclocking subsystem with stress testing
- Deep diagnostics suite (bottleneck detector, driver diagnostics, system repair)
- Smart Recommendations engine with dismissal/snooze/accept tracking
- Smart Insights page with anomaly detection
- Profile Marketplace with community profiles and ratings
- ML.NET intelligence engine (recommendation acceptance model, 3-sigma anomaly detection)
- Enterprise/Fleet management (multi-machine orchestration)
- REST API on `localhost:7071` with Bearer auth and OpenAPI docs
- Web dashboard (PWA) served from `/` on the API host
- Swagger UI at `/swagger.html`
- CLI tool (`Optimizer.Cli`) for scripted optimizations
- Mobile companion (MAUI + PWA)

### Added — V2 Features
- DNS configuration on Network page
- Privacy dashboard on System page
- System tray integration with H.NotifyIcon
- Dashboard real-time charts
- Hardware info page
- SMART disk health monitoring
- Windows Service Manager
- Per-process power management
- Boot time analyzer
- Diagnostics page with WinSAT scores
- Recommendations engine
- Windows Update page
- Security center page (Defender, BitLocker, firewall status)
- Network speed test and latency monitor
- Event log viewer
- Expanded cleanup operations
- Smart profile switching (automation rules)
- Toast notification system
- Reports page (PDF/HTML export)
- Overclocking subsystem (CPU/RAM profiles)
- Driver diagnostics with vendor APIs
- Onboarding wizard
- Localization infrastructure (en/es/de/fr/ja)

### Added — V1 Features
- WinUI 3 / Windows App SDK redesign
- Profile system with built-in presets (Gaming, Productivity, Battery Saver, Performance)
- Undo/redo for all registry changes
- Settings service with JSON persistence
- Theme/backdrop support (Mica, Acrylic)
- Serilog structured logging
- xUnit + Moq test project
- GitHub Actions CI workflow
- REST API + Web dashboard
- CommunityToolkit.Mvvm MVVM pattern throughout

### Fixed
- CPU performance counter blocked UI thread (Thread.Sleep → async warm-up)
- Virtual memory counter returned wrong value
- UndoService double-load on startup
- Profile import crash on invalid JSON
- OptimizationCard text/button overflow in narrow layouts

### Technical
- Extracted 15+ interface abstractions (IProfileService, IHistoryService, etc.)
- OptimizationIds constants class eliminates magic strings
- ByteFormatter consolidates all byte/speed formatting
- SettingsViewModel registered as singleton

---

## [1.0.0] — Legacy WPF version

Original single-file WPF optimizer. Replaced by the WinUI 3 redesign in v2.0.
