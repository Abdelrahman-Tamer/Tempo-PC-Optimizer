## Tempo v2.2.5 — Hardened Security, Native Self-Elevation & Zero-Fake Telemetry

### What's New in v2.2.5
- **Native Self-Elevation Engine**: Completely retired external PowerShell scripting in favor of native child self-elevation (`runas`) via Tempo's own verified binary (`--set-approved`, `--measure-boot`, `--trim <LETTER>`) with strict parameter validation and watchdog termination.
- **Zero-Unsafe Managed Memory**: Enforced `<AllowUnsafeBlocks>false</AllowUnsafeBlocks>` across the entire solution, eliminating all unmanaged unsafe memory blocks.
- **Startup Protection Guard**: Added detection for Windows-managed startup entries (`0x06` byte flag in HKCU and HKLM `StartupApproved`), safeguarding system-critical autostart items from tampering.
- **Hierarchical Path & Symlink Safeguards**: Hardened directory boundary enforcement (`IsSafePath`), blocking `.git`, `.vs`, system roots, and personal user directories, with 24-hour age cutoff for temp file cleanup.
- **Strict Web Content Security Policy (CSP)**: Completely extracted inline JavaScript to external module `docs/assets/app.js`, removed `unsafe-inline` from `script-src`, and upgraded telemetry contrast to WCAG 2.2 AA.
- **Automated Security Test Suite (`Tempo.Tests`)**: Added a dedicated xUnit test project covering security path guards, 24-hour temp file cutoff, SHA-256 validation, startup protection logic, and process exclusion counts.
- **Bilingual Safe Uninstaller**: Inno Setup uninstaller now prompts user with confirmation dialog before deleting `%APPDATA%\Tempo` configuration and logs, defaulting to preserving user data.
- **Zero COM Collection Leaks**: Wrapped all WMI searcher queries across `App.xaml.cs` and `HardwareMonitorService.cs` in deterministic `using` scopes to prevent native COM collection leaks.
- **Honest UI Telemetry Fallbacks**: Eliminated synthetic CPU clock fallbacks and fabricated release notes; UI displays honest `"--"` or structured notices when hardware sensors or release bullets are absent.

---

### Cryptographic Checksums (SHA256)

```
SHA256: b1250292cef86164ca31ff607542a2ff05332931d11e5e578793384d8922eaaf
Tempo-Setup-v2.2.5.exe: b1250292cef86164ca31ff607542a2ff05332931d11e5e578793384d8922eaaf
Tempo-v2.2.5-win-x64.zip: 95bf4ee639d4acb00d8bd5a60d74e12b89f1257cac5ade6cc3506ef1b76f4aad
```

**Verify with PowerShell**:
```powershell
Get-FileHash .\Tempo-Setup-v2.2.5.exe -Algorithm SHA256
```

---

### System Requirements & Security Notes
- **Operating System**: Windows 10 (Version 1703+) or Windows 11 (64-bit x64).
- **Runtime**: [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (the installer will prompt to install if missing).
- **Privilege Architecture**:
  - The application runs strictly as standard user (`asInvoker`). Daily monitoring and standard cache cleaning require zero administrator rights.
  - Elevation is requested explicitly via UAC prompt only for SSD TRIM, machine startup toggles, and in-app update installation.
  - The installer requires Administrator rights (`PrivilegesRequired=admin`) to install into `Program Files` and uses `CloseApplications=force` to safely shut down existing Tempo processes before upgrading.
- **VirusTotal Report**: *(Link will be appended after release publication)*
