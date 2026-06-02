# Backlog — Make Optimizer Actionable (not just advisory)

> Captured 2026-06-03. Theme: **close the loop between detection/recommendation and action.**
> Right now many features detect a problem or recommend a fix, then punt the actual change to an
> external tool (Windows Settings, vendor tools, BIOS, Control Panel). The goal is to let the
> program *perform* the fix in-app wherever it's safely possible.

## The principle

Every place Optimizer currently says "here's a problem" or "you should do X" should — where safe —
also offer a **"Fix it" / "Do it now"** button that performs the change from within the program.
Detection without action is half a feature.

---

## 1. Event Log debugging (diagnose + act on errors)

**Today:** The Event Logs page (Batch 38/V2) *browses* Application/System/Security logs with filters
and friendly explanations. It's read-only.

**Wanted:**
- When an error/critical event occurs, let the user **diagnose it from inside the program**:
  - Parse the event (source, event ID, bug-check code, faulting module) and explain the likely cause
  - Correlate related events (e.g. a driver crash + the subsequent reboot)
  - Map common error signatures → known fixes (e.g. "this is a corrupt-system-file pattern → run SFC/DISM", "this is a failing-driver pattern → roll back/update the driver")
- **Offer the fix in-app:** a "Diagnose" → "Suggested fix" → "Apply fix" flow.
  - SFC/DISM/CHKDSK already exist as launchers (Batch 36) — wire them as the *action* for the matching error class.
  - Driver-related errors → link to the driver update/rollback action (see item 2 + the existing driver diagnostics).
- Severity triage: surface recurring critical errors prominently (already partly in Diagnostics).

**Existing pieces to build on:** `EventLogService`, `DriverDiagnosticsService`, `SystemRepairService`
(SFC/DISM/CHKDSK), `DiagnosticsService`/plugins.

---

## 2. Actionable recommendations (apply the suggested change in-app)

**Today:** Recommendations, Smart Insights, Diagnostics findings, and predictive maintenance all
*describe* what to do. Some have one-click fixes (optimizations); many just advise (bottleneck,
driver update, "consider X").

**Wanted:** Whenever a suggestion is made — **bottleneck, driver update, cleanup, setting change,
whatever** — there should be a button to *inflict that change* from the program.

- **Bottleneck fixes:** the bottleneck detector finds the offending process / subsystem → offer the
  concrete action (lower its priority, set affinity, kill it, suggest the relevant optimization).
- **Driver updates:** detect outdated/conflicting drivers → actually update/rollback in-app
  (pnputil for install, vendor APIs where available, or fetch + install the inf), not just "your
  driver is old."
- **Any recommendation card:** ensure it carries an executable action, not just text. The
  `Recommendation` model already has a `QuickAction` delegate (Batch 37) — the gap is that many
  recommendations don't populate it. Audit every recommendation source and give each a real action
  where one safely exists.

**Existing pieces:** `RecommendationsService` (has `QuickAction`), `BottleneckDetectorService`,
`DriverDiagnosticsService`, `IOptimizationHandler` pipeline (can apply changes + undo),
`PredictiveMaintenanceService`.

**Constraint:** keep the safety model — confirmation + undo capture for anything that changes the
system (the plugin/optimization pipeline already does this).

---

## 3. In-app update search (don't open Control Panel / Settings)

**Today:** The Updates page (Batch 35/V2) reads `Get-HotFix` history + winget, but "check for Windows
updates" currently launches `ms-settings:windowsupdate-action` (opens the Settings app).

**Wanted:** Perform the update search **inside the program**:
- Windows Update: query + trigger scans via the Windows Update Agent API (WUApiLib / `IUpdateSearcher`)
  rather than opening Settings. Show available updates, let the user select + install in-app.
- Drivers: scan + offer updates in-app (ties into item 2).
- Apps: winget already runs in-process — extend to show/install updates in the grid (mostly done).
- BIOS/firmware: detection only (can't safely auto-flash), but surface "update available" with the
  vendor link — that one stays advisory.

**Existing pieces:** `UpdateService` (winget + Get-HotFix), `IPowerShellRunner`.
**New dependency likely needed:** WUApiLib COM interop (`Microsoft.Update.Session`) for real
Windows Update search/install without leaving the app. Needs admin (already have it). Wrap
defensively — WUA can be slow/locked by policy.

---

## Cross-cutting

- This is fundamentally about turning Optimizer from a **dashboard** into a **control panel that acts**.
- Every "action" must flow through the existing safety rails: confirmation (where it changes the
  system), undo capture (`IUndoService`), history logging, and clear labeling of risk.
- Likely a future phase (call it **"Phase: Actionability"**) — could slot before or after the
  remaining V8 themes (3 Voice, 4 Gamification, 9 A11y). Arguably high-value enough to prioritize.

---

*This is a capture of intent, not a finalized plan. Turn into a proper spec → batches when we pick it up.*
