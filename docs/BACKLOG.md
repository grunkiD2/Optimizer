# Optimizer — Backlog

Parked opportunities, not yet scheduled.

## From the Windows Settings reference (registry-backed, directly actionable)

Source: *Reference for Windows 11 and Windows 10 settings* (learn.microsoft.com/windows/apps/develop/settings/settings-common).
We use **none** of these keys today; all are plain registry reads/writes.

### 1. Use Windows' stored "user intent" as a context-detection signal  *(highest ROI)*
`ContextDetectionService` currently uses processes + time-of-day only. Windows stores the user's
declared setup intent as a bitmask:
- `HKCU\Software\Microsoft\Windows\CurrentVersion\CloudExperienceHost\Intent` — bits:
  `0b10`=Gaming, `0b100`=Family, `0b1000`=Creativity, `0b10000`=Schoolwork,
  `0b100000`=Entertainment, `0b1000000`=Business, `0b10000000`=Development
- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock` → `devModeEnabled`

Fold in as an extra signal (e.g. bias toward Gaming when that bit is set; consider modelling a
"Development" context we don't have today).

### 2. Add registry tweaks to the relevant profiles (canonical keys)
| Tweak | Key | Profile |
|---|---|---|
| Disable Autoplay | `HKCU\…\Explorer\AutoplayHandlers\DisableAutoplay` (1) | Privacy/Security |
| Transparency effects off | `HKCU\…\Themes\Personalize\EnableTransparency` (0) | Gaming / low-end perf |
| Accent on title bar / Start | `HKCU\…\DWM\ColorPrevalence`, `HKCU\…\Themes\Personalize\ColorPrevalence` | Personalization |
| Windows Update UX | `HKLM\…\WindowsUpdate\UX\Settings`: `IsContinuousInnovationOptedIn`, `IsExpedited`, `RestartNotificationsAllowed2`, `AllowMUUpdateService` | Updates page |
| USB notifications | `HKCU\Software\Microsoft\Shell\USB\NotifyOnUsbErrors`, `NotifyOnWeakCharger` | Notifications |

## Not practical from that doc (Cloud Data Store — read-only via readCloudDataSettings.exe)
- Focus Assist / Do-Not-Disturb (`QuietHoursProfile`/`QuietMoment`) — great for a Gaming profile,
  but needs a *different* toggle mechanism than this doc documents.
- App inventory metadata (`AppMetaData`: installSource, wingetID, lastLaunchTime, isPinned).
- Multi-display prefs (`minimizeWindowsOnMonitorDisconnect`, `rememberWindowLocationsPerMonitorConnection`).

## Bigger-picture
- The doc's framing is Backup/Restore **data portability**, which rhymes with our profiles/snapshots/
  undo. A "Windows settings backup/restore" feature is conceivable — feature, not optimization.

## Redesign housekeeping
- Centralize the ~25 duplicate `BoolToVisibility` converters into `App.xaml` (task #14).
