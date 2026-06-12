# Per-Process Power Intelligence (PPI) — feature plan

**Status:** CORE SHIPPED 2026-06-12 (`Services/Power/`). **Model decision:** the §4 fallback
was promoted to primary — on this desktop (no battery) the Energy-Estimation-Engine ETW
provider emits no per-process energy, and ETW sessions would demand elevation the app
doesn't run with. Attribution = per-process CPU-time share × the MEASURED CPU package watts
(via ISensorService → the Fancontrol federation's LHM server = real measurement, not TDP).
Clearly labeled "estimated"; sum-to-attributed-pool holds by construction. Shipped: sampler
(`PowerAttributionService`), ADS drift loop (`PowerInsightsService`, Welford + 72 h half-life
+ modified-z with σ-floor, 4 h dedup), SQLite tables (PowerSnapshots/PowerBaselines/
PowerDriftEvents — PascalCase, deviating from §5's snake_case for codebase consistency),
`/api/power/{processes,drift}`, assistant tool `get_power_drainers`, settings incl. exclusion
regexes. Livetested: context-aware attribution live (Plex ctx, 65 W package), synthetic
CPU-load appeared as #1 drainer within one 30 s tick, self-exclusion works. 635/635 tests.
**Residuals (post 51a0d35 — UI/prompt/recommendations SHIPPED same day):** verification checks #4/#6/#7 (need multi-day runtime). The original ETW path below is kept as the design record.

## 0. Concept in one sentence

Subscribe to Windows' Energy Estimation Engine ETW provider (the same telemetry WPA's "Energy Estimation Engine Summary (by Process)" table reads), attribute power draw to **individual processes** live, learn per-context baselines, and surface drift via the existing Recommendations panel — **read-only, no kernel writes, no kernel driver beyond LHM's PawnIO**.

## 1. Verification strategy (8 distinct checks)

| # | Check | Pass criterion | Where it lives |
|---|---|---|---|
| 1 | **WPA cross-check** | Capture a 60s trace with `wpr -start GeneralProfile` + our app reading the same ETW stream. Aggregate per-process energy should match WPA's table within ±5%. | Manual verification doc; one-time before merge |
| 2 | **Sum-to-system check** | Σ(per-process power) should equal system total (from LHM/PawnIO `Power.CpuPackage`) within ±10% over a 5-min window. | Integration test, runs in CI on a real machine label |
| 3 | **Synthetic-workload test** | Spin a known CPU-bound test process (`Optimizer.WinUI.Tests.PowerLoadFixture`) at 1-core 100% load. Verify it appears in top-3 within 30s and its attributed power is ≥40% of the package power increment. | xUnit integration test (`PpiAttributionTests`) |
| 4 | **Observer-effect check** | Measure idle + loaded power consumption with PPI **disabled** vs **enabled** across 10 trials. Difference must be < 1.5% of total power (PPI must not perturb what it measures). | Benchmark fixture, manual run, results in `docs/PPI-VERIFICATION.md` |
| 5 | **Schema fidelity** | Assert that every `EnergyEstimationProvider` event we parse round-trips through our serializer with no field loss. | Unit tests against captured `.etl` files in `tests/fixtures/` |
| 6 | **Baseline-stability test** | Same workload, same context, two separate 10-min windows: the resulting baseline (median + IQR per process) should overlap by ≥90%. | Integration test, single-PC only (marked `[Trait("Env", "Hardware")]`) |
| 7 | **False-positive ceiling** | Run for 24h with no anomalies expected (steady-state). Anomaly suggestions surfaced must be ≤2 over the whole window. | Long-running test (manual, weekly) |
| 8 | **Constraint-compliance audit** | Verify by code review + test: no `RegistryKey.SetValue`, no `Process.Kill`, no `SetPriorityClass` invoked by PPI services. Read-only enforced at the service-interface boundary. | Static check in `Optimizer.WinUI.Tests/Architecture/PpiReadOnlyTests.cs` (NetArchTest or similar) |

## 2. Use cases (7, all already-modeled contexts)

1. **Plex transcoding power envelope.** User notices Plex Server consumes ~45 W at idle + 75 W transcoding 4K HEVC. PPI surfaces transcoding-process attribution and flags when a 4K transcode consumes ≥2× the historical median for that codec — could mean h264 fallback (CPU instead of QuickSync) is being hit.

2. **Coding-context language-server runaway.** OmniSharp / Roslyn / TypeScript-server occasionally spin on a corrupt project state and consume 15 W for hours of idle wall-clock. PPI surfaces "language-server-1 has held 15 W for 47 min while VS Code window has been minimized" — a class of bug currently invisible to Task Manager because CPU % cycles up-down faster than the eye sees.

3. **Gaming-context overlay overhead.** Discord, OBS, and MSI Afterburner overlay services collectively consume 8–12 W during gaming sessions. PPI separates *background overhead* from *game-engine power*, letting the user see "this game is actually 110 W; 12 W of that is overlay tax." Concretely answers "should I disable RTSS?"

4. **Idle drift discovery.** User locks the PC, expects ~25 W. Actual draw is 60 W. PPI's "what's keeping us awake" view identifies the offender — typically a leaked timer, a Windows Search re-indexing event, or a Defender scan. Already partly visible via Reliability Monitor but not in real time.

5. **Battery (when used on laptop).** Top-N drainers by **energy** (not power) over the last hour. Differs from CPU% in Task Manager because energy = average power × time — a process that wakes every 60s for 100ms at 5W drains more battery than one running at 1W steady. PPI captures the *integral*.

6. **Per-context baseline learning.** Context-detection service already classifies Plex/Work/Gaming/Idle. PPI builds **per-context baselines** for the top-N processes in each. "Drift" is then *contextual* — Chrome at 8 W is normal under Work, anomalous under Idle.

7. **Root-cause feed for the LLM assistant.** When the user opens the assistant and asks "why is my system slow?", the assistant's `ContextualPromptBuilder` can now include the top-3 power drainers and their drift-vs-baseline state. Concrete, citable input that turns a vague question into actionable triage.

## 3. The automation: Adaptive Drift Surfacing (ADS)

**Design intent:** advanced enough to be a real differentiator, scoped narrowly enough to be safe.

### What ADS does
1. Maintains per-context, per-process online statistics (Welford μ/σ, EWMA, IQR).
2. Classifies each top-N process every 30s as *normal / elevated / anomalous* using a robust modified-z test against the per-context baseline.
3. **Surfaces** anomalies as `Recommendation` rows in the existing Automate hub Recommendations panel — never auto-applies.
4. De-duplicates: same (process, context, anomaly-class) doesn't re-surface for 4 hours unless the magnitude grows by ≥50%.
5. Decays old observations (exponential decay, half-life = 72h) so a process whose normal profile shifts (e.g., software upgrade) re-baselines smoothly.

### What ADS will NOT do
- Will not kill processes.
- Will not set priorities or affinities.
- Will not modify the registry.
- Will not write to `Power*` APIs.
- Will not call out to the network (the model is purely local).

### Why this counts as "advanced"
- **Contextual** rather than global. Most optimizers flag "Chrome uses 8 W" — useless. PPI flags "Chrome uses 8 W *under Idle*" — actionable.
- **Online** statistics, no batch retraining. Fits the single-PC, no-cloud, sparse-data shape.
- **Half-life decay** + magnitude-bump-only re-surfacing prevents alert fatigue without missing real drift.
- Plugs **directly into the existing Welford accumulator** in `PredictiveAlertService` — same algorithm family, narrower target.
- Optional upgrade path: the SAD framework (IJCAI 2025) for sparse anomaly detection without manual thresholds, if Welford-residual-z proves too noisy in practice.

### Kill-switch and exclusion list
- Single toggle: `Settings → Automate → Power anomaly suggestions` (`AppSettings.PpiSuggestionsEnabled`, default `false` until shaken out).
- Exclusion list (regex): `AppSettings.PpiProcessExclusions` — pre-seeded with `MsMpEng`, `TrustedInstaller`, `SearchHost`, `SystemSettings`, `Optimizer.WinUI` (don't flag ourselves). Editable from the Settings UI.
- Confirm-on-first-occurrence policy from the 8-phase learning engine applies the moment any *action* (not just a suggestion) is later added — but ADS itself doesn't take actions, so the kill switch is the only gate for this phase.

## 4. Requirements (hard + soft)

### Hard
- Windows 11 22H2+ for full `Microsoft-Windows-Energy-Estimation-Engine` ETW provider exposure (matches current TFM).
- Administrator elevation to subscribe to system ETW providers (the app already runs elevated when launched with "Relaunch as Admin").
- NuGet: `Microsoft.Windows.EventTracing.Processing.All` (TraceProcessor SDK, MIT-licensed, x64 only — already constraint-fitting because Optimizer is x64-only).
- SQLite schema additions (P1 already exists, just new tables).
- Background hosted service (already a pattern in the app — see `ScheduledOptimizationService`, `ContextAutomationService`).

### Soft
- ETW buffer tuning: 64 MB ring buffer, 2 KB per event ≈ 32 K events of history. Sufficient for 5-min windows at expected event volumes.
- Per-process keying: use `Pid + ProcessStartTime` (not name) for identity within a session. Re-key by name across PID-reuse.
- Performance budget: PPI service must hold steady-state CPU ≤ 1% on a 14900K, memory ≤ 80 MB. Enforced by a benchmark gate.

### Risks
| Risk | Mitigation |
|---|---|
| Energy Estimation Engine provider data unavailable on user's hardware (some integrated GPUs / older silicon) | Fallback: use `Microsoft-Windows-Kernel-Process` + `Microsoft-Windows-Kernel-Power` and approximate via CPU%×TDP — clearly labeled "estimated" in the UI |
| ETW session collisions with WPA / xperf | Use a dedicated private session name (`Optimizer-PPI`) and `EVENT_TRACE_PRIVATE_LOGGER_MODE` |
| Long-running ETW session leaks (logman stuck sessions on crash) | Register Ctrl+C / `AppDomain.ProcessExit` cleanup; on startup, scan and clean any orphaned `Optimizer-PPI*` sessions |
| Privacy concern (per-process power could be sensitive in some contexts) | All data is local-only and visible only to the user; explicit toggle to retain historical baselines vs. session-only |

## 5. Codebase implementation

### New files (proposed structure)
```
Optimizer.WinUI/Services/Power/
  ├── IPowerAttributionService.cs            // public interface — read-only
  ├── PowerAttributionService.cs             // ETW subscription, parsing, aggregation
  ├── PowerSnapshot.cs                       // immutable record { Pid, ProcessName, EnergyJoules, AveragePowerW, WindowSeconds, Timestamp }
  ├── ProcessPowerBaseline.cs                // per-(context,process) baseline state — Welford accumulators
  ├── PowerBaselineStore.cs                  // SQLite-backed persistence
  ├── DriftDetector.cs                       // modified-z test + half-life decay
  └── AdaptiveDriftSurfacingService.cs       // hosted service — runs every 30s, emits Recommendations

Optimizer.WinUI/ViewModels/
  └── PowerInsightsViewModel.cs              // drives the Monitor → Power Insights panel

Optimizer.WinUI/Views/
  └── PowerInsightsPage.xaml(.cs)            // see UI plan below

Optimizer.WinUI/Models/
  └── PowerInsightsModels.cs                 // ProcessPowerRow, DriftBadge, BaselineHistogram

Optimizer.WinUI.Tests/Services/Power/
  ├── PowerAttributionServiceTests.cs        // mock IPC ETW source, golden assertions
  ├── DriftDetectorTests.cs                  // synthetic distributions
  ├── PpiAttributionTests.cs                 // integration — needs hardware
  └── PpiReadOnlyTests.cs                    // architectural test: no writes
```

### Touched existing files
- `Services/Analytics/PredictiveAlertService.cs` — extend to consume PPI drift events as another anomaly source (does not replace its existing logic).
- `Services/Assistant/ContextualPromptBuilder.cs` — add top-3 power drainers + drift state to the assistant's context block.
- `Services/Data/DatabaseSchema.cs` — add three new tables: `power_snapshots`, `power_baselines`, `power_drift_events`. Migration via the idempotent ALTER path (Phase 1 pattern).
- `Settings/AppSettings.cs` — `PpiEnabled`, `PpiSuggestionsEnabled`, `PpiProcessExclusions`, `PpiBaselineHalfLifeHours` (default 72), `PpiDriftZThreshold` (default 3.5).
- `App.xaml.cs` (DI registration) — register `IPowerAttributionService`, the hosted services, and the VM.
- `Views/HubRegistry.cs` — Monitor hub gains a third section: `new("Power Insights", typeof(PowerInsightsPage))`.
- `MainWindow.PageMap` — add `"PowerInsights"` so the assistant can `navigate_to_page`.
- `CLAUDE.md` — add one-line note about the new ETW + TraceProcessor dependency.

### Schema (SQLite)
```sql
CREATE TABLE power_snapshots (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  ts INTEGER NOT NULL,                -- unix epoch ms
  context_id INTEGER NOT NULL,        -- FK contexts(id)
  pid INTEGER NOT NULL,
  process_name TEXT NOT NULL,
  energy_joules REAL NOT NULL,
  avg_power_w REAL NOT NULL,
  window_sec INTEGER NOT NULL
);
CREATE INDEX idx_power_snapshots_ts ON power_snapshots(ts);
CREATE INDEX idx_power_snapshots_ctx_proc ON power_snapshots(context_id, process_name);

CREATE TABLE power_baselines (
  context_id INTEGER NOT NULL,
  process_name TEXT NOT NULL,
  count INTEGER NOT NULL,             -- Welford n
  mean_w REAL NOT NULL,               -- Welford μ
  m2 REAL NOT NULL,                   -- Welford M₂ (sum of squared diffs)
  ewma_w REAL NOT NULL,               -- exponential moving average
  last_updated INTEGER NOT NULL,
  PRIMARY KEY (context_id, process_name)
);

CREATE TABLE power_drift_events (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  ts INTEGER NOT NULL,
  context_id INTEGER NOT NULL,
  process_name TEXT NOT NULL,
  observed_w REAL NOT NULL,
  baseline_w REAL NOT NULL,
  z_score REAL NOT NULL,
  classification TEXT NOT NULL,        -- 'elevated' | 'anomalous'
  suggestion_id INTEGER NULL,          -- FK recommendations(id) if surfaced
  dismissed_at INTEGER NULL,
  resolved_at INTEGER NULL
);
```

### Build/test sequence
1. Add NuGet `Microsoft.Windows.EventTracing.Processing.All` (verify x64-only constraint).
2. Implement `PowerSnapshot` + `IPowerAttributionService` (no UI yet).
3. Unit tests on a captured `.etl` fixture file (golden snapshots).
4. Wire up the hosted service; run for 24 h on the user's machine, eyeball the SQLite output.
5. Build `DriftDetector` + tests against synthetic distributions.
6. Wire `AdaptiveDriftSurfacingService` to emit `Recommendation` rows.
7. UI panel (next section).
8. Verification matrix from §1.
9. Ship behind `PpiEnabled=false` default; flip after one week of stable observation.

## 6. UI / design plan

The Monitor hub today has two sections (Sensors & Inventory, Event Log). Add a third: **Power Insights**.

### Page layout — `PowerInsightsPage.xaml`

```
┌─ HudBackdrop ─────────────────────────────────────────────────────────────────┐
│  HudPageHeader  Icon:⚡   Title: Power Insights                                │
│                 Description: "Per-process power attribution and drift         │
│                 detection. Read-only — never changes your system."            │
│                 Actions:  [ Export CSV ]  [ Reset baselines ]                  │
│                                                                                │
│  ── Vitals row (3 StatTiles, same shape as PerformancePage) ──                 │
│   ┌─ PACKAGE POWER ─┐  ┌─ TOP DRAIN ─────┐  ┌─ ANOMALIES (24H) ──┐            │
│   │ 87 W ▁▂▃▆█▇▅▃▂  │  │ chrome.exe       │  │ 3                  │            │
│   │ live · 30s     │  │ 14.2 W (1.8× ⬆)  │  │ 1 elevated, 2 anom │            │
│   └────────────────┘  └─────────────────┘  └────────────────────┘            │
│                                                                                │
│  ── Section: TOP DRAINERS ───────────────────────────────────────────────      │
│   tk:Segmented:  [Live]  [5 min]  [1 hr]  [24 hr]                              │
│                                                                                │
│   ┌─ HudCard ──────────────────────────────────────────────────────────────┐  │
│   │ Process               Avg W   Energy (Wh)   vs baseline    Drift       │  │
│   │ ───────────────────────────────────────────────────────────────────── │  │
│   │ chrome.exe (×12)      14.2    23.7          1.8× ⬆          ⚠ anom    │  │
│   │   PID 4231, 2 GB                                                       │  │
│   │ wmplayer.exe          11.5    19.2          1.1×            ✓         │  │
│   │ steam.exe (×4)         8.3    13.8          0.9×            ✓         │  │
│   │ MsMpEng.exe            6.1    10.2          —  (excluded)              │  │
│   │ … 14 more · expand →                                                   │  │
│   └────────────────────────────────────────────────────────────────────────┘  │
│                                                                                │
│  ── Section: DRIFT HISTORY ─────────────────────────────────────────────       │
│   ┌─ HudCard ──────────────────────────────────────────────────────────────┐  │
│   │ ⚠  chrome.exe  · Idle context · 4 min ago                              │  │
│   │   Observed 14.2 W vs 5.1 W baseline · z=4.2                            │  │
│   │   [ View in Recommendations ]  [ Dismiss ]  [ Add to exclusions ]      │  │
│   │                                                                        │  │
│   │ ●  language-server-1.exe · Work context · 22 min ago                   │  │
│   │   Sustained 15.0 W vs 2.4 W baseline · z=6.8                          │  │
│   │   [ View in Recommendations ]  [ Dismiss ]                             │  │
│   └────────────────────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────────────────┘
```

### Visual choices, mapped to the calm-token vocabulary
- Vitals row uses existing `hud:StatTile` controls — no new chrome to design.
- Top-drainer ListView uses `hud:HudCard` with `HudSurfaceAltBrush` + `HudHairlineBrush` separators.
- Drift badges use semantic brushes: ✓ = `SuccessBrush`, ● elevated = `WarningBrush`, ⚠ anomalous = `DangerBrush`. The accent stays earned.
- "Live / 5 min / 1 hr / 24 hr" uses `tk:Segmented` — same control already used in 5 other places.
- The mini bar in the package-power tile is a `HudMicroSparkline` (subclass of existing `HudStatusBar` if needed).
- No motion beyond the existing `HoverLift` on StatTiles; no glow on baseline rows. The earned-accent rule means glow only on the **drift card** (red border bloom for anomalous) and the **top-drain caption** when delta ≥ 2×.

### Settings panel (in existing `SettingsPage` → AI Assistant section)
```
┌─ Power anomaly suggestions ──────────────────────────────────────┐
│ [⬤] Enable                                                       │
│  Process exclusions (regex, one per line)                        │
│  ┌──────────────────────────────────────────────────────────────┐│
│  │ MsMpEng                                                      ││
│  │ TrustedInstaller                                             ││
│  │ SearchHost                                                   ││
│  │ Optimizer\.WinUI                                             ││
│  └──────────────────────────────────────────────────────────────┘│
│  Drift threshold (z)              [3.5     ]                     │
│  Baseline half-life (hours)       [72      ]                     │
│  [ Reset all baselines ]                                         │
└──────────────────────────────────────────────────────────────────┘
```

Implemented as `SettingsCard` instances — consistent with the rest of Settings.

### Recommendations integration
- New `RecommendationKind = "PowerDrift"`.
- Each surfaced recommendation has body text: *"chrome.exe is consuming 14.2 W in the Idle context vs. a 5.1 W baseline (z = 4.2). 47 background tabs detected; consider The Great Suspender or a tab-discarder."*
- Recommendations are **suggestions only** — no "Apply" button. They link to the Drift History card.

## 7. Other ideas worth chaining

These come up naturally once PPI ships — flag them now so the order is clear:

1. **Per-app energy budgets (later, opt-in).** Once baselines are stable, the user could *opt in* to a budget per app per context — "Discord should never exceed 5 W under Gaming" — surfaced as a notification when crossed. Still no auto-action; just visibility.

2. **Power → cost translation.** Multiply average wattage by local kWh price (user-entered) to show "Chrome cost you $0.43 this month under Idle." Concrete framing of why drift matters.

3. **Sleep-state attribution.** Extend ETW subscription to `Microsoft-Windows-Kernel-Power` modern-standby events. Detect "this device woke 47 times last night because of `wlanext`" — explains why morning battery is low on a laptop.

4. **Gaming-overlay accounting.** Use ETW + process-tree to specifically attribute Discord-overlay, RTSS, MSI Afterburner, NVIDIA Overlay power separately from the game process. The "overhead tax" view game-context users would love.

5. **Cross-link with the existing CPU & Power page.** Optimize-hub Affinity dialog could pre-select cores to avoid for high-drain background processes — the existing affinity UI gets a `Suggested affinity (based on drift)` button.

6. **Plugin SDK hook.** A `IPowerInsightConsumer` interface in the plugin SDK lets community plugins consume the power stream — e.g. a Plex-specific plugin that knows "transcoding more than 110 W on this CPU = h264 fallback engaged."

7. **One-shot diagnostic mode.** From the assistant: "diagnose my power" → ADS kicks into 5-min observation, returns a written report. Maps the assistant's reactive model onto PPI directly.
