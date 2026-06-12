# Optimizer — Backlog

Parked opportunities, not yet scheduled.

## In flight / parked-but-designed

- **Per-Process Power Intelligence (PPI)** — full design brief at [`docs/POWER-INSIGHTS.md`](POWER-INSIGHTS.md). ETW + Energy Estimation Engine + per-context drift detection. Read-only. Ready to schedule. *Federation note (2026-06-12): the Fancontrol system's brain wants exactly this signal to fix its focus≠load-source attribution bias — build PPI with a consumer contract in mind (see docs/MACHINE-OWNERSHIP.md).*
- ~~**Fancontrol federation, Phase 2 (command bridge)**~~ — ✅ SHIPPED 2026-06-12: FixedTimeEquals token check + FancontrolCommandService (apply-profile/night/ack-alerts via ctl.ps1, fail-closed validation) + 4 assistant tools + REST POSTs + PWA quick controls. Phase 3 (telemetry → SQLite + /api/fancontrol/history + thermal-alert dedup) shipped same day. Remaining federation ideas: Optimizer diagnostics → ntfy; surface /api/fancontrol/history as charts in the Monitor hub. (The PWA/mobile phone surfaces were removed entirely 2026-06-12 — VISION.md scope.)
- ~~**Settings hard-reset sharp edge**~~ — ✅ ROOT-CAUSED + FIXED 2026-06-12: the wipes came from SettingsService TESTS writing the real `%LocalAppData%` file (`Reset()`/`Save()` on unloaded instances). Fixed with a path-injectable ctor + isolated temp files in SettingsServiceTests/SettingsMergeTests, plus a `.rejected` forensics copy + error log when a malformed file fails to parse (no more silent total reset).
- **Flaky test (unidentified)** — one 1/602 failure observed on a cold run 2026-06-12, did not reproduce in 4 reruns and the test name wasn't captured. If it fires again: capture the `[FAIL]` line; likely another static-EngineLog parallel leak (ConsoleViewModelTests had the same shape, fixed in 7afca70).

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
