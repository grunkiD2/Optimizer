# Optimizer V2 — Comprehensive Feature Roadmap

> Vision: Transform Optimizer from a basic tweak tool into a complete system intelligence platform. Every page gets deeper functionality. New pages add diagnostics, overclocking, updates, security, and AI-driven recommendations.

**Document Status:** Draft brainstorm — to be reviewed, prioritized, and broken into implementation plans.

---

## Known Issues (Pre-existing)

- **CRASH:** Profiles page → Import button crashes the app. Likely `FileOpenPicker.InitializeWithWindow` or JSON deserialization. Investigate before V2 work begins.

---

## Table of Contents

1. [Existing Page Upgrades](#1-existing-page-upgrades)
2. [New Pages](#2-new-pages)
3. [Cross-Cutting Capabilities](#3-cross-cutting-capabilities)
4. [Overclocking Subsystem](#4-overclocking-subsystem)
5. [Diagnostics & Detection](#5-diagnostics--detection)
6. [Recommendations Engine](#6-recommendations-engine)
7. [Prioritization Matrix](#7-prioritization-matrix)
8. [Hardware/API Dependencies](#8-hardwareapi-dependencies)

---

## 1. Existing Page Upgrades

### 1.1 Dashboard

**Current:** Live metrics (CPU/Memory/GPU/Disk/Network), health score, top processes, I/O activity.

**Upgrades:**

- **Real-time charts** — replace single numbers with sparkline graphs (60s rolling window)
- **GPU details** — per-GPU temperature, VRAM usage, power draw, fan RPM, clock speeds
- **CPU details** — per-core usage histogram, package temperature, current frequency vs base/boost, thermal throttling indicator
- **Memory details** — frequency, timings (CL-tRCD-tRP-tRAS), modules detected, cached vs available
- **Disk details** — per-drive temperature, SMART status badge, IOPS, queue depth, response time
- **Network details** — adapter name, link speed, MAC, IP, DNS, gateway, signal strength (Wi-Fi)
- **Battery widget** (laptops) — charge %, time remaining, health %, cycles, design vs current capacity
- **System uptime** — boot time, uptime, last sleep duration
- **Alert banner** — surface critical issues (thermal throttling, disk failing, low memory)
- **Quick actions tile** — Apply Safe Tune (existing), Run Cleanup, Kill Heavy Process, Reboot Recommended
- **Performance score** — composite score 0-100 with category breakdown (responsiveness, thermals, stability)
- **Process detail flyout** — click process → see modules, threads, file handles, network connections
- **Customizable layout** — drag/drop tile arrangement, hide/show widgets
- **Compact mode** — minimal "always on top" pinned widget

**Detection capabilities:**
- Thermal throttling event detection
- Memory leak detection (gradual memory growth pattern)
- Disk failure prediction (SMART trends)
- Background process anomaly (process suddenly using 50%+ CPU)

---

### 1.2 Performance

**Current:** 5 optimizations (background apps, animations, visual effects, power settings, page file).

**Upgrades:**

**Process & Power Management:**
- **Power Plan Manager** — switch between Balanced/High Performance/Ultimate Performance/Custom with one click
- **Process priority manager** — set Realtime/High/Above Normal/Normal/Below Normal/Idle per process
- **CPU affinity manager** — pin specific processes to specific cores
- **Background processes audit** — list of services running in background with "necessary?" indicator
- **Game Mode** — comprehensive (not just Windows toggle): disable GPU scheduling for other apps, raise game priority, disable notifications, etc.
- **Fullscreen optimizations** toggle per-game
- **Hardware-Accelerated GPU Scheduling** toggle
- **Variable Refresh Rate** toggle

**Memory Tuning:**
- **Memory compression** toggle
- **Standby memory** auto-clear configuration
- **Empty working sets** button (free RAM from cached processes)
- **SuperFetch/Prefetch** tuning
- **Page file** per-drive configuration with smart recommendations

**Visual/UX:**
- **Animations granular control** — taskbar, window minimize/maximize, menus, tooltips (each toggleable)
- **DPI scaling override** per-application
- **Cursor settings** — speed, blink rate, snap-to default
- **Sound scheme** — silence Windows sounds with one toggle

**Benchmarking:**
- **CPU benchmark** — integrated (PassMark-style or custom)
- **Memory bandwidth** benchmark
- **Latency test** — keyboard-to-screen, click-to-screen
- **Baseline + diff** — measure before/after applying optimizations

**Detection:**
- Boot time slow → suggest delayed startup items
- Game running but power plan is Balanced → suggest High Performance auto-switch
- Many background processes → suggest review

---

### 1.3 Network

**Current:** 2 optimizations (network settings, DNS flush).

**Upgrades:**

**Connectivity:**
- **Live speedtest** — Ookla/Cloudflare API integration, results graphed over time
- **Latency monitor** — ping graph to gateway, DNS, external server (Google/Cloudflare)
- **Bandwidth usage per-process** — which apps are using your network right now
- **Connection quality meter** — packet loss %, jitter, MOS score

**DNS:**
- **DNS configuration** — Cloudflare 1.1.1.1, Google 8.8.8.8, Quad9, custom presets
- **DNS-over-HTTPS** toggle and provider selection
- **DNS-over-TLS** toggle
- **Cache flush + restart resolver**
- **DNS leak test**

**TCP/IP Tweaks:**
- **TCP autotuning** level (normal, restricted, disabled)
- **Receive Window (RWIN)** custom size
- **TCP Congestion Control** (CTCP, DCTCP, BBR if available)
- **Network Throttling Index** registry tweak
- **NetBIOS over TCP/IP** disable
- **IPv6** privacy address management
- **Nagle's Algorithm** disable for gaming

**Wi-Fi Specific:**
- **Channel analyzer** — visualize congestion on 2.4/5/6 GHz
- **Signal strength meter** with adapter details
- **Connected networks list** with management
- **Roaming aggressiveness** tuning
- **MIMO/Beamforming** status

**Firewall & Security:**
- **Active connections viewer** (`netstat`-style with process names)
- **Listening ports** list
- **Firewall rules editor** — add/remove/enable/disable with templates
- **Port scanner** — scan local machine for exposed ports
- **VPN status** detection + connection
- **Proxy configuration**

**Diagnostics:**
- **Network adapter health** — reset, repair, driver info
- **Packet capture** (lightweight, requires npcap)
- **DNS resolution test** for common domains
- **Trace route** to any host
- **Ping sweep** of local subnet

---

### 1.4 Storage

**Current:** 2 optimizations (temp files, Windows Update cache).

**Upgrades:**

**Disk Health:**
- **SMART monitoring** — temperature, power-on hours, bad sectors, predicted failure
- **Disk health score** per drive with explanation
- **SSD wear leveling** — life remaining %, TBW used
- **Disk benchmark** — sequential/random read/write speeds
- **Disk error log** — recent I/O errors from Event Log

**Cleanup Expansion:**
- **Smart cleanup** — temp, cache, logs, crash dumps, old Windows installations, hibernation file, system restore points
- **Browser cache cleanup** (Edge, Chrome, Firefox detection)
- **Recycle bin** flush
- **Thumbnail cache** rebuild
- **Font cache** reset
- **Icon cache** reset
- **Component store cleanup** (DISM `/Cleanup-Image /StartComponentCleanup`)

**Disk Analysis:**
- **Treemap visualizer** — see what's taking space (WinDirStat-style)
- **Large files finder** — list files >1GB sorted by size
- **Duplicate file finder** — hash-based deduplication
- **Old files finder** — files not accessed in 6/12/24+ months
- **Folder size scanner** with sortable list

**Maintenance:**
- **TRIM scheduler** (SSDs)
- **Defrag scheduler** (HDDs only — auto-skip SSDs)
- **CHKDSK launcher** with schedule for next boot
- **DISM repair** automation
- **SFC scan** automation

**Page File:**
- **Per-drive page file** size configuration
- **Auto-recommend** based on RAM and workload
- **Crash dump file** management

**Drive Management:**
- **Mount/Unmount** drives
- **Drive letter** reassignment
- **Quick format** option
- **Disk usage history** chart (track free space over time)

**Detection:**
- Drive predicted to fail (SMART) → urgent alert
- Drive >90% full → cleanup suggestion
- HDD heavily fragmented → defrag suggestion
- Unused old files >100GB → archive suggestion

---

### 1.5 System

**Current:** 3 optimizations (telemetry, consumer features, hibernation).

**Upgrades:**

**Windows Settings:**
- **Privacy dashboard** — every privacy setting in one place (camera, mic, location, ads, telemetry, advertising ID, app diagnostics)
- **Notifications** master toggle and per-app
- **Windows Recall** disable (newer builds)
- **Copilot** disable
- **Widgets** disable
- **News & Interests** disable
- **Lockscreen ads** disable
- **Start menu ads** disable
- **File Explorer ads** disable
- **Suggested apps** disable

**Telemetry Levels:**
- **Diagnostic data** level (Required/Optional)
- **Inking & typing** data
- **Tailored experiences** disable
- **Diagnostic data viewer** integration

**System Features:**
- **Hibernation** with size impact display
- **Fast Startup** toggle with warning
- **System Restore** management (create, configure size, restore)
- **Page file** (covered in Storage but cross-referenced here)
- **System protection** drives configuration

**Security Features:**
- **Memory Integrity (HVCI)** toggle with explanation
- **Virtualization-Based Security** toggle
- **Smart App Control** status
- **Core Isolation** settings
- **Tamper Protection** status
- **Controlled Folder Access** management

**Services:**
- **Service Manager** — list all services, start/stop, set startup (Auto/Auto-Delayed/Manual/Disabled)
- **Service recommendations** — "These 5 services can be safely disabled"
- **Service dependencies** viewer
- **Bulk operations** for service tweaks

**Registry:**
- **Registry cleaner** with safety scoring (only confirmed-safe entries)
- **Registry backup** before any change
- **Common tweaks** library (right-click menu, context menu options, etc.)

**Event Log:**
- **Critical errors** in last 24h/7d/30d
- **Warnings** dashboard
- **Filter by source** (Application, System, Security)

**Detection:**
- Telemetry level higher than user preference
- Unnecessary services enabled
- Critical event log errors recurring

---

### 1.6 Startup

**Current:** Lists startup entries with toggles.

**Upgrades:**

**Analysis:**
- **Boot time measurement** — actual time from power-on to desktop ready
- **Startup impact score** per item — measured CPU/disk/network impact during boot
- **Boot time graph** — historical trend
- **"You can save X seconds"** indicator showing potential improvement

**Management:**
- **Categorize startup items** — Essential, Optional, Unknown, Bloatware
- **Bulk operations** — disable all bloatware, enable all essentials
- **Delayed start** — set N second delay before launching
- **Startup folder** browser (user + all users)
- **Run keys** registry browser
- **Scheduled Tasks** filter for logon triggers (more comprehensive than current)
- **Services with Automatic startup** integration

**Bloatware Detection:**
- **Known bloatware library** — preinstalled trial software, manufacturer crapware (HP, Dell, Lenovo specific)
- **Remove suggestions** — uninstall preinstalled apps
- **Optional features** scanner — Windows Features that can be removed

**Smart Recommendations:**
- "You haven't used Spotify in 90 days but it starts with Windows"
- "Adobe Updater isn't critical; consider scheduling weekly instead"
- "Discord can be set to start in 30s for faster login"

---

### 1.7 Profiles

**Current:** 5 built-in presets + user snapshots with CRUD.

**Upgrades:**

**More Presets:**
- **Streaming** — broadcast software optimization, GPU encoder priority
- **Video Editing** — disk caching, GPU compute priority, RAM allocation
- **Music Production** — low-latency audio, disable power management on USB
- **3D Rendering** — CPU max boost, disable timer coalescing
- **Workstation** — multi-monitor, stability focus
- **Laptop Power Save** — aggressive battery extension
- **Server** — service stability, minimal UI
- **Privacy Maximum** — every telemetry/tracking option off
- **Office/Productivity** — default-friendly settings
- **Quiet PC** — minimize fan noise (lower power limits)

**Smart Profiles:**
- **Auto-switch based on context** — game launched? Switch to Gaming. Battery? Switch to Power Save.
- **Time-based switching** — work hours = Work profile, evening = Gaming
- **Application-triggered** — when OBS launches, apply Streaming profile

**Profile Operations:**
- **Compare profiles** — diff view between two profiles
- **Merge profiles** — combine selected optimizations from multiple
- **Profile inheritance** — base + delta
- **Version history** for snapshots (undo profile changes)
- **Cloud sync** — sync profiles across devices via OneDrive/GitHub Gist
- **Share via URL** — export profile as base64 share link
- **Profile marketplace** — community profiles (long-term)

**Validation:**
- **Pre-apply check** — warn if profile contains optimization that requires admin and you're not elevated
- **Conflict detection** — profile A enables X, profile B disables X
- **System compatibility** — "This profile assumes NVIDIA GPU, you have AMD"

---

### 1.8 History

**Current:** Day-grouped log of changes with undo support.

**Upgrades:**

**Filtering & Search:**
- **Filter by category** — Performance, Network, Storage, etc.
- **Filter by action** — Applied, Undone, One-time
- **Filter by reversibility**
- **Date range picker**
- **Free-text search**

**Visualization:**
- **Calendar heatmap** — see when optimizations were applied
- **Statistics dashboard** — total applied, undo rate %, most-applied optimization
- **Per-optimization usage** — "DisableTelemetry: applied 3 times, never undone"
- **Activity graph** — actions per day chart

**Export & Reporting:**
- **Export to CSV**
- **Export to PDF** — formatted report
- **Email report** (optional)
- **Print-ready view**

**Insights:**
- **"You frequently undo X"** — suggest never showing it
- **"You applied X but reverted Y, which conflicts"** — point out causality
- **Restore point** integration — show system restore points alongside history

---

### 1.9 Settings

**Current:** Appearance, Monitoring, Data sections.

**Upgrades:**

**Appearance:**
- **Custom theme builder** — pick all accent/background/text colors
- **Theme presets** — Dracula, Nord, Catppuccin, Solarized
- **Animations** speed control
- **Compact/Comfortable/Spacious** density
- **Font** customization
- **Icon style** — Fluent, Material, Classic

**Monitoring:**
- **Refresh interval** per metric type (CPU 1s, Disk 5s, etc.)
- **History retention** in days
- **Telemetry collection** opt-in (anonymous usage stats)

**Notifications:**
- **Toast notifications** — high CPU, disk full, thermal warning
- **Snooze** settings
- **Email alerts** (optional, requires SMTP config)
- **Sound** for critical alerts

**Backup & Sync:**
- **Cloud backup** — settings, profiles, history to OneDrive/GitHub
- **Local backup** — scheduled export to disk
- **Import from backup**
- **Reset to defaults** (current — keep)

**Advanced:**
- **Logging level** — Trace/Debug/Info/Warning/Error
- **Log file location** override
- **Crash reporting** opt-in
- **Auto-updates** — check daily/weekly/manual
- **Pre-release** channel toggle
- **Keyboard shortcuts** customization
- **System tray** behavior (minimize to tray, start minimized)
- **Window position** memory

**Localization:**
- **Language** selector — English, Spanish, German, French, Chinese, Japanese (i18n via resx)
- **Date/time format**
- **Units** — Decimal (1000) vs Binary (1024) for storage

---

## 2. New Pages

### 2.1 Diagnostics

**Goal:** One-click full system review with detailed reports.

**Sections:**
- **Quick scan** (30 seconds) — high-level health check
- **Full scan** (5 minutes) — comprehensive diagnostic
- **Hardware tests** — CPU stress, GPU stress, memory test (MemTest86-style), disk surface scan
- **Driver verification** — check for outdated, missing, conflicting drivers
- **Error log analysis** — parse Event Logs for critical errors with explanation
- **Reliability monitor** — embed Windows Reliability View
- **Crash dump analysis** — recent BSODs with bug check codes explained
- **Performance bottleneck detector** — identifies what's slowing the system

**Output:** Detailed report with:
- Issues found (sorted by severity)
- Recommended fixes (one-click apply where safe)
- Hardware health summary
- Comparison to baseline

---

### 2.2 Tuning (Overclocking)

**Goal:** Safe, monitored overclocking for CPU/GPU/RAM.

**See Section 4 for full details.**

---

### 2.3 Updates

**Goal:** Centralized update management for everything.

**Sections:**

**Windows Update:**
- Status overview
- Pause/resume
- History (success, failures)
- Configure active hours
- Optional updates
- Driver updates within Windows Update

**Drivers:**
- **Vendor-specific** — NVIDIA GeForce Experience API, AMD Adrenalin, Intel DSA integration
- **Generic driver scanner** — compare installed vs available
- **Driver backup** before install
- **Driver rollback** UI

**Applications:**
- **winget integration** — list outdated apps, bulk update
- **Microsoft Store** updates
- **Chocolatey** integration (if installed)

**BIOS/Firmware:**
- **Current BIOS version**
- **Vendor portal links** — direct links to motherboard manufacturer's update page
- **SSD firmware** — vendor-specific tools detection

**Patch Tuesday tracker:**
- Show upcoming Microsoft patches
- Show known issues with recent patches

---

### 2.4 Security

**Goal:** Centralized security posture management.

**Sections:**

**Windows Defender:**
- Status (real-time protection, cloud protection, sample submission)
- Quick/Full/Custom scan launchers
- Quarantine viewer
- Exclusion management
- Threat history

**Firewall:**
- Domain/Private/Public profiles status
- Custom rules editor
- Recent blocked connections
- Application access list

**Privacy Score:**
- Composite score based on telemetry, ads, location sharing, app permissions
- One-click fix for low score

**Credential Audit:**
- Saved Wi-Fi passwords viewer (requires admin)
- Stored credentials check
- Browser password import detection (security warning)

**Vulnerabilities:**
- Outdated software check
- Common Vulnerabilities and Exposures (CVE) matching for installed apps
- Open ports list (security implication)

**Encryption:**
- BitLocker status per drive
- File encryption status
- Recovery key backup status

---

### 2.5 Recommendations

**Goal:** AI-driven personalized suggestions.

**See Section 6 for full details.**

---

### 2.6 Hardware

**Goal:** Comprehensive system info — like CPU-Z + GPU-Z + Speccy combined.

**Sections:**

**CPU:**
- Model, manufacturer, codename
- Cores/threads, base/boost clock, cache (L1/L2/L3)
- Instruction sets (AVX, AVX-512, SSE, etc.)
- Socket, TDP, current voltage/frequency
- Temperature, power draw

**Memory:**
- Modules detected (size, speed, manufacturer, part number, serial)
- Per-slot info
- Current frequency, timings (CL-tRCD-tRP-tRAS-tRC)
- XMP profile detection
- Voltage

**GPU:**
- Model, vendor, VRAM size, VRAM type (GDDR6, HBM2, etc.)
- Core/memory clocks (current and boost)
- Driver version, VBIOS version
- DirectX/Vulkan/OpenCL support
- Multi-GPU detection
- HDMI/DP outputs

**Motherboard:**
- Manufacturer, model, chipset
- BIOS version, date, vendor
- SuperIO chip
- Slots (PCIe, RAM, M.2, SATA)

**Storage:**
- All drives with full specs (interface, capacity, firmware)
- SMART summary

**Network:**
- All adapters (wired, wireless, virtual)
- MAC, IP, gateway, DNS
- Link speed
- Driver version

**Display:**
- Each monitor (model, resolution, refresh rate, HDR, color depth, connection)

**Audio:**
- Playback/recording devices
- Default device
- Driver

**Power:**
- PSU info (where available — limited support)
- Battery (laptops)

**OS:**
- Windows edition, build, install date
- License status
- Boot mode (UEFI vs Legacy)
- Secure Boot status

**Export:** Full report to PDF/JSON/text.

---

### 2.7 Logs / Events

**Goal:** Browse system logs without using Event Viewer.

- **Application log**
- **System log**
- **Security log**
- **Setup log**
- **Forwarded events**
- **Custom views** (errors only, last 24h, by source)
- **Filter** by level (Critical, Error, Warning, Info)
- **Search** by keyword
- **Explain error code** — friendly description of common errors
- **Suggest fix** — link to common solutions

---

### 2.8 Reports

**Goal:** Generate exportable system reports.

**Report types:**
- **System snapshot** — full hardware + software inventory
- **Performance baseline** — benchmark scores
- **Health report** — issues found, fixes applied
- **Optimization log** — what was done and when
- **Custom report** — pick sections

**Export formats:**
- PDF (formatted, branded)
- HTML
- JSON (machine-readable)
- Plain text

**Use case:** Tech support, before/after comparison, system documentation.

---

## 3. Cross-Cutting Capabilities

### 3.1 System Tray Integration

- **Always-on** tray icon with live CPU/memory mini-meter
- **Quick menu** — switch profile, open dashboard, run cleanup
- **Notifications** for events
- **Start minimized** option

### 3.2 Keyboard Shortcuts

- Global hotkeys for profile switching
- Per-page shortcuts
- Customizable

### 3.3 Scripting / Automation

- **Run optimizations from command line** — `optimizer.exe --apply-profile gaming`
- **Webhook triggers** — POST to apply a profile
- **PowerShell module** export
- **Scheduled tasks** for automatic profile switching

### 3.4 Telemetry & Analytics (opt-in)

- Anonymous usage stats — which features are used most
- Crash reporting (Sentry-style)
- Performance metrics (page load times, optimization apply success rates)

### 3.5 Onboarding Wizard

First-launch experience:
- "Welcome — what do you use this PC for?" (Gaming, Work, Mixed)
- Suggest initial profile
- Tour of features
- Privacy settings prompt

### 3.6 Help & Documentation

- **In-app help** — tooltip explanations for every optimization
- **Knowledge base** — articles on what each tweak does
- **Video walkthroughs** (links to YouTube)
- **What's New** changelog
- **About** page with credits and license

### 3.7 Localization

Multi-language support via .resx files:
- English (default)
- Spanish, German, French, Italian (initial)
- Chinese (Simplified), Japanese, Korean (Asia)
- RTL language support (Arabic, Hebrew)

### 3.8 Accessibility

- High contrast theme
- Screen reader support (Narrator)
- Keyboard-only navigation
- Configurable font sizes
- Color-blind friendly palettes

---

## 4. Overclocking Subsystem

### 4.1 CPU Overclocking

**Intel:**
- **Multiplier (ratio)** — per-core multiplier control
- **Voltage (Vcore)** — manual, offset, adaptive
- **AVX offset** — separate ratio for AVX workloads
- **Cache (Ring) ratio**
- **Load-Line Calibration (LLC)** level
- **Power limits (PL1/PL2)**
- **Current limit (Icc Max)**
- **Turbo time** (Tau)

**AMD:**
- **Per-CCD/CCX** ratio
- **CO (Curve Optimizer)** offsets per core
- **PBO** limits (PPT/TDC/EDC)
- **Voltage (VID)** offset
- **Fabric clock (FCLK)** sync with memory

**Safety:**
- **Temperature limits** — auto-revert if package temp > X°C
- **Voltage caps** — hard cap (e.g., never above 1.4V)
- **Stability watchdog** — detect crash → auto-revert to last stable settings
- **Stress test integration** — Prime95, Cinebench, custom workload
- **Stability score** based on hours of stability testing

**Profiles:**
- Stock
- Mild OC (XMP + small multiplier bump)
- Moderate OC
- Aggressive OC
- Custom

### 4.2 GPU Overclocking

**NVIDIA (via NVAPI):**
- **Core clock offset** (MHz)
- **Memory clock offset** (MHz)
- **Power limit** (%)
- **Temperature limit** (°C)
- **Voltage offset** (mV)
- **Fan curve editor** — multi-point curve

**AMD (via ADL):**
- Same as above with vendor differences
- **Memory timings** (HBM/GDDR)

**Generic:**
- **Frame rate cap** per game
- **Vsync override** per game

**Safety:**
- Auto-revert on driver crash
- Temperature monitoring
- Game stability test integration

### 4.3 RAM Overclocking

- **XMP/DOCP/EXPO** profile loading
- **Frequency** manual
- **Timings** (primary: CL, tRCD, tRP, tRAS, tRC, CR)
- **Sub-timings** (advanced)
- **Voltage** (DRAM, IMC, SoC)
- **Stress testing** (TestMem5, Karhu integration)

### 4.4 Limitations & Disclaimers

- **Vendor APIs required** — NVAPI, ADL, Intel XTU SDK
- **Hardware support matters** — K-series Intel, X/Ryzen unlocked AMD, OC-friendly motherboards
- **Risk acknowledgment** — clear warnings, signed agreement on first use
- **Warranty implications** — explicit disclosure

---

## 5. Diagnostics & Detection

### 5.1 Hardware Diagnostics

- **CPU stress test** — sustained 100% load with temperature/voltage logging
- **GPU stress test** — Furmark-equivalent or 3D mark integration
- **Memory test** — MemTest86 launcher (boot-time) or RAM stress in Windows
- **Disk surface scan** — bad sector detection
- **Fan/cooling test** — measure temperature delta under load
- **Display test** — dead pixel detection, color accuracy

### 5.2 Driver Diagnostics

- **Outdated drivers** detection
- **Generic vs OEM** drivers
- **Conflicting drivers** (e.g., NVIDIA + Intel iGPU)
- **Missing drivers** (Device Manager errors)
- **Driver rollback history**

### 5.3 Software Diagnostics

- **System file integrity** (SFC equivalent UI)
- **Component store health** (DISM)
- **Windows Update repair** automation
- **Profile corruption** check
- **Registry consistency** check

### 5.4 Performance Diagnostics

- **Boot time analyzer** — what slowed boot
- **Memory leak detector** — track suspicious processes
- **High CPU detector** — log who used most CPU in last 24h
- **Disk I/O bottleneck** — which process is hammering disk
- **Network bottleneck** — which process is saturating bandwidth

### 5.5 Network Diagnostics

- **DNS lookup chain** trace
- **MTU optimization** — find optimal MTU size
- **Packet loss test**
- **Routing diagnostics**
- **Wi-Fi spectrum analysis**

---

## 6. Recommendations Engine

**Concept:** Continuously analyze the system and surface personalized suggestions.

### 6.1 Detection Triggers

- **Disk usage** > 90% → cleanup suggestion
- **RAM usage** consistently > 80% → upgrade suggestion
- **CPU temperature** > 80°C sustained → thermal issue
- **GPU driver** > 60 days old → update suggestion
- **No restore point** in 30 days → create one
- **Telemetry level** higher than user-preferred → disable
- **5+ apps in startup** unused in 30 days → suggest removal
- **No backup** in 90 days → suggest backup
- **Battery health** < 80% (laptops) → battery aging notice
- **SMART warning** on any drive → urgent replace
- **Critical Event Log** errors recurring → investigation prompt

### 6.2 Recommendation Card Format

Each recommendation shows:
- **Severity badge** (Info / Warning / Critical)
- **Title** — what the issue is
- **Explanation** — why it matters
- **Suggested action** — what to do
- **One-click fix** — if applicable
- **Dismiss** / **Snooze** / **Don't show again**
- **Learn more** — link to docs

### 6.3 Personalization

- Track which suggestions are accepted vs dismissed
- Adjust prioritization based on user behavior
- Respect "don't show this again" preferences
- Group similar recommendations

### 6.4 Categories

- **Health** — physical hardware issues
- **Performance** — speed/responsiveness
- **Security** — vulnerabilities, weak settings
- **Privacy** — telemetry, tracking
- **Maintenance** — updates, backups, cleanup
- **Upgrades** — hardware suggestions based on bottleneck analysis

### 6.5 Smart Insights

- "Your CPU is bottlenecking your GPU 73% of the time in gaming"
- "Your RAM is 90% used — adding 16GB would help"
- "You haven't restarted in 21 days — recommended after major updates"
- "Your SSD has 12% life remaining — plan replacement"

---

## 7. Prioritization Matrix

### Tier 1 — Critical for V2 release (next 3 months)

| Feature | Page | Effort | Impact |
|---------|------|--------|--------|
| Fix Profiles Import crash | Profiles | 1d | Critical bug |
| Real-time charts | Dashboard | 3d | High visual upgrade |
| Per-process power management | Performance | 4d | High user value |
| DNS configuration (Cloudflare/Google/custom) | Network | 2d | Easy win |
| Disk health (SMART) | Storage | 5d | Health-critical |
| Privacy dashboard | System | 3d | High user value |
| Service Manager | System | 4d | Power-user feature |
| Boot time analyzer | Startup | 4d | Differentiator |
| More built-in profiles | Profiles | 2d | Easy add |
| Filter/search history | History | 2d | Quality of life |
| Diagnostics page (basic) | NEW | 5d | Major feature |
| Recommendations engine (basic) | NEW | 7d | Major differentiator |
| Hardware page | NEW | 5d | High user value |
| System tray integration | Cross-cutting | 3d | UX polish |

**Tier 1 total:** ~50 developer-days

### Tier 2 — Important for V2.1 (months 4-6)

- Updates page (Windows + drivers + apps)
- Security page
- Cleanup expansion (browser cache, treemap)
- Power Plan Manager
- Speedtest + latency monitor
- Service recommendations
- Cloud sync for profiles
- Smart profile switching
- Notifications system
- Logs/Events page

### Tier 3 — Power User V3 (6+ months)

- Overclocking (CPU/GPU/RAM) — major effort
- Driver vendor API integration (NVAPI, ADL)
- Memory test integration
- Cloud sync for everything
- Scripting/PowerShell module
- Reports generation
- Localization (multiple languages)
- Onboarding wizard

### Tier 4 — Long-term vision

- Community profile marketplace
- Mobile companion app
- AI-driven optimization (ML-based recommendations)
- Enterprise edition (centralized fleet management)
- macOS/Linux ports (if engine abstraction allows)

---

## 8. Hardware / API Dependencies

### 8.1 Native Windows APIs (no licensing)

- **PerformanceCounter** — system metrics
- **WMI / Win32_*** — hardware info
- **Registry** — settings tweaks
- **PowerShell cmdlets** — power plans, services
- **Win32 APIs** — process management, file ops
- **Event Log API** — error monitoring
- **WMIC / DISM / SFC** — system maintenance

### 8.2 Vendor SDKs (may require registration)

- **NVAPI** (NVIDIA) — GPU control, telemetry
- **ADL** (AMD GPU) — same for AMD
- **Intel XTU SDK** — Intel CPU control
- **Intel DSA / IGCC** — Intel iGPU
- **LibreHardwareMonitor** (open-source) — comprehensive sensor data
- **OpenHardwareMonitor** — older, less maintained

### 8.3 Third-Party Tools (launched, not embedded)

- **MemTest86** — memory test
- **CrystalDiskInfo** — SMART data (or use SMART APIs directly)
- **HWiNFO** sensors (export shared memory)
- **MSI Afterburner** RTSS (overlay integration)

### 8.4 Online Services

- **Cloudflare Speed Test** API
- **Ookla Speedtest** CLI
- **NVIDIA driver versions** — check from NVIDIA cloud
- **Microsoft Update Catalog** API
- **winget** — built into Windows now

### 8.5 Anti-Cheat / Security Considerations

- **EAC / BattlEye** — overclocking tools may flag (must disclose)
- **Tamper Protection** — some tweaks blocked by default
- **HVCI** — kernel-level changes restricted
- **Secure Boot** — BIOS modifications need OS cooperation

---

## 9. Architectural Implications

To support this scope, the codebase needs evolution:

### 9.1 New abstractions

- `IHardwareInfoProvider` — query CPU/GPU/RAM/etc.
- `IDiagnosticRunner` — pluggable diagnostic checks
- `IRecommendation` — strategy pattern for suggestion sources
- `ISensorReader` — temperature/voltage/clock readings
- `IBenchmarkRunner` — async benchmark execution

### 9.2 Modularity

- Break monolithic `WindowsOptimizerService` (already partially done)
- Each new page = own subsystem
- Plugin model for community contributions (long-term)

### 9.3 Background services

- **Polling service** — continuous metric collection
- **Recommendations service** — periodic analysis
- **Update checker** — daily background check
- **Notification service** — toast dispatch

### 9.4 Data layer

- Move from JSON files to SQLite for history (better query)
- Time-series data for metrics
- Cloud sync layer (when applicable)

### 9.5 Cross-process

- Background service for monitoring (continues when UI closed)
- Tray icon hosted separately
- Elevated helper process for admin operations

---

## 10. Next Steps

1. **Triage:** User reviews and prioritizes
2. **Fix Import crash** — investigate immediately
3. **Pick Tier 1 features** for next implementation cycle
4. **Brainstorming session** for any unclear features
5. **Spec each feature** → implementation plan → execute

This document is a living roadmap — update as priorities shift.
