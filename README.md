<div align="center">

  <img src="app.png" width="100" height="100" alt="Tempo PC Optimizer Logo" style="border-radius: 20px; box-shadow: 0 8px 24px rgba(0,102,255,0.3);" />

  # Tempo Diagnostic & PC Optimizer
  **High-Precision Windows Diagnostic, Telemetry & Performance Engine**

  *أداة هندسية خفيفة وعالية الدقة لتشخيص وتحسين أداء ويندوز 10 و 11*

  <br />

  [![Release](https://img.shields.io/badge/Release-v2.2.0-0066FF.svg?style=for-the-badge&logo=windows)](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer/releases/latest)
  [![Size](https://img.shields.io/badge/Size-3.19%20MB-10B981.svg?style=for-the-badge)](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer/releases/latest)
  [![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20x64-00E5FF.svg?style=for-the-badge)](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer)
  [![Framework](https://img.shields.io/badge/Framework-.NET%2010.0%20WPF-6366F1.svg?style=for-the-badge)](https://dotnet.microsoft.com/)
  [![License](https://img.shields.io/badge/License-MIT-purple.svg?style=for-the-badge)](LICENSE)

  <br />

  [**🌐 Official Website & Download Page**](https://abdelrahman-tamer.github.io/Tempo-PC-Optimizer/) • [**📦 Download Latest Release (v2.2.0)**](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer/releases/latest) • [**🐛 Report an Issue**](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer/issues)

</div>

---

## ⚡ Highlights / أبرز المميزات

- **🚀 Zero-Fake Metrics**: Real hardware and network polling directly via Windows API and hardware sensors — zero artificial or simulated numbers.
- **🧠 Smart Working Sets Optimization**: Instant RAM relief by freeing inactive application working sets while safeguarding 15 core Windows NT kernel services.
- **📦 Startup Apps Manager with 100% Authentic Icons**: Reads Windows registry startup entries and extracts genuine associated icons from executable binaries with zero text collisions and friendly naming.
- **🧹 Developer & Browser Cache Purge**: Safely inspects and clears temporary caches, Win32 Shell Recycle Bin, Chrome & Edge caches, and developer package caches (`npm`, `NuGet http-cache`, `pip`).
- **💽 SSD Discovery & Elevated TRIM**: Automatically differentiates between SSDs and HDDs, ensuring TRIM operations maintain solid-state speeds while protecting mechanical drives from strain.
- **📌 Compact Companion Toolbar**: Ultra-compact horizontal capsule (42px) and slim vertical sidebar (34px) with live telemetry and auto-peek screen edge docking.
- **🪶 Ultra-Lightweight Footprint**: Complete portable distribution is only **3.19 MB** with 0% CPU consumption during idle.

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

| Package | Version | Architecture | Size | Checksum / Status | Direct Link |
| :--- | :---: | :---: | :---: | :---: | :--- |
| **Tempo-v2.2.0-win-x64.zip** | `2.2.0` | `x64` | **3.19 MB** | `Verified Safe / Clean` | [**Download ZIP**](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer/releases/download/v2.2.0/Tempo-v2.2.0-win-x64.zip) |
| **Portable Source Code** | `2.2.0` | `Any` | — | `MIT License` | [**Source (ZIP)**](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer/archive/refs/tags/v2.2.0.zip) |

*No installation required. Unzip anywhere and launch `Tempo.exe`.*

---

## 🛡️ Architecture & Safety Whitelist / الأمان والموثوقية

Tempo is engineered with a strict **Least Privilege (`asInvoker`)** security model. Unlike other aggressive PC cleaners, it will **never** terminate random background processes or break running services:

1. **Kernel Protection Whitelist**: The memory optimizer strictly excludes 15 critical Windows services:
   `System`, `Registry`, `smss.exe`, `csrss.exe`, `wininit.exe`, `services.exe`, `lsass.exe`, `svchost.exe`, `fontdrvhost.exe`, `dwm.exe`, `explorer.exe`, `ShellExperienceHost.exe`, `StartMenuExperienceHost.exe`, `SearchHost.exe`, `SearchIndexer.exe`.
2. **Explicit Confirmation on Permanent Actions**: Recycle Bin purge, developer cache clearance, and startup app unregistration all require explicit user confirmation with default safe selection (`No`).
3. **Registry Safety**: Only modifies user-scoped `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entries when explicitly requested by the user.

---

## 💻 System Requirements / متطلبات التشغيل

- **Operating System**: Windows 10 (Build 19041+) or Windows 11 (64-bit).
- **Architecture**: x64.
- **Prerequisites**: [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (if not already installed).
- **Permissions**: Runs cleanly under standard user permissions. UAC elevation is only requested when triggering Windows TRIM optimization for SSDs.

---

## 🛠️ Developer & Build Guide / دليل البناء البرمجي

To clone and compile Tempo locally from source:

```bash
# Clone the repository
git clone https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer.git
cd Tempo-PC-Optimizer

# Restore dependencies
dotnet restore

# Build release binary
dotnet build -c Release

# Publish lean win-x64 package
dotnet publish Tempo.csproj -c Release -r win-x64 --self-contained false -o publish_tempo
```

---

## 👨‍💻 Engineering Credits & Author

- **Architect & Developer**: **Eng. Abdelrahman Emam**
- **GitHub**: [@Abdelrahman-Tamer](https://github.com/Abdelrahman-Tamer)
- **Copyright**: © 2026 Eng. Abdelrahman Emam. All rights reserved.
- **License**: Released under the [MIT License](LICENSE).
