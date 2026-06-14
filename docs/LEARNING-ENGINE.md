# Optimizer Learning Engine

The learning engine makes Optimizer adapt to how you actually use the PC: it records what
the assistant and the user do, in what **context** (Gaming / Work / Plex / Unknown), learns
what works, and can optionally automate it. Everything is **local-only** (one SQLite file)
and **reversible**.

> Companion to the 8-phase plan in `C:\Users\sjpo1\.claude\plans\calm-whistling-wirth.md`.
> This doc is the source of truth for *what shipped*.

---

## 1. Storage

A single SQLite database at `%LocalAppData%\Optimizer\optimizer.db`, created/owned by
`Services/Data/DatabaseService.cs`.

- **Backup = copy one file.** `BackupToFileAsync` / `RestoreFromFileAsync` use SQLite's online
  backup API.
- **Access layer:** `ExecuteQueryAsync` returns `List<DbRow>` — use the typed getters
  (`GetString`, `GetInt`, `GetLong`, `GetDouble`, `GetBool`, `GetDateTime[OrNull]`). Don't
  reintroduce `Convert.ToX(row["col"])` boilerplate.
- **Transactions:** `RunInTransactionAsync(batch => …)` for multi-statement work. Any
  DELETE-all-then-repopulate rebuild **must** use it so a crash can't leave a partial table.
- **Migrations:** `DatabaseSchema.Tables` are `CREATE TABLE IF NOT EXISTS` (idempotent);
  `DatabaseSchema.Migrations` are idempotent `ALTER TABLE ADD COLUMN` statements (duplicate-column
  errors are swallowed). Bump `CurrentVersion` and add a migration when the schema changes.

### Table map

| Table | Written by | Purpose |
|-------|-----------|---------|
| `Settings`, `Metadata` | infra | key/value + schema version |
| `Contexts` | seed | the four contexts |
| `AssistantActions` | `AssistantActionLogger` | every tool call: success, duration, context |
| `AssistantSessions`, `SessionEvents` | `SessionPersistence` | conversation transcripts |
| `ToolContextMetrics` | `ActionAnalyticsService` | materialized success-rate rollup (rebuilt) |
| `LearnedPatterns` | `PatternExtractionService` | mined tool n-grams (rebuilt) |
| `AssistantFeedback` | `AssistantFeedbackService` | 👍/👎 per tool |
| `ProfileContextAssociations`, `ProfileApplications` | `ProfileContextService` | "did the profile stick?" |
| `SuggestedRules` | `RuleSuggestionService` | proposed automation rules |
| `UndoStack` | `ChangeSetService` | before/after change snapshots |
| `MetricBaselines`, `AnomalySuppressions`, `AnomalyAlerts` | `AnomalyDetector` | learned baselines + alerts |
| `MaintenanceAlerts` | `PredictiveAlertService` | deduped disk forecasts |
| `OptimizationOutcomes` | `AutoApplyPolicy` | per-context apply outcomes (auto-apply gate) |
| `ContextSnapshots` | `ContextStateManager` | per-context registry baseline |
| `ScheduledTasks`, `ScheduleExecutions` | `ScheduledOptimizationService` | unattended runs |
| `ProfileTimeline` | `ProfileTransitionWatcher` | fgwatch profile-active intervals (one row per profile switch; `EndTs` NULL = still active) |
| `ProfileOutcomes` | `ProfileOutcomesService` | per-interval rollups (coolant-p95 from `FancontrolTelemetry` + fps-1%-low from PresentMon) for "sidst vs forrige" |
| `TrendHistory`, `RecommendationPreferences` | (legacy, mirrored) | |

---

## 2. How decisions are made — statistical, **not** Claude

> **Important divergence from the approved plan.** The plan said "Claude API as the
> decision-making model" for recommendations / anomaly detection / pattern suggestion. The
> shipped implementation is **purely statistical / rules-based** and makes **no Claude calls**:
>
> - Anomaly detection = Welford online mean/variance + a 2σ threshold (`WelfordAccumulator`,
>   `AnomalyDetector`).
> - Pattern mining = contiguous-session n-gram frequency (`PatternExtractionService`).
> - Rule suggestion = time-of-day bucketing (`RuleSuggestionService`).
> - Recommendation ranking = severity + learned tool success + feedback (`RecommendationRanker`).
>
> Claude is used **only** as the interactive assistant (`Services/Assistant/*`) that calls tools
> on the user's request. It is *not* the autonomous decision-maker. This is deliberate (cheap,
> offline, transparent). If/when we want Claude-driven recommendations, that's a real feature to
> design — not a stub to fill.

