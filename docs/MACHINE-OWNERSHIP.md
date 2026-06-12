# Machine ownership matrix — Optimizer ⟷ Fancontrol federation

This machine runs a LIVE, autonomous machine-control system (`L:\Users\Fancontrol`):
an always-on fan brain (PowerShell, 5 s loop) driving FanControl.exe/Corsair hardware,
a foreground-app profile switcher (display/HDR/NVIDIA/power/lighting), Process Lasso
owning power-plan switching, and a permanent LibreHardwareMonitor 0.9.6 web server on
`http://localhost:8085/data.json` (PawnIO driver).

Optimizer federates with that system via its contracts. It must NEVER compete with it.
Full plan: `L:\Users\Fancontrol\docs\optimizer-merge-plan.md`.

## Who owns what (hard rules on this machine)

| Domain | Owner | Optimizer's role |
|---|---|---|
| Fan/pump control | FanBrain (`fan_brain.ps1`) + FanControl.exe | **Read-only.** Never write fan/HID state. |
| Power plans (`powercfg /setactive`) | Process Lasso (Gaming Mode/IdleSaver) | **Hands off.** `OptimizePowerSettingsHandler` must not run here; profiles that include `optimize-power-settings` are not applied on this machine. |
| Display/GPU/profile switching (DDC, HDR, .nip, Chroma) | fgwatch + `ctl.ps1 apply-profile` | Invoke via `ctl.ps1` only (Phase 2 command bridge). Never write `state\profile_hint.txt` directly — it carries 4 contracts. |
| Hardware sensors (kernel driver) | LHM web server :8085 (single driver stack) | Consume via HTTP (`ExternalSensorServerUrl`), never init in-proc LHM when the external server is configured. |
| Thermal alarms/anomalies | sentinel + ntfy push | Diagnostics may FEED findings to sentinel; do not duplicate thermal alerting. |
| Registry tweaks, cleanup, diagnostics, SMART, BSOD analysis | **Optimizer** | Full ownership (with undo capture). |
| Windows services/startup management | **Optimizer** | Full ownership. |

## Machine-local settings that encode this (in `%LocalAppData%\Optimizer\app-settings.json`)

- `AutoContextSwitchEnabled: false` + no `profile-rules.json` — profile automation stays OFF
  (fgwatch owns app→profile switching).
- `AutoApplyEnabled: false` — no unattended optimization applies.
- `ExternalSensorServerUrl: http://localhost:8085/data.json` — single LHM driver stack (Phase 1).

## Why (incidents that shaped these rules)

- Two systems fighting over power plans previously pinned max-performance 24/7
  (Plex in Lasso's gaming list). Last-write-wins on `powercfg` is not a theory.
- Optimizer's in-proc LHM (`SensorService`) loads a second kernel-driver stack next to
  the permanent :8085 instance — driver contention produces NaN sensors and stale reads.
- `fgwatch` implements manual-wins-always + DOWNGRADE_OK gating; a second 15 s first-match
  automation (ProfileAutomationService) racing it would break those guarantees.
