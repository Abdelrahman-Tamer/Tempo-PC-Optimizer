# Changelog / سجل التغييرات

All notable changes to the **Tempo PC Optimizer** project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.2.4] - 2026-09-03

### Added
- **Running Apps & RAM Manager Tab (التطبيقات والذاكرة)**: Dedicated navigation tab listing primary desktop applications, accurate RAM consumption in MB/GB, and percentage of total system memory.
- **Intelligent Application Rollup Engine**: Automatically bundles child worker instances, language servers, and helper processes (`language_server`, `msedgewebview2`, worker node instances) under their primary parent suite (e.g. Antigravity, Chrome, Edge) with combined memory totals.
- **Calm, Modern Dark UI**: Replaced neon traffic-light indicators with cohesive dark cards (`#141722`), calm ice-blue memory values, and elegant muted rose pill End Task buttons.
- **Smart 4-Second Auto-Refresh**: Periodically updates application memory in the background with zero lag, automatically pausing during user searches and window minimization.
- **In-App & Web Feedback System (الملاحظات والاقتراحات)**: Dedicated feedback tab in the sidebar and official landing page allowing users to submit bug reports, feature requests, and suggestions directly to the developer with optional diagnostic specs.
- **Direct End Task with Safety Guard (إنهاء المهمة)**: One-click process group termination directly from the application with immediate memory reclaim and notification.
- **Critical OS Protection**: Critical Windows system processes (`System`, `dwm`, `svchost`, `csrss`, `services`, `lsass`, `Tempo`) are badged as **Protected** with process termination disabled to safeguard system stability.
- **Live Search Filtering**: Instant, zero-latency search box to filter running processes by name or description.
- **Interactive Showcase on Download Page**: Tabbed screenshot switcher allowing visitors to preview every view in high resolution before installing.

---

## [2.2.31] - 2026-09-03

### Added
- **Desktop Toolbar SSD & Dual Network Pods**: Real-time storage percentage monitor with smart color thresholds and combined download/upload traffic telemetry (`↓ 18M  ↑ 4M`).
- **Concise Bullet-Point Update Notes**: In-app updater now formats release notes as clean, digestible bullet points without markdown clutter or links.
- **Accurate Windows Security Detection**: Parity-based Registry evaluation for `StartupApproved` system entries, correctly recognizing Windows Security as active & protected.
- **High-DPI Showcase Assets**: Re-rendered 2x Ultra-HD application screenshots across all views and companion bars.

### Fixed & Improved
- **Least-Privilege Error Boundary**: Global unhandled exception handlers logging safely to `%APPDATA%\Tempo\error.log` with zero abrupt silent crashes.
- **Companion Toolbar Dimensions**: Expanded horizontal capsule to 570px and vertical dock to 315px to accommodate all telemetry pods without clipping.

---

## [2.2.0] - 2026-09-03

### Added
- **Official Inno Setup Installer**: Packaged `Tempo-Setup-v2.2.0.exe` with standard wizard (Arabic & English), auto-shortcuts, uninstaller registration, and .NET 10.0 Desktop Runtime check.
- **Discord-Style Update System**: Visual glowing update badge in window header and companion toolbar when an update is available on GitHub Releases.
- **In-App Update Modal**: Displays release notes, version delta, and one-click silent installer (`/SILENT /CLOSEAPPLICATIONS`).
- **Security & SHA256 Verification**: Cryptographic hash calculation and integrity validation before executing any downloaded installer.
- **Update Rate-Limit Caching**: 6-12 hour local cache interval to protect unauthenticated GitHub API rate limits.
- **Skip Version & Postpone Options**: Freedom to postpone update notification or permanently skip specific release versions.
- **Authentic Startup Icons**: Native 100% executable associated icon extraction for all registered startup applications.
- **Elevated SSD TRIM**: Storage media type discovery distinguishing SSDs from rotating HDDs with elevated TRIM maintenance.
- **Dual-Mode Desktop Toolbar**: Slim horizontal capsule (42px) and vertical dock (34px) with auto-peek and persistent screen coordinates.

### Security & Safety
- **Hierarchical Path Validation**: Canonical prefix boundary check (`IsSafePath`) strictly preventing traversal into system directories (`Windows`, `System32`, `Program Files`) or user personal folders (`Desktop`, `Documents`, `Pictures`, `Music`, `Videos`, `Downloads`).
- **Reparse Point (Symlink / Junction) Protection**: Non-traversing iterative file enumeration (`SafeEnumerateFiles`) immediately skips NTFS junctions and symlinks across all cleanup modules.
- **24-Hour Safe Temp Cutoff**: Temporary file cleanups now preserve files created or modified within the past 24 hours to prevent collisions with active processes.
- **Least Privilege Execution**: Native `asInvoker` execution level running safely under standard user rights with zero mandatory elevation prompts.
- **Installer LPE Remediation**: Eliminated execution of user-writable binaries during legacy installation cleanups.
- **Kernel Protection Whitelist**: Safely protects 15 core Windows NT kernel services and `Tempo` itself from working set trimmings.

### Fixed & Changed
- **Full Localization Parity**: Localized all dialogs, notifications, storage badges, startup counts, and system tray menus in Arabic and English.
- **Update Modal Layout Direction**: Fixed hardcoded RTL alignment, allowing update dialog to honor active LTR/RTL language settings.
- **Resource Key Mismatch**: Fixed Module 3 tile key mapping to properly display `TileBrowserTitle`.
- **Silent Update Relaunch**: Enabled automatic application relaunch after background silent updates.
- **Size Optimization**: Stripped non-Windows runtime dependencies and debug symbols, bringing installer size to **4.65 MB**.

---

## [2.1.0] - 2026-09-02

### Added
- **Developer Caches Purge**: One-click cleanup for npm, NuGet http-cache, and pip package caches.
- **Recycle Bin Confirmation**: Win32 Shell API inspection with explicit confirmation dialog.
- **Top 5 RAM Consumers**: Live tracking of top memory-consuming background processes.

---

## [2.0.0] - 2026-08-30

### Added
- Initial release of Tempo PC Optimizer (.NET 10.0 Windows Desktop).
