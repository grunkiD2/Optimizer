# Optimizer V4-V7 Roadmap

> Vision: Beyond a single-machine Windows tweaker, become an ecosystem — sharing, intelligence, enterprise, and cross-platform reach.

**Status:** Planning. Tier 1-3 complete. This document plans Tier 4-7.

## Scope Reality

Some Tier 6/7 features (true mobile apps, real cloud infrastructure) require resources beyond a single WinUI 3 codebase. Where that's the case, we scaffold the architecture so future work has a foundation. Honest tradeoffs noted per batch.

---

## Tier 4: Ecosystem & Sharing (V4)

**Theme:** Move from solo tweaker to community-aware platform. Cloud sync, sharing, marketplace.

### Batch 15: Cloud sync foundation
- Local-file-based sync via OneDrive folder detection (zero-server)
- Manual export/import to any cloud
- Conflict resolution: timestamp-based
- Sync scope: profiles, snapshots, history, settings
- New service: `ICloudSyncService`

### Batch 16: Profile marketplace
- Browse curated profile library (bundled JSON catalog)
- Submit profile dialog (generates shareable JSON)
- Local "favorites" list
- Search + filter by category
- New page: **Marketplace**

### Batch 17: Profile sharing
- Generate shareable URL (base64-encoded JSON in fragment)
- QR code generator for mobile pickup
- Import from URL/clipboard
- One-click "Apply Shared Profile" workflow

### Batch 18: Ratings + community telemetry (opt-in)
- Local 5-star rating per profile/preset
- Optional anonymous usage tracking ("which presets are most applied")
- Compatible with future server backend

---

## Tier 5: AI & Intelligence (V5)

**Theme:** Move from rule-based recommendations to learned, adaptive intelligence.

### Batch 19: Pattern detection engine
- Workload classifier (Gaming / Productivity / Idle / Heavy IO)
- Time-series analysis of metrics
- Daily/weekly usage profile per user
- New service: `IPatternDetectionService`

### Batch 20: ML.NET-backed recommendations
- ML.NET pipeline trained on local telemetry
- Replace heuristic rules in `RecommendationsService` with model predictions
- Retrains weekly from local history
- Confidence scores on each recommendation

### Batch 21: Anomaly detection
- Statistical baselines for CPU/Memory/Disk/Network
- 3-sigma deviation alerts
- Memory leak detection (gradual growth pattern)
- Background process anomaly (sudden CPU spike)
- New service: `IAnomalyDetectionService`

### Batch 22: Natural language assistant
- Text input box on dashboard
- Intent classifier (apply profile / show metric / run diagnostic / etc.)
- Heuristic-based for now; design ready for LLM swap
- New page: **Assistant**

### Batch 23: Auto-tuning rules engine
- Define IF/THEN rules ("if temperature >75°C, apply Quiet preset")
- Visual rule builder
- Execute in background
- Extends `ProfileAutomationService` to general-purpose rules

### Batch 24: Predictive maintenance
- SMART trend analysis (predict drive failure weeks ahead)
- Disk space exhaustion projection
- "Time until X" forecasts
- New view in Diagnostics page

---

## Tier 6: Enterprise / Fleet (V6)

**Theme:** Manage many machines, not just one. Compliance, deployment, reporting.

### Batch 25: Fleet view
- Import machine roster (CSV)
- Per-machine status cards
- Aggregate health across fleet
- Local-file-based for now (no agent communication)
- New page: **Fleet**

### Batch 26: Config templates / group policies
- Save current state as a "template"
- Apply template across fleet
- Versioning
- Export as PowerShell DSC script
- New page: **Templates**

### Batch 27: Centralized reporting
- Aggregate reports across fleet
- HTML dashboard export
- Compliance status summary
- Extends existing Reports page

### Batch 28: Compliance frameworks
- Checklists: HIPAA, SOC 2, NIST 800-171, CIS Benchmark
- Each control mapped to optimization/setting check
- Pass/fail dashboard
- Audit log export
- New page: **Compliance**

### Batch 29: Remote deployment scaffolding
- Generate PowerShell DSC config from current profile
- Generate Intune-compatible script package
- WinGet config YAML export
- Documentation for SCCM deployment

---

## Tier 7: Cross-Platform & APIs (V7)

**Theme:** Beyond the Windows window. Programmable, remote-accessible.

### Batch 30: REST API server
- Embedded ASP.NET Core minimal API
- Endpoints: GET /metrics, GET/POST /profiles, POST /apply/{id}
- Bearer token auth (token in settings)
- OpenAPI spec auto-generated
- Toggleable in Settings

### Batch 31: CLI tool
- New project: `Optimizer.Cli`
- `optimizer apply <profile>`, `optimizer status`, `optimizer scan`
- Shares core services library via shared project
- Scriptable for automation/CI

### Batch 32: Mobile companion API
- Define mobile-friendly subset of REST API
- WebSocket for real-time metrics
- Push notification integration spec
- iOS/Android app stubs (Swift/Kotlin folders with README) — placeholder, not buildable from .NET

### Batch 33: Web dashboard
- Static HTML+JS dashboard
- Served from embedded API
- Real-time metrics via WebSocket
- Profile management
- Mobile-responsive

---

## Total Scope

- **19 batches** across 4 tiers
- **~10-15 new pages/projects**
- **~25-30 new services**
- **Estimated:** ~80-120 dev-days at current pace

## Execution Order

Tiers can be implemented somewhat in parallel, but dependencies suggest:
1. **Tier 7 first (API + CLI)** — provides foundation other tiers can use
2. **Tier 4 (ecosystem)** — builds on file/local-storage patterns we have
3. **Tier 5 (intelligence)** — depends on history data we've collected
4. **Tier 6 (enterprise)** — uses templates from earlier tiers

I'll execute in this order. Each batch ends with build verification and commit. Final cycle = build/test/run validation.
