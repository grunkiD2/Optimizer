# Optimizer — Vision & durable constraints

**Scope: private, single-user, local-only.** No cloud, no multi-platform, no
enterprise/fleet features. The user is the only user.

## Why it exists

The user runs the same PC across very different workloads and wants every aspect
of the machine tuned to the current job — without manual tweaking. They want
root-cause analysis when something fails, not symptom-patching. They want the
system to catch things they'd miss.

## Daily contexts (switched multiple times per day)

1. **Plex Server** — streaming workload, network stability priority.
2. **Work** — balanced performance, coding/IDE optimization.
3. **Gaming** — FPS/latency, GPU/CPU priority.
4. Other ad-hoc — not pre-modeled.

## Working principles (durable design constraints)

Any new feature must respect these:

- **AI is for analysis, not chat.** No Q&A interface. Use it for root-cause
  analysis, pattern detection, recommendations.
- **Every automation feature is toggleable.** The user retains the kill switch.
- **Reversible changes only.** Full undo history; rollback only auto-fires on
  critical errors.
- **Detailed logs, not summaries.**
- **Learn what works but don't get stuck in patterns** — pick the best route if
  a better one becomes available.

The original must-have checklist is implemented — see
[LEARNING-ENGINE.md](LEARNING-ENGINE.md) (PR #2) and
[REDESIGN-IA.md](REDESIGN-IA.md) (PR #3) for what shipped.

## Federation addendum (2026-06-12)

The "every aspect tuned to the current job" goal is shared with the machine's
live Fancontrol system (autonomous noise-first fan/profile control). Optimizer
is the observing/diagnostics shell in that federation — ownership rules in
[MACHINE-OWNERSHIP.md](MACHINE-OWNERSHIP.md), phased plan in
`L:\Users\Fancontrol\docs\optimizer-merge-plan.md`. The single-user/local-only
scope above is also why the PWA/MAUI phone companions were REMOVED outright
(2026-06-12) and why the federation ignores Optimizer.Server and the marketplace.
