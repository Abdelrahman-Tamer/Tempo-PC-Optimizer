## Tempo v2.2.8 — Hardware Specs, Companion Bar Polish & Process Overhaul

### What's New in v2.2.8
- **Floating Companion Toolbar Fix**: Resolved a visual DWM window parking artifact where closing the borderless floating toolbar with the close button parked a transparent glitch stub in the bottom-left corner of the screen. Implemented full window hiding to System Tray with zero desktop residue and smooth instant restoration.
- **Real Hardware Specifications & Telemetry**: Added genuine CPU model detection (Intel/AMD), device/chassis model lookup, detailed RAM hardware specs (capacity, DDR generation, clock frequency MHz, and slot count), precise live battery percentage with AC charging status, real system uptime, and asynchronous ICMP ping latency monitor.
- **Active Process Hierarchy & Visual Identity**: Integrated high-fidelity executable icon extraction with elegant fallback monograms for all active user applications, combined multi-process grouping with process count indicators, and non-blocking asynchronous process termination.
- **Deep Clean & Elevation Hardening**: Added non-admin user temp cleanup with explicit 24-hour safety cutoff, protected ReadyBoot cache exclusion, and hardened elevated system cleaning IPC with strict target file path validation preventing arbitrary file overwrite.

---

### Cryptographic Checksums (SHA256)

```
Tempo-Setup-v2.2.8.exe: ea359f5bd965e0cb5dabc53d23909e7e24b160382598b5152cb6959b1fa06ab3
Tempo-v2.2.8-win-x64.zip: 12e4eef0560b6ca7193a33d26a9864cb2f32fa85389097109b2d19f730b2e604
```

**Verify with PowerShell**:
```powershell
Get-FileHash .\Tempo-Setup-v2.2.8.exe -Algorithm SHA256
```

---

### System Requirements & Security Notes
- **Operating System**: Windows 10 (Version 1703+) or Windows 11 (64-bit x64).
- **Runtime**: [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (the installer will prompt to install if missing).
- **Privilege Architecture**:
  - The application runs strictly as standard user (`asInvoker`). Daily monitoring and standard cache cleaning require zero administrator rights.
  - Elevation is requested explicitly via UAC prompt only for SSD TRIM, deep system junk cleanup, and machine startup toggles.
  - The installer requires Administrator rights (`PrivilegesRequired=admin`) to install into Program Files and uses `CloseApplications=force` to safely shut down existing Tempo processes before upgrading.
