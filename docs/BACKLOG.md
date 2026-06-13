# Optimizer — Backlog

Parked opportunities, not yet scheduled.

## Harvested from the pre-redesign roadmaps (2026-06-12 audit)

Full audit of the 7 deleted roadmap docs (ROADMAP-V2/V3-V7/V8×2, AUTOMATION, BACKLOG-ACTIONABILITY, CROSS-PLATFORM — survives in the laughing-mendel worktree): **80 items done, 37 partial, 32 missing, 25 scrapped-by-vision.** The survivors, curated:

### Worth doing (vision-clean, concrete value)
- **Monitor → Vitals rolling charts** — restore the chart engine deleted with DashboardPage (the IA plan explicitly called for a Vitals section). Build it as ONE chart engine that also draws `/api/fancontrol/history` → folds into convergence Etape 1.
- **Event Log actionability** — pattern-match event signatures → known-fix categories, wire `SystemRepairService` actions into a confirm-driven apply flow on EventLogsPage (BACKLOG-ACTIONABILITY §1).
- **In-app Windows Update** — WUApiLib COM interop to query/trigger update scans on UpdatesPage instead of launching Settings (§3).
- **Cleanup scope expansion** — hibernation file, System Restore points, browser caches (Edge/Chrome/Firefox); 5-30 GB easy wins.
- **Per-process bandwidth breakdown** (ETW/IPHelper) — sister analysis to PPI: find rogue uploaders the way PPI finds power drainers.
- **Process detail flyout** — modules/threads/handles for root-causing hung processes ("analysis, not symptom-patching").
- **DNS preset buttons** (Cloudflare/Google/Quad9) — old Tier-1 item, never shipped.

