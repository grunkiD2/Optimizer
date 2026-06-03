# V8 Implementation Plan — Themes 1, 2, 3, 4, 5, 7, 8, 9

> Concrete, dependency-ordered build plan for the selected V8 themes.
> Themes 6 (True Multi-Platform) and 10 (Enterprise) are deferred to "the rest of the V8 plan".

**Status:** Plan — awaiting approval before implementation begins.

---

## Selected themes

From `ROADMAP-V8-PLUS.md`, the themes in scope:

| # | Theme | Status |
|---|-------|--------|
| 1 | Server-Side Ecosystem | ✅ **DONE** (Phase A: auth, sync, marketplace) |
| 2 | User-Defined Optimizations (Plugins) | Planned |
| 3 | Voice + Conversational Assistant | Planned |
| 4 | Gamification + Engagement | Planned |
| 5 | Deeper Hardware Control | Planned |
| 7 | Privacy-First AI | Planned |
| 8 | Developer Platform | Planned |
| 9 | Accessibility + Internationalization | Planned |

Deferred (the "rest" we do afterward): **6 (Multi-Platform)**, **10 (Enterprise)**.

---

## Why this order (not the listed order)

The numbers you gave (1-2-3-4-5-7-8-9) are the *selection*, not the build order. The correct build order is driven by **dependencies** and **value-of-information** — build the things others rest on first, and do cross-cutting polish last so we don't redo it.

### Dependency graph

```
Theme 1 (Server) ──────┬──> Theme 2 (Plugins: marketplace distribution)
   [DONE]              ├──> Theme 8 (Dev Platform: public API)
                       ├──> Theme 4 (Gamification: leaderboards/sync)
                       └──> Theme 7 (Privacy AI: federated learning)

Theme 2 (Plugins) ─────────> Theme 8 (API exposes plugin management)
                             (build plugins first so the API is complete in one pass)

Theme 8 (Dev Platform) ─┬──> Theme 3 (Voice: command surface + event bus)
   builds event bus ────└──> Theme 4 (Gamification: achievement triggers)

Theme 7 (Privacy AI) ──────> Theme 3 (Voice: shares on-device ML runtime)

Theme 5 (Hardware) ──── independent (builds on existing Tuning/sensors)

Theme 9 (A11y + i18n) ── cross-cutting; LAST, over the now-stable UI
```

### Resulting build order

**1 → 2 → 8 → 5 → 7 → 3 → 4 → 9**

| Order | Theme | Why here |
|-------|-------|----------|
| ✅ | 1 — Server | Done. Foundation for 2, 4, 7, 8. |
| **B** | 2 — Plugins | Top strategic value. Extends the existing `IOptimizationHandler` pattern (Phase 6a) + the marketplace (Phase A). Plugins are the distribution-ready next step. |
| **C** | 8 — Dev Platform | Exposes plugins + everything else through a public API. Built *after* plugins so the API surface is complete in one pass. Ships the **internal event bus** that themes 3 and 4 need. |
| **D** | 5 — Hardware | Fully independent. Builds on `TuningService` + `SensorService`. A self-contained power-user depth chunk that blocks nothing. |
| **E** | 7 — Privacy AI | Upgrades the ML.NET foundation (`IntelligenceService`). Federated learning uses the server (Theme 1). Produces better data that gamification (4) and voice (3) consume. |
| **F** | 3 — Voice/Assistant | Heaviest theme. Reuses Theme 7's on-device ML runtime and Theme 8's command/event surface. Built once those foundations are solid. |
| **G** | 4 — Gamification | Only meaningful once there's lots to gamify (plugins applied, hardware tuned, AI insights, voice usage). Uses the event bus (8) + server (1). |
| **H** | 9 — A11y + i18n | LAST. Cross-cutting — touches every page. Done after all new pages from 2/3/4/5/7/8 exist, so we localize and a11y-audit the *stable* UI once instead of repeatedly. |

---

## Phase B — Theme 2: User-Defined Optimizations (Plugins)

**Goal:** Let users (and the community) define optimizations declaratively without recompiling the app.

**Builds on:** `IOptimizationHandler` pattern (Phase 6a), `IUndoService`, marketplace (Phase A).

### B1 — Manifest format + parser
- `OptimizationManifest` model (id, name, description, category, requires_admin, reversible, declared changes)
- YAML + JSON support (YamlDotNet)
- JSON-schema validation, friendly error messages
- `Models/Plugins/`, `Services/Plugins/IManifestParser`
- **Tests:** parse valid/invalid manifests, schema violations, version compatibility

