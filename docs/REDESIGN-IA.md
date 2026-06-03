# Optimizer — Information Architecture Redesign

## Governing design principles (Windows 11 Fluent)

Adopted as the ideology for the whole redesign. When in doubt, these win.

- **Calm** — *emphasize significant items only when necessary.* **Calm base, earned accent**:
  surfaces are quiet glass + a hairline by default; the cyan glow-edge, blooms, and strong accent
  are reserved for what's significant (the hero health ring, the active/selected item, status,
  the primary action). Never glow everything.
- **Effortless** — one obvious primary action per view; clear hierarchy; focus and precision.
- **Personal** — let detected context (Gaming/Work/Plex) gently theme the moment.
- **Familiar** — prefer real Fluent controls (NavigationView, Segmented, SettingsCard, InfoBar)
  over heavy customization. No learning curve.
- **Complete + Coherent** — one design system, one IA (the 25→7 consolidation below).
- **Materials / Geometry / Typography / Motion** — Mica + Acrylic; rounded geometry;
  Segoe UI Variable for text (monospace only for telemetry numerics); motion that is purposeful
  (feedback, way-finding), never decorative.

---



Driven by a full function inventory of all 25 pages. The visual skin landed; the *structure*
is what still reads as dated. This collapses 25 flat nav items into **1 home + 5 hubs + Settings**,
each hub using **segmented sub-nav** so "more of the same" lives in one place instead of N pages.

## What the inventory showed
- **Duplication:** live metrics on Dashboard + Performance + Command Center; sensors on Dashboard +
  Hardware + Tuning; the *same* "optimization card + Apply All/Undo" pattern on Performance, System,
  Network, Storage (and more); health score in 3 places.
- **Thin pages:** Updates, Reports, Templates, Event Logs, History — a few functions each.
- **Job split across pages:** Performance ↔ Tuning (both "make the CPU fast"); Startup ↔ Services
  (both "what runs"); Marketplace ↔ Plugins (both "third-party"); Profiles ↔ Templates (both "saved config").
- **Mis-leveled controls:** power-plan picker is a light control sitting at page level — belongs inside
  "CPU & Power" and applied via a profile, not a headline.

## The attribute lens
Every function tagged on four axes; the clusters fall out naturally:

| Axis | Values |
|------|--------|
| **Intent** | Observe · Act · Tune · Analyze · Automate · Configure · Extend |
| **Domain** | System · CPU/GPU · Memory · Disk · Network · Privacy · Security · Apps · Devices |
| **Cadence** | Daily-glance · Occasional · One-time-setup |
| **Weight** | Heavy (charts/visualizers) · Medium · Light (toggles/lists) |

Grouping by **Intent first, Domain second** gives 5 destinations.

## Consolidation: 25 → 1 home + 5 hubs

| New destination | Segmented sections | Absorbs (old pages) | Intent |
|---|---|---|---|
| **⌂ Command Center** *(home)* | — | **Dashboard** (duplicate live overview) | Observe |
| **◉ Monitor** | Vitals · Sensors · Inventory · Event Log | Hardware, Event Logs, Dashboard's charts | Observe |
| **⚡ Optimize** | **CPU & Power** · Privacy & System · Network · Storage · Startup & Services · Devices | **Performance + Tuning**, System, Network, Storage, **Startup + Services**, Devices | Act / Tune |
| **🧠 Automate** | Profiles · Automation Rules · Recommendations · Learning · History | Profiles **+ Templates**, Recommendations, Learning, History | Automate |
| **🛡 Protect** | Diagnostics · Security · Compliance · Updates | Diagnostics, Security, Compliance, Updates | Analyze |
| **⊞ Extend** | Extensions · Fleet · Reports | **Marketplace + Plugins** (→ Extensions), Fleet, Reports | Extend |
| **⚙ Settings** *(footer)* | — | Settings | Configure |

**Headline merges (your instincts, made concrete):**
- **Performance + Tuning → "CPU & Power"** — power plans, boost/PL limits, process priority/affinity,
  stress tests, perf optimizations: one place. Power-plan picker becomes a control inside it (and a
  profile action), not a page section.
- **Startup + Services → "Startup & Services"** — "what runs at boot / in the background."
- **Marketplace + Plugins → "Extensions"** — one third-party discovery surface.
- **Templates → Profiles**, **Dashboard → Command Center** — kill the duplicates.

Net: nav drops from **25 items** to **7** (home + 5 hubs + settings). Each hub is ONE page with a
`HudPageHeader` + a `Segmented` sub-nav; the six Optimize domains share the optimization-card pattern
instead of being six near-identical pages.

## The new shell (the parts still un-touched)
1. **Slim hub rail** — replace the 220px flat 25-item list with a ~64px **glass icon rail** (home + 5
   hubs + settings), expand-on-hover to labels, a glowing cyan active indicator. Modern, uncluttered,
   and it makes the 5-hub model legible at a glance.
2. **Segmented sub-nav** — at the top of each hub, a `Segmented` control switches sections (e.g.
   Optimize → CPU & Power | Privacy | Network | Storage | Startup & Services | Devices). Density without
   a giant scroll.
3. **Console/activity dock** — finish the glass treatment: `Segmented` tabs (Activity · Assistant ·
   Output), monospace output, a pulsing "live" dot, drag-to-resize (Toolkit Sizers).

## Why this fixes "outdated"
Fewer, purposeful destinations; the same job in one place; depth via segments not scroll; a slim modern
rail and a real console — the structure finally matches the skin.

## Build order (incremental, low-risk)
1. **New shell** — slim hub rail + the segmented hub framework + console dock restyle. (Visible win, sets the frame.)
2. **Optimize hub** — highest merge value: fold Performance+Tuning into "CPU & Power", then the other domains.
3. **Monitor / Automate / Protect / Extend** hubs.
4. Retire the merged/duplicate pages.