Context detection (`ContextDetectionService`) is processes + time-of-day. Active-profile
detection is a noted future enhancement, intentionally not wired in.

---

## 3. Automation safety model (opt-in, confirm-on-first)

All automation is **off by default** and gated. See `AppSettings`:

- `AutomationPaused` — master kill switch (overrides everything).
- `AutoContextSwitchEnabled` + `AutoContextSwitchConfidence` — `ContextAutomationService` only
  auto-applies the best-known profile for a newly-detected context when
  `successRate × min(1, applyCount/5) ≥ confidence`.
- `AutoApplyEnabled` + `AutoApplySuccessThreshold` + `AutoApplyExcluded` — `AutoApplyPolicy`
  enforces **confirm-on-first-occurrence**: an optimization may only auto-apply in a context
  after ≥ N prior **successes there and zero failures**, isn't excluded, and automation isn't paused.

Every automated change is reversible (`ChangeSetService` snapshots; `ContextStateManager` keeps
per-context baselines from bleeding across contexts).

---

## 4. Background work

Started in `App.OnLaunched` after the window settles; all bounded by
`IHostApplicationLifetime.ApplicationStopping` (they stop cleanly on exit):

- ~1 min after launch: `RecalculateMetricsAsync` + `ExtractPatternsAsync`.
- every 10 min: `ProfileContextService.ResolvePendingAsync` + `RuleSuggestionService.GenerateSuggestionsAsync`.
- every 2 min: sample cpu/memory into `AnomalyDetector`; ~hourly `PredictiveAlertService.EvaluateAsync`.
- `ScheduledOptimizationService` (hosted) evaluates due tasks every minute.

The **Learning** page (`Views/LearningPage.xaml`) surfaces all of this and exposes accept/reject
on suggestions and acknowledge on alerts; "Export Report" writes a markdown summary.

---

## 5. Gotchas (read before debugging)

- **DPI manifest is load-bearing.** `app.manifest` declares `dpiAwareness = PerMonitorV2`. Without
  it, on a **mixed-DPI multi-monitor** setup WinUI mis-maps pointer coordinates and the mouse wheel
  scrolls an element offset ~1.5× from the cursor (`RasterizationScale` wrongly reports 1.0). Do
  **not** remove that manifest block.
- **Don't resize the WinUI window with raw Win32 `SetWindowPos`** in tooling — WinUI won't sync its
  pointer transform and you'll reproduce a phantom offset that isn't a real bug.
- **WinUI 3 wheel input is "lifted"** — it never arrives as `WM_MOUSEWHEEL`, and a focused
  ScrollViewer consumes `PointerWheelChanged` before it bubbles. Routed handlers and `WM_MOUSEWHEEL`
  subclassing both fail; don't try to intercept the wheel. (History: there is no wheel router any
  more — the manifest fix made native scrolling correct.)
- **Build/run the non-RID output**: `bin\Debug\net10.0-windows10.0.22621.0\Optimizer.WinUI.exe`. A
  stale `win-x64\` RID subfolder can shadow it and mislead testing.
- **The test project has `ImplicitUsings` disabled** — add explicit `using` lines.

---

## 6. Testing

- DB-backed services are tested against a real temp-file database via `DbTestBase`
  (`new DatabaseService(tempPath)` test constructor + pooled-connection cleanup). See
  `LearningEngineIntegrationTests.cs` for the high-stakes coverage (analytics aggregation, the
  profile stick/fail verdict, anomaly thresholds + suppression, suggestion thresholds, alert dedupe,
  pattern mining).
- Pure logic has unit tests: `WelfordAccumulatorTests`, `RecommendationRankerTests`,
  `ScheduledOptimizationTests` (next-run), `RegistryStateSnapshotTests`, `AutoApplyPolicyTests`.
