# ⚡ Tempo Diagnostic & PC Optimizer

<div align="center">

  <img src="app.png" width="120" height="120" alt="Tempo Logo" />

  ### High-Precision Windows Diagnostic, Telemetry & Performance Engine
  **أداة تشخيص وتحسين أداء نظام ويندوز فائقة الدقة والسرعة**

  [![Release](https://img.shields.io/badge/Release-v2.2.0-0066FF.svg?style=for-the-badge)](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer/releases)
  [![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20x64-00E5FF.svg?style=for-the-badge)](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer)
  [![Framework](https://img.shields.io/badge/Framework-.NET%2010.0%20WPF-44DDC1.svg?style=for-the-badge)](https://dotnet.microsoft.com/)
  [![License](https://img.shields.io/badge/License-MIT-purple.svg?style=for-the-badge)](LICENSE)

  [**Download Latest Release (v2.2.0)**](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer/releases/latest) • [**Live Website & Download Page**](https://abdelrahman-tamer.github.io/Tempo-PC-Optimizer/)

</div>

---

## 🌟 Overview / نظرة عامة

**Tempo Diagnostic & PC Optimizer** is an ultra-fast, lightweight (3.2 MB), native Windows utility engineered in **C# / .NET 10.0 WPF**. Designed with Fluent Design principles and precision dark-mode aesthetics, it offers zero-fake metrics, deep system memory optimization, safe storage cleanup, registry startup management with authentic app icons, and an ultra-compact desktop companion toolbar.

تم تصميم **Tempo** ليوفر أقصى سرعة واستجابة للنظام مع استهلاك شبه معدوم لموارد المعالج والذاكرة، وبدون أي إعلانات أو برمجيات تتبع.

---

## 🚀 Key Features / الميزات الرئيسية

### 1. ⚡ Real-Time Hardware Telemetry (قياسات حقيقية دقيقة)
- **CPU & Memory Live Gauges**: Real-time hardware polling with color-coded status rings (Green `<60%`, Yellow `60-85%`, Red `>85%`).
- **Live Network Throughput**: True byte-delta telemetry tracking real-time network upload/download speeds.
- **Top 5 RAM Consumers**: Live process working set tracking to identify background memory hogs instantly.

### 2. 🧠 Smart Working Sets Memory Boost (تعزيز الذاكرة الذكي)
- Frees inactive and cached application working sets using safe Windows API calls (`EmptyWorkingSet`).
- **Whitelisted Safety**: Protects 15 critical Windows NT kernel and system services (`dwm.exe`, `explorer.exe`, `csrss.exe`, etc.).

### 3. 🧹 Safe Cache & Deep System Cleaner (تنظيف آمن وشامل)
- **Temporary Files & Prefetch**: One-click wipe for temporary junk caches.
- **Recycle Bin**: Win32 Shell inspection with explicit confirmation dialog for permanent deletion.
- **Browser Caches**: Chrome & Edge cache clearing with browser-active detection.
- **Developer Package Caches**: Safe purge for `npm`, `NuGet (http-cache)`, and `pip` package directories.

### 4. 📦 Startup Apps Manager with Native Icons (منظم بدء التشغيل)
- Enumerates registry run entries (`HKCU` and `HKLM`).
- **100% Authentic Native Icons**: Automatically extracts high-resolution associated icons from executable binaries.
- **Clean Naming & Zero Collisions**: Strips raw GUIDs and registry hex codes into clean, human-readable app names.
- **Security App Protection**: Highlights security software (Defender, Antivirus, Avast) with an amber warning badge.

### 5. 💽 SSD Discovery & Elevated TRIM (دعم أقراص SSD والـ TRIM)
- Automatically detects drive storage type (SSD vs HDD).
- Safe TRIM command for solid-state storage to maintain peak write speeds while protecting rotational hard drives from wear.

### 6. 📌 Compact Companion Toolbar (شريط سطح المكتب المصاحب)
- **Horizontal Capsule Mode**: Sleek 42px capsule with telemetry pods and auto-dismiss notifications.
- **Vertical Slim Dock Mode**: Ultra-slim 34px sidebar docked to the screen edge with auto-peek and auto-hide.
- **Persistent Geometry**: Remembers your exact preferred screen coordinates across reboots (`position.json`).

---

## 💻 System Requirements / متطلبات التشغيل

- **Operating System**: Windows 10 (64-bit) / Windows 11 (64-bit)
- **Architecture**: x64
- **Runtime**: [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Privileges**: Standard user (`asInvoker`). UAC prompt only required when executing SSD TRIM operations.

---

## 📦 Download & Installation / التحميل والتشغيل

1. Go to the [**Releases Page**](https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer/releases/latest) or visit the [**Official Website**](https://abdelrahman-tamer.github.io/Tempo-PC-Optimizer/).
2. Download `Tempo-v2.2.0-win-x64.zip` (3.2 MB).
3. Extract the ZIP file to any folder of your choice (e.g. `C:\Program Files\Tempo` or Desktop).
4. Run `Tempo.exe`!

---

## 🛠️ Building from Source / البناء من الكود المصدري

```bash
# Clone repository
git clone https://github.com/Abdelrahman-Tamer/Tempo-PC-Optimizer.git
cd Tempo-PC-Optimizer

# Restore & Build
dotnet restore
dotnet build -c Release

# Publish portable win-x64 distribution
dotnet publish Tempo.csproj -c Release -r win-x64 --self-contained false -o publish_tempo
```

---

## 👨‍💻 Author & Engineering Credits

- **Architect & Lead Developer**: **Eng. Abdelrahman Emam**
- **GitHub**: [@Abdelrahman-Tamer](https://github.com/Abdelrahman-Tamer)
- **License**: [MIT License](LICENSE) © 2026 Eng. Abdelrahman Emam.
