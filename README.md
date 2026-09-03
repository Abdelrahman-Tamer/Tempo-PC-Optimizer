<div align="center">

  <img src="app.png" width="100" height="100" alt="Tempo PC Optimizer Logo" style="border-radius: 20px; box-shadow: 0 8px 24px rgba(0,102,255,0.3);" />

  # Tempo Diagnostic & PC Optimizer
  **High-Precision Windows Diagnostic, Telemetry & Performance Engine**

  *أداة هندسية خفيفة وعالية الدقة لتشخيص وتحسين أداء ويندوز 10 و 11*

  <br />

  [![Release](https://img.shields.io/badge/Release-v2.2.0%20(Stable)-0066FF.svg?style=for-the-badge&logo=windows)](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer/releases/latest)
  [![Status](https://img.shields.io/badge/Status-Production--Ready-10B981.svg?style=for-the-badge)](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer)
  [![Installer Size](https://img.shields.io/badge/Installer-4.65%20MB-00E5FF.svg?style=for-the-badge)](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer/releases/latest)
  [![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20x64-6366F1.svg?style=for-the-badge)](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer)
  [![Framework](https://img.shields.io/badge/Framework-.NET%2010.0%20WPF-purple.svg?style=for-the-badge)](https://dotnet.microsoft.com/)
  [![License](https://img.shields.io/badge/License-MIT-gray.svg?style=for-the-badge)](LICENSE)

  <br />

  [**🌐 Official Website & Download Page**](https://abdelrahman-tamer.github.io/Tempo-PC-Optimizer/) • [**📦 Download Latest Release (v2.2.0)**](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer/releases/latest) • [**📝 Release Notes**](#-whats-new-in-v220) • [**🐛 Report an Issue**](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer/issues)

</div>

---

## ⚡ Highlights / أبرز المميزات

- **🚀 Zero-Fake Metrics**: Real hardware and network polling directly via Windows API and LibreHardwareMonitor — zero artificial, simulated, or fabricated metrics.
- **🛡️ Hardened Filesystem Safety**: Hierarchical boundary validation and automatic exclusion of NTFS junctions / symlinks prevent path traversal and protect personal user data.
- **⏱️ 24-Hour Safe Temp Cutoff**: Intelligent age filtering skips files created or modified within the last 24 hours to prevent collisions with active processes and installers.
- **🧠 Smart Working Sets Optimization**: Instant RAM relief by freeing inactive application working sets while safeguarding 15 core Windows NT kernel services and the host application.
- **📦 Startup Apps Manager with 100% Authentic Icons**: Reads Windows registry startup entries (`HKCU` & `HKLM`) and extracts genuine high-DPI executable icons.
- **🧹 Multi-Browser & Developer Cache Cleaner**: Clears temporary browser caches across multiple Chrome, Edge, Brave, and Firefox profiles, as well as developer caches (`npm`, `pip`, `NuGet http-cache`).
- **💽 Accurate SSD Discovery & Elevated TRIM**: Automatically differentiates solid-state drives from mechanical hard drives, issuing TRIM commands safely.
- **📌 Dual-Mode Companion Toolbar**: Ultra-compact horizontal capsule (42px) and slim vertical sidebar (34px) with live telemetry and auto-peek edge docking.
- **🌐 Complete Bilingual Parity**: Seamless English (LTR) and Arabic (RTL) interface with localized dialogs, tooltips, notifications, and system tray menus.
- **🪶 Lean win-x64 Distribution**: Standalone installer footprint reduced to **~4.65 MB** with zero debug symbol overhead.

---

## 📸 Visual Showcase / معرض شاشات التطبيق

<div align="center">
  <table>
    <tr>
      <td width="50%" align="center">
        <b>📊 Main Diagnostic Dashboard</b><br />
        <img src="docs/assets/screen_overview.png" width="100%" alt="Tempo Dashboard" />
      </td>
      <td width="50%" align="center">
        <b>🚀 Startup Manager with Authentic Icons</b><br />
        <img src="docs/assets/screen_startup.png" width="100%" alt="Startup Apps Manager" />
      </td>
    </tr>
    <tr>
      <td colspan="2" align="center">
        <b>📌 Dual-Mode Companion Desktop Bar (Top Capsule & Side Slim Dock)</b><br />
        <img src="docs/assets/screen_toolbar_top.png" width="85%" alt="Top Toolbar" />
      </td>
    </tr>
  </table>
</div>

---

## 📥 Quick Download / التنزيل المباشر

| Package | Type | Version | Arch | Size | Details / Status | Direct Link |
| :--- | :---: | :---: | :---: | :---: | :---: | :--- |
| **Tempo-Setup-v2.2.0.exe** | 🚀 **Installer** *(Recommended)* | `2.2.0` | `x64` | **4.65 MB** | Automated Inno Setup with desktop & start menu shortcuts + clean uninstaller | [**Download Setup (.exe)**](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer/releases/download/v2.2.0/Tempo-Setup-v2.2.0.exe) |
| **Tempo-v2.2.0-win-x64.zip** | 🪶 **Portable** | `2.2.0` | `x64` | **3.19 MB** | Standalone portable archive, zero installation required | [**Download ZIP**](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer/releases/download/v2.2.0/Tempo-v2.2.0-win-x64.zip) |
| **Source Code** | 📄 Open Source | `2.2.0` | `x64` | — | MIT License repository snapshot | [**Source (ZIP)**](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer/archive/refs/tags/v2.2.0.zip) |

### Cryptographic Verification

Verify installer integrity using PowerShell before running:

```powershell
Get-FileHash .\Tempo-Setup-v2.2.0.exe -Algorithm SHA256
```

**Expected SHA256 Checksum:**
```text
2e5c8baaef6a67528fcaf415693159a848e14865a4c2364183d1ac6c73c2e243
```

---

## 🛡️ Security, Safety & Architectural Disclosure / الأمان والموثوقية

Tempo is engineered with a strict **Least Privilege (`asInvoker`)** model and transparent safety guarantees:

### 1. Hierarchical Path Validation (`IsSafePath`)
Every file and directory target undergoes canonical hierarchical containment checks before deletion. Deletions are strictly blocked if paths resolve within:
- Core Windows roots: `C:\Windows` (with `C:\Windows\Temp` explicitly isolated as the only permitted system temp path), `System32`, `SysWOW64`.
- Application binaries: `C:\Program Files`, `C:\Program Files (x86)`, and the application's own working directory.
- User personal folders: `Desktop`, `Documents`, `Pictures`, `Music`, `Videos`, `Downloads`, and the root user profile.
- Source code and project definitions (`.cs`, `.xaml`, `.csproj`, `.sln`, `.config`, `.cpp`, `.h`, `.py`, `.java`, `.go`, `.rs`).

### 2. Reparse Points (Symlink & NTFS Junction) Exclusion
The filesystem traversal engine inspects `FileAttributes.ReparsePoint` on every directory and file. If an NTFS junction, soft link, or symlink is detected, it is **immediately skipped without traversal**, preventing link-following attacks that could redirect cleanup into personal data directories.

### 3. 24-Hour Safe Age Filter
`QuickCleanTemp()` and `ScanAllCaches()` apply a 24-hour age cutoff on temporary files. Files created or modified within the past 24 hours are left untouched, preventing race conditions or corruption of active downloads, background installers, or locked scratch buffers.

### 4. Kernel & Process Protection Whitelist
Memory working set optimization strictly excludes 15 critical processes:
`System`, `Idle`, `Registry`, `Secure System`, `smss`, `csrss`, `wininit`, `winlogon`, `services`, `lsass`, `dwm`, `fontdrvhost`, `Memory Compression`, `audiodg`, and `Tempo` itself.

### 5. Auto-Update Integrity Chain
The auto-updater enforces HTTPS, validates file extensions (`.exe` only), verifies the downloaded payload against a mandatory SHA256 checksum prior to execution, and downloads to an unpredictable GUID-based temporary filename.

---

## 🆕 What's New in v2.2.0 / الجديد في الإصدار 2.2.0

- **🛡️ Hardened Filesystem Engine**: Added hierarchical prefix path validation (`IsSafePath`) and iterative reparse-point skipping (`SafeEnumerateFiles`).
- **⏱️ 24-Hour Safety Cutoff**: Temporary file scans and cleanups now exclude files modified within the last 24 hours.
- **🌐 Comprehensive Localization Parity**:
  - Full English (LTR) and Arabic (RTL) localization for all modals, dialogs, toasts, and tooltips.
  - Resolved update modal layout direction, ensuring clean native presentation in both languages.
  - Corrected Browser Cache tile title mapping (`TileBrowserTitle`).
- **🔒 Installer Privilege Hardening**: Eliminated elevation of user-writable binaries during legacy cleanup and enabled clean app restart after silent background updates.
- **📦 Lean Release Package**: Stripped non-Windows runtime dependencies (`android`, `linux`, `osx`) and debug symbols, reducing setup size by ~800 KB to **4.65 MB**.
- **📊 Metric Consistency**: Harmonized cache scanning with actual cleanup routines for 1:1 reporting accuracy.

---

## 💻 System Requirements / متطلبات التشغيل

- **Operating System**: Windows 10 (Version 1703 / Creators Update or newer) or Windows 11 (64-bit).
- **Architecture**: `x64` Native.
- **Runtime**: [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (checked automatically by installer).
- **Privilege Level**: Runs standard user (`asInvoker`). Administrator elevation is only requested on-demand when performing SSD TRIM or launching updates.

---

## 🛠️ Developer & Build Guide / دليل البناء البرمجي

To clone and compile Tempo locally from source:

```powershell
# Clone repository
git clone https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer.git
cd Tempo-PC-Optimizer

# Restore dependencies
dotnet restore

# Build release binary
dotnet build -c Release

# Publish lean win-x64 distribution
dotnet publish Tempo.csproj -c Release -r win-x64 --self-contained false -o publish_tempo
```

---

## 👨‍💻 Engineering Credits & Author

- **Architect & Developer**: **Eng. Abdelrahman Emam**
- **GitHub**: [@Abdelrahman-Tamer](https://github.com/Abdelrahman-Tamer)
- **Copyright**: © 2026 Eng. Abdelrahman Emam. All rights reserved.
- **License**: Released under the [MIT License](LICENSE).