### Nice-to-have (parked)
Disk-space treemap (WinDirStat-style) · composite 0-100 health breakdown per category (the ring exists; the breakdown doesn't) · global hotkeys · compact always-on-top widget · firewall-rules viewer + port scan · Templates-UI polish · accessibility pass (AutomationProperties/Narrator/high-contrast) AFTER convergence Etape 4 so it isn't done twice.

### Rejected in the audit (with reasons — do not resurrect without a scope change)
- **All 9 items in AUTOMATION.md** — auto power-plan/profile switching belongs to Process Lasso/fgwatch on this machine (MACHINE-OWNERSHIP.md); the learning engine's confirm-first model superseded the rest.
- OAuth2/public API + rate limiting, voice input, shareable-URL/QR profiles, fleet/DSC/Intune — VISION.md (single-user, local-only, analysis-not-chat).
- Persist process priorities across boots + integrated "Game Mode" combo — Lasso (ProBalance/Gaming Mode) and fgwatch own those domains here.
- Stale claims corrected: live speedtest EXISTS (`NetworkSpeedTestService`), ClaudeClient double-bill was FIXED (`26f865d`).

## Performance — cross-layer profile (2026-06-13, read-only, Impact × Effort)
Live measurements: data.json 205 KB/fetch, optimizer.db ~5 MB, history.json 423 KB, a daily app.log hit the 10 MB cap.

**Phase 1 (high value, low effort):**
- **Cap `ConsoleViewModel.Lines`** — unbounded ObservableCollection grows for the whole session (verbose console on by default); trim oldest to ~1–2k. *High.* (`ViewModels/ConsoleViewModel.cs:19,31`)
- Cache `TotalPhysicalMemory` once + hold a persistent `PerformanceCounter` instead of per-tick WMI query + counter create/dispose. (`Services/SystemMonitorService.cs:333,349`)
- SQLite `journal_mode=WAL` + `busy_timeout` — kills reader/writer contention (currently default DELETE mode, no WAL files; 5 s/30 s writers vs UI/API reads). (`Services/Data/DatabaseService.cs:16`)
- 2–5 s TTL cache on `DetectContextAsync` — `Process.GetProcesses()` runs 5–10× per assistant turn. (`Services/ContextAuthorityService.cs:47`)

**Phase 2 (high value, medium effort):**
- Read-through cache (sub-second TTL) on `GetSnapshot()`, or point consumers at `SystemDataBus.LatestSensors` — the 205 KB data.json is fetched + regex-parsed **2–4× per cycle** (SystemDataBus, GpuControlService, /api/sensors, PowerAttribution, SmartInsights). (`Services/ExternalLhmSensorService.cs:45`)
- Async sensor path (`GetSnapshotAsync`) + `Interlocked` reentrancy guard — stop sync-over-async thread pinning + 2 s-timer pile-up. (`Services/SystemDataBus.cs:112`)
- Activate window first, then async DB-init + `*.Load()` — 423 KB history.json + 5 MB DB init block first paint. (`App.xaml.cs:377`)
- Coalesce assistant stream deltas — per-token O(n²) string concat + UI dispatch per token. (`ViewModels/AssistantViewModel.cs:104`)

**Phase 3 (polish):** prepared-statement reuse in batch inserts (17k recompiles on backlog) · `rec-preferences.json`(121 KB)→SQLite · retention-column indexes (PowerDriftEvents/AnomalyAlerts/ScheduleExecutions/ProfileApplications) · HealthRing single-blur · page `NavigationCacheMode` · sparkline buffer reuse · tail (not full-read) events.jsonl · `HistoryService`→SQLite · `ParseNumber` → `[GeneratedRegex]` · DEBUG Kestrel auto-start deferral.

## In flight / parked-but-designed

- ~~**Per-Process Power Intelligence (PPI)**~~ — ✅ CORE SHIPPED 2026-06-12 (estimated-attribution model; see the status header in [`docs/POWER-INSIGHTS.md`](POWER-INSIGHTS.md)). Remaining: Monitor-hub UI page, ContextualPromptBuilder drainer block, Recommendations-row surfacing, long-run verification (#4/#6/#7), and the Fancontrol-brain consumer (`/api/power/processes` is the contract — brain-side consumption is a reviewed Fancontrol change).
- ~~**Fancontrol federation, Phase 2 (command bridge)**~~ — ✅ SHIPPED 2026-06-12: FixedTimeEquals token check + FancontrolCommandService (apply-profile/night/ack-alerts via ctl.ps1, fail-closed validation) + 4 assistant tools + REST POSTs + PWA quick controls. Phase 3 (telemetry → SQLite + /api/fancontrol/history + thermal-alert dedup) shipped same day. Remaining federation ideas: Optimizer diagnostics → ntfy; surface /api/fancontrol/history as charts in the Monitor hub. (The PWA/mobile phone surfaces were removed entirely 2026-06-12 — VISION.md scope.)
- ~~**Settings hard-reset sharp edge**~~ — ✅ ROOT-CAUSED + FIXED 2026-06-12: the wipes came from SettingsService TESTS writing the real `%LocalAppData%` file (`Reset()`/`Save()` on unloaded instances). Fixed with a path-injectable ctor + isolated temp files in SettingsServiceTests/SettingsMergeTests, plus a `.rejected` forensics copy + error log when a malformed file fails to parse (no more silent total reset).
- **Flaky test — IDENTIFIED 2026-06-13:** `ConsoleViewModelTests.Appends_a_line_when_an_event_is_published` failed 1/595 on a cold run during R6 work, passed isolated (3/3) and on full-suite rerun (595/595). Shape: ConsoleViewModel subscribes to the (static-ish) event-bus/EngineLog surface — parallel tests publishing events can interleave. Fix candidate: assert with Contains-not-exact-count, or give the test its own EventBus instance (same class of fix as 7afca70).

## From the Windows Settings reference (registry-backed)

Source: *Reference for Windows 11 and Windows 10 settings* (learn.microsoft.com/windows/apps/develop/settings/settings-common).

### 1. Use Windows' stored "user intent" as a context-detection signal — ✅ DONE 2026-06-04

`ContextDetectionService` now reads:
- `HKCU\Software\Microsoft\Windows\CurrentVersion\CloudExperienceHost\Intent` — bitmask:
  `0b10`=Gaming, `0b100`=Family, `0b1000`=Creativity, `0b10000`=Schoolwork,
  `0b100000`=Entertainment, `0b1000000`=Business, `0b10000000`=Development
- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock\devModeEnabled`

It uses the bitmask as a **bias** that breaks ties when processes are ambiguous, and the
declared intent flows through to the assistant via `ContextualPromptBuilder` ("at setup the
user declared this PC is for: Gaming, Development, DevMode"). Mapping: Gaming→Gaming;
Entertainment→Plex; Business/Development/Creativity/DevMode→Work. See
[`UserIntent`](../Optimizer.WinUI/Services/ContextDetectionService.cs).

### 2. Registry tweaks shipped as optimization handlers — ✅ DONE 2026-06-04

| Tweak | Handler | Hub category |
|---|---|---|
| Disable Autoplay | `DisableAutoplayHandler` | System (privacy) |
| Transparency effects off | `DisableTransparencyEffectsHandler` | Performance |
| Accent on title bar / Start | `EnableAccentTitleBarsHandler` | System (personalization) |
| Quiet Windows Update UX (IsContinuousInnovationOptedIn / IsExpedited / AllowMUUpdateService) | `ConfigureWindowsUpdateUxHandler` | System (Updates) |
| USB error / weak-charger notifications | `DisableUsbNotificationsHandler` | System (notifications) |

All registered in `App.xaml.cs` DI, all reversible via the existing `IUndoService.CaptureRegistry`
path, all picked up automatically by the optimization-card UI. The `RestartNotificationsAllowed2`
key is intentionally **not** flipped — restart nags are useful, the goal here was to suppress the
surprise-update paths only.

## Not practical from that doc (Cloud Data Store — read-only via readCloudDataSettings.exe)

- Focus Assist / Do-Not-Disturb (`QuietHoursProfile`/`QuietMoment`) — great for a Gaming profile,
  but needs a *different* toggle mechanism than this doc documents.
- App inventory metadata (`AppMetaData`: installSource, wingetID, lastLaunchTime, isPinned).
- Multi-display prefs (`minimizeWindowsOnMonitorDisconnect`, `rememberWindowLocationsPerMonitorConnection`).

## Bigger-picture

- The doc's framing is Backup/Restore **data portability**, which rhymes with our profiles/snapshots/
  undo. A "Windows settings backup/restore" feature is conceivable — feature, not optimization.

## Redesign housekeeping

- ✅ DONE 2026-06-04: Centralized the ~19 duplicate `BoolToVisibility` converters into `App.xaml`
  (every page used to declare its own instance; now one shared resource).

## Open items from this session

- **`Services/Assistant/ClaudeClient.cs` double-call refactor** — `CLAUDE.md` notes that the
  current implementation calls both `CreateStreaming` and non-streaming `Messages.Create` per
  turn, which double-bills. Streaming alone is sufficient for tool-use accumulation per the
  SDK (`TryPickStart` / `TryPickDelta` / `TryPickStop` + the `ContentBlock` variants); refactor
  is mechanical.

- **`SessionPersistence.cs:22` null-dereference warning** — only warning in the build; quick fix
  but not load-bearing.