### B2 — Reversibility framework
- Declarative change types: `registry`, `service`, `file`, `powercfg`, `scheduled-task`
- Each change declares `apply` + `revert` (or auto-captures prior state for undo)
- `IDeclarativeChangeExecutor` that applies a change and registers undo with `IUndoService`
- Permission/allow-list model (which registry hives, which paths a manifest may touch)
- **Tests:** apply→capture→undo round-trip per change type; permission rejection

### B3 — Manifest handler + sandbox
- `ManifestOptimizationHandler : IOptimizationHandler` — executes a manifest's declared changes
- Registers dynamically into the existing handler dictionary in `WindowsOptimizerService`
- Sandbox: manifests are declarations only (no arbitrary code); strict path allow-list
- Plugin loader scans `%LocalAppData%\Optimizer\plugins\*.yaml`
- **Tests:** manifest → handler execution, isApplied detection, undo

### B4 — Plugin marketplace + signing
- Server: `PluginListing` entity (distinct from profile listings), submit/browse/approve
- Signature verification (Optimizer team Ed25519 key; "verified" badge)
- Client: Plugins page or Marketplace tab — install/enable/disable/remove plugins
- **Tests:** server submit→approve→browse; client install→enable→apply

**Phase B output:** community can author + share optimizations as YAML; ~30-40 new tests.

---

## Phase C — Theme 8: Developer Platform

**Goal:** Expose Optimizer's capabilities to third parties + automation. Ships the **internal event bus** used by later themes.

**Builds on:** `Optimizer.Server` (Phase A), local REST API (`ApiHostService`), plugins (Phase B).

### C1 — Public API hardening
- OAuth2 client-credentials + long-lived API keys on `Optimizer.Server`
- Scopes (`metrics:read`, `profiles:write`, `plugins:manage`, etc.)
- Rate limiting middleware (per-key)
- API key management UI in WinUI Settings
- **Tests:** scope enforcement, rate-limit, key revocation

### C2 — Event bus + webhooks ⭐ (shared infra)
- Internal `IEventBus` — publish/subscribe for domain events (optimization applied, anomaly detected, plugin installed, profile synced, threshold crossed)
- Webhook subscriptions (server-side): register URL + event filter, signed delivery, retry w/ backoff
- This is the foundation Themes 3 (proactive suggestions) and 4 (achievement triggers) consume
- **Tests:** event publish/subscribe, webhook delivery, retry, signature

### C3 — SDK + PowerShell module
- TypeScript + Python clients generated from OpenAPI spec
- `Optimizer` PowerShell module (`Import-Module Optimizer`; `Get-OptimizerStatus`, `Invoke-OptimizerProfile`, etc.)
- **Tests:** PowerShell module smoke tests; SDK generation verification

### C4 — CLI cloud commands + automation recipes
- Extend `Optimizer.Cli`: `optimizer login`, `optimizer sync`, `optimizer marketplace`, `optimizer plugin`
- IFTTT / Zapier / Power Automate recipe templates (docs + webhook examples)
- **Tests:** CLI command parsing + API integration

**Phase C output:** programmable platform; event bus live; ~25-30 new tests.

---

## Phase D — Theme 5: Deeper Hardware Control

**Goal:** Real GPU OC, CPU undervolting, fan curves — the V3 deferrals.

**Builds on:** `TuningService`, `SensorService`, `StressTestService`. Fully independent of server.

### D1 — NVIDIA control (NVAPI)
- P/Invoke wrapper for NVAPI: read clocks/power/temp/fan, write core/memory offsets, power limit, temp limit
- Auto-revert watchdog reuses `StressTestService` thermal watchdog pattern
- **Tests:** NVAPI availability detection, safe-default clamps (mock the native layer)

### D2 — AMD control (ADL)
- ADL equivalent of D1 for AMD GPUs
- Vendor detection picks NVAPI vs ADL vs read-only fallback
- **Tests:** vendor routing, clamps

### D3 — CPU undervolting + affinity + power limits
- Undervolt via MSR (laptop battery wins) — with hard safety rails + disclaimer gate
- Per-core affinity manager (pin legacy apps)
- PL1/PL2 power-limit display + adjust where supported
- **Tests:** clamp logic, affinity mask math, MSR availability detection

### D4 — Fan curves + device control
- Multi-point fan curve editor (vendor APIs)
- PCIe/USB device enable-disable (Device Manager via SetupAPI/PnP)
- Memory timing display (DDR5 learned timings, read-only)
- **Tests:** curve interpolation, device enumeration

