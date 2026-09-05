## Tempo v2.2.7 — Hardware WMI Runtime Fix & Boot Diagnostics Hotfix

### What's New in v2.2.7
- **WMI Runtime Assembly Resolution**: Resolved an issue where published standalone builds packaged an abstract reference assembly (System.Management.dll, 74 KB) in the application root, throwing PlatformNotSupportedException on Windows and causing SSD media detection, BIOS post time, uptime, and startup apps telemetry to fail. Enforced automated MSBuild deployment of the native Windows runtime assembly (314 KB) into the publish output.
- **Physical Drive Media Detection**: Fixed SSD/HDD partition mapping to validate proper letter drive identifiers (C, D), preventing null partition pointers from corrupting the media type lookup and correctly restoring the SSD badge for all storage drives.
- **Startup Apps & Boot Telemetry Resilience**: Hardened background boot performance loading with comprehensive exception guards (when (ex is not OutOfMemoryException) and task-level error logging), preventing background worker termination and ensuring startup applications and boot time metrics are displayed reliably.

---

### Cryptographic Checksums (SHA256)

`
SHA256: e57092b10281d045759b7452aaf86c46607615c6359d94f672f7004e387c6dbb
Tempo-Setup-v2.2.7.exe: e57092b10281d045759b7452aaf86c46607615c6359d94f672f7004e387c6dbb
Tempo-v2.2.7-win-x64.zip: 0a5ce2d622e73d63e1385e498aec655204764f6c1a6b29511ec184ffafbeec49
`

**Verify with PowerShell**:
`powershell
Get-FileHash .\Tempo-Setup-v2.2.7.exe -Algorithm SHA256
`

---

### System Requirements & Security Notes
- **Operating System**: Windows 10 (Version 1703+) or Windows 11 (64-bit x64).
- **Runtime**: [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (the installer will prompt to install if missing).
- **Privilege Architecture**:
  - The application runs strictly as standard user (sInvoker). Daily monitoring and standard cache cleaning require zero administrator rights.
  - Elevation is requested explicitly via UAC prompt only for SSD TRIM, machine startup toggles, and in-app update installation.
  - The installer requires Administrator rights (PrivilegesRequired=admin) to install into Program Files and uses CloseApplications=force to safely shut down existing Tempo processes before upgrading.
