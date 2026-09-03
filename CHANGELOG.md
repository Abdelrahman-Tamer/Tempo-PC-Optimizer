# Changelog / سجل التغييرات

All notable changes to the **Tempo PC Optimizer** project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [2.2.0] - 2026-09-03

### Added
- **Official Inno Setup Installer**: Packaged Tempo-Setup.exe with standard wizard (Arabic & English), auto-shortcuts, uninstaller registration, and .NET 10.0 Desktop Runtime check.
- **Discord-Style Update System**: Visual glowing update badge in window header and companion toolbar when an update is available on GitHub Releases.
- **In-App Update Modal**: Displays release notes, version delta, and one-click silent installer (/SILENT /CLOSEAPPLICATIONS).
- **Security & SHA256 Verification**: Automatic hash calculation and integrity validation before executing any downloaded installer.
- **Update Rate-Limit Caching**: 6-12 hour local cache interval to protect unauthenticated GitHub API rate limits.
- **Skip Version & Postpone Options**: Freedom to postpone update notification or permanently skip specific release versions.
- **Authentic Startup Icons**: Native 100% executable associated icon extraction for all registered startup applications.
- **Elevated SSD TRIM**: Storage media type discovery distinguishing SSDs from rotating HDDs with elevated TRIM maintenance.
- **Dual-Mode Desktop Toolbar**: Slim horizontal capsule (42px) and vertical dock (34px) with auto-peek and persistent screen coordinates.

### Changed
- **Branding Unification**: Fluent squircle neon 'T' icon standardized across desktop shortcuts, titlebar, system tray, and toolbars.
- **Size Optimization**: Distribution package reduced to ~4.4 MB installer / 3.19 MB portable zip with 0% idle CPU footprint.
- **Landing Page Redesign**: Impeccable minimalist bilingual (Arabic / English) website hosted live on GitHub Pages.

### Security
- **Least Privilege Execution**: Runs under standard user rights (sInvoker), only elevating when required for SSD TRIM or system updates.
- **Windows Kernel Whitelist**: Explicitly protects 15 core Windows NT kernel services from memory cleanup routines.

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