**Phase D output:** real hardware tuning; ~20 new tests. **Risk:** vendor SDK licensing — NVAPI/ADL are click-through-available; document the boundary.

---

## Phase E — Theme 7: Privacy-First AI

**Goal:** Smarter, fully-local intelligence. Federated improvement without raw-data exfiltration.

**Builds on:** `IntelligenceService` (ML.NET 4.0), `RecommendationsService`, server (Phase A).

### E1 — Enhanced anomaly detection
- Replace 3-sigma heuristic with ML.NET SSA (Singular Spectrum Analysis) time-series detector
- Per-metric trained baselines (CPU/mem/disk/net), leak-vs-spike classification
- **Tests:** SSA detects injected anomalies, ignores normal variance

### E2 — Predictive maintenance
- SMART trend models — "drive shows wear patterns similar to drives that failed within N days"
- Disk-space exhaustion forecast ("full in ~12 days at current rate")
- "Time until X" forecasts surfaced in Diagnostics/Recommendations
- **Tests:** trend extrapolation, forecast accuracy on synthetic data

### E3 — Per-user custom models + differential privacy
- On-device personalized recommendation model improvements
- Differential-privacy noise layer for any opt-in telemetry
- **Tests:** DP noise bounds, model isolation per machine

### E4 — Federated learning scaffold
- Server aggregates model gradient updates (not raw data); opt-in
- Client computes local gradient, uploads deltas; server averages into global model; clients pull improved model
- **Tests:** gradient aggregation correctness, opt-out respected

**Phase E output:** credible "private AI for your PC"; ~25 new tests.

---

## Phase F — Theme 3: Voice + Conversational Assistant

> **Status update (2026-06-03):** Shipped a **cloud (Claude API) intent path** instead of starting
> with the on-device LLM. The assistant is **opt-in, off by default, bring-your-own-key** (the
> Anthropic key is stored encrypted via Windows DPAPI, never in the settings file). It maps natural
> language to the app's existing actions via Claude **tool-use** over a new `ICommandRegistry`;
> read-only queries answer directly, while system-changing actions are **proposed → confirmed →
> executed** through the existing `IUndoService`. It lives in a new **persistent console dock**
> (Activity tab = live `IEventBus` stream; Assistant tab = chat) that toggles with `Ctrl+\`` and can
> **pop out** into its own window, plus a `Ctrl+K` omnibox. The only data leaving the machine is the
> user's messages plus a short system-metrics summary, sent to Anthropic under the user's own key.
> The on-device Phi-3/ONNX runtime below remains a **future, fully-offline option**. Spec:
> `docs/superpowers/specs/2026-06-03-claude-intent-assistant-design.md`;
> plan: `docs/superpowers/plans/2026-06-03-claude-intent-assistant.md`.

**Goal:** Talk to your PC. On-device LLM, no cloud, no telemetry.

**Builds on:** Theme 7 ML runtime, Theme 8 command surface + event bus, existing RelayCommands.

### F1 — Local LLM runtime
- ONNX Runtime + Phi-3 Mini (3.8B, <4GB) — model download/management, GPU/CPU inference
- `ILocalLlmService` with streaming token output
- Graceful fallback if hardware can't run it (heuristic intent matching)
- **Tests:** model load, inference smoke, fallback path

### F2 — Intent classification + command routing
- NL → action mapping over existing commands ("apply gaming profile", "run a scan", "what's eating my CPU")
- Reuses Theme 8's command registry
- **Tests:** intent→command mapping accuracy on a fixture set

### F3 — Conversational UI
- New **Assistant** page — chat panel, history, streaming responses, action chips
- **Tests:** message flow, action execution from chat

### F4 — Voice input + proactive suggestions
- Windows speech-to-text for the command bar
- Context-aware prompts via Theme 8 event bus ("OBS launched — apply Streaming profile?")
- **Tests:** event→suggestion mapping, debounce/dismissal

**Phase F output:** conversational + voice control; ~20 new tests. **Heaviest theme.**

---

## Phase G — Theme 4: Gamification + Engagement

**Goal:** Make maintenance habitual without dark patterns. Opt-in, meaningful, never anxiety-inducing.

**Builds on:** server (Phase A), event bus (Phase C), activity from all prior themes.

### G1 — Achievement engine
- Definitions ("First Optimization", "Power User", "Privacy Pro", "Plugin Author", "Tuner")
- Event-bus-driven unlock tracking, local + server sync
- Achievements UI surface
- **Tests:** unlock triggers, idempotency, sync

