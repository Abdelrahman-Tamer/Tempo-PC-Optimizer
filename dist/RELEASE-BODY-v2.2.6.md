## Tempo v2.2.6 — Companion Bar Expansion, Dedicated Update Styling & UI Polish

### What's New in v2.2.6
- **Companion Toolbar Expansion & Dedicated Update Styling**: Expanded horizontal companion toolbar width to 630px to comfortably accommodate all hardware telemetry and dynamic indicators. Designed a dedicated circular update action style (`ToolbarUpdateBtn`) with vibrant emerald green `#10B981` background and isolated rendering to prevent visual cutoff.
- **Update Progress Layout Separation**: Restructured update modal download progress panel with distinct two-column layout (`*` and `Auto`) and text trimming, preventing overlap between download status text and percentage indicator.
- **Structured 2+2+2 Release Notes Categorization**: In-app update modal now organizes release highlights into structured categories (Changes, Improvements, Fixes) capped at 2 points each with denylist sanitization, eliminating technical clutter and jargon.
- **GitHub Release Direct Link**: Added a clean "Full details on GitHub" ghost action link in the update modal for complete release changelog review.
- **Bilingual Localization Sync**: Added localized strings across English and Arabic dictionaries for update groupings, sanitized categories, and GitHub release links.

---

### Cryptographic Checksums (SHA256)

```
SHA256: 212fbdd210bd5411ee1a7fa6d6dac232785424c7cbc070685e571172449296b9
Tempo-Setup-v2.2.6.exe: 212fbdd210bd5411ee1a7fa6d6dac232785424c7cbc070685e571172449296b9
Tempo-v2.2.6-win-x64.zip: a19a1244803063f87325819e112309eb7a11ed5256fe899b0a179aed6d330e9a
```

**Verify with PowerShell**:
```powershell
Get-FileHash .\Tempo-Setup-v2.2.6.exe -Algorithm SHA256
```

---

### System Requirements & Security Notes
- **Operating System**: Windows 10 (Version 1703+) or Windows 11 (64-bit x64).
- **Runtime**: [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (the installer will prompt to install if missing).
- **Privilege Architecture**:
  - The application runs strictly as standard user (`asInvoker`). Daily monitoring and standard cache cleaning require zero administrator rights.
  - Elevation is requested explicitly via UAC prompt only for SSD TRIM, machine startup toggles, and in-app update installation.
  - The installer requires Administrator rights (`PrivilegesRequired=admin`) to install into `Program Files` and uses `CloseApplications=force` to safely shut down existing Tempo processes before upgrading.