### G2 — Health streaks + weekly report card
- Consecutive-healthy-days streak
- Shareable weekly summary card ("Your PC improved 12% this week")
- **Tests:** streak calculation, report generation

### G3 — Leaderboards (opt-in)
- Server-backed: boot time, uptime stability, fewest crashes
- Strictly opt-in, anonymizable handles
- **Tests:** ranking, opt-out exclusion

### G4 — Discovery challenges + contribution badges
- "Try all 5 power presets" style challenges
- Marketplace contribution badges (N profiles, M downloads)
- **Tests:** challenge progress, badge award

**Phase G output:** engagement layer; ~20 new tests. **Guardrail:** opt-in only, no manipulative notifications.

---

## Phase H — Theme 9: Accessibility + Internationalization

**Goal:** Usable by everyone, in their language. Done LAST so we polish the complete, stable UI once.

**Builds on:** localization scaffold (Batch 14), every page that now exists.

### H1 — Screen reader pass
- `AutomationProperties.Name`/`HelpText` across all ~25 pages
- Narrator live-region announcements for status changes
- **Tests:** automation peer presence (where testable)

### H2 — Localization completion
- Fill `.resw` for 8 priority languages: es, de, fr, it, ja, ko, zh-CN, pt-BR
- RTL wiring (FlowDirection) for future ar/he
- **Tests:** resource-key coverage (no missing keys per language)

### H3 — Visual accessibility
- High-contrast theme polish, reduced-motion option, text scaling to 200%+, color-blind-safe chart palettes
- **Tests:** palette contrast ratios

### H4 — Keyboard-only navigation
- Full app operable without mouse, including tray menu + dialogs
- Focus management + visible focus rings audit
- **Tests:** tab-order coverage where testable

**Phase H output:** inclusive, localized app; ~15 new tests. Translation cost is mostly external (translators), not code.

---

## Shared infrastructure (called out once)

- **Internal event bus** (`IEventBus`) — built in Phase C2, consumed by F4 (proactive voice) and G1 (achievements). Build once, reuse.
- **On-device ML runtime** — Theme 7 (E) establishes ONNX patterns that Theme 3 (F1) reuses for the LLM.
- **Reversibility framework** — Theme 2 (B2) declarative undo could later inform enterprise audit (Theme 10).

---

## Rough sizing

| Phase | Theme | Batches | Est. tests added | Relative effort |
|-------|-------|---------|------------------|-----------------|
| B | 2 Plugins | 4 | ~35 | Large (security) |
| C | 8 Dev Platform | 4 | ~28 | Medium |
| D | 5 Hardware | 4 | ~20 | Medium (vendor SDKs) |
| E | 7 Privacy AI | 4 | ~25 | Medium |
| F | 3 Voice/Assistant | 4 | ~20 | Large (LLM) |
| G | 4 Gamification | 4 | ~20 | Small-Medium |
| H | 9 A11y + i18n | 4 | ~15 | Medium (cross-cutting) |
| | **Total** | **28** | **~163** | — |

Test trajectory: 293 today → ~456 at completion.

---

## Execution method

Same proven flow as V1-V8.A:
1. One subagent per batch, dispatched in dependency order
2. Build + test verification after each batch
3. Commit + push per batch
4. Build/test/run validation checkpoint at the end of each phase
5. Tech-debt audit after every ~2 phases (the pattern that's kept the codebase clean)

Each phase is independently shippable — we can pause between any two phases and the product is in a coherent state.

---

## Decision checkpoints (don't need answers now, but will arise)

- **Theme 2/B4:** signing key custody — who holds the Optimizer signing key for "verified" plugins?
- **Theme 5/D:** pursue NVIDIA/AMD partnership, or stay on click-through NVAPI/ADL only?
- **Theme 3/F1:** ship the ~2GB Phi-3 model with the app, or download on first use?
- **Theme 4/G3:** leaderboards require a moderation/anti-cheat story — acceptable scope?
- **Theme 7/E4:** federated learning is the highest-complexity item — keep, or stop Theme 7 at E3?

---

## Recommendation

Approve the order **2 → 8 → 5 → 7 → 3 → 4 → 9** and start with **Phase B (Plugins)**. It's the highest-leverage next step: it changes Optimizer from "an app we shipped" into "a platform users extend," and everything from Phase A (marketplace, sync, accounts) is already in place to distribute community plugins.
