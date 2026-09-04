# Code Signing Roadmap — Tempo PC Optimizer

This document tracks the plan to sign Tempo binaries with an Authenticode certificate to eliminate SmartScreen warnings and reduce anti-virus false positives.

## Certificate Options

| Type | Annual Cost (est.) | SmartScreen Effect | Recommended Provider |
| :--- | :---: | :--- | :--- |
| **EV** (Extended Validation) | ~$400 | Immediate SmartScreen reputation — no warning from day one | DigiCert, Sectigo, GlobalSign |
| **OV** (Organization Validation) | ~$200 | Gradual reputation build over ~2–4 weeks of downloads | Sectigo, GlobalSign |

> **Recommendation**: EV is strongly preferred for a system-level optimiser that uses AV-sensitive APIs.

## Signing Commands

### Manual (local build)

```powershell
# Sign the main executable
signtool sign /f "cert.pfx" /p "%CERT_PASSWORD%" /fd sha256 /tr http://timestamp.digicert.com /td sha256 "publish_tempo\Tempo.exe"

# Sign the Inno Setup installer
signtool sign /f "cert.pfx" /p "%CERT_PASSWORD%" /fd sha256 /tr http://timestamp.digicert.com /td sha256 "dist\Tempo-Setup-v*.exe"

# Verify signature
signtool verify /pa "publish_tempo\Tempo.exe"
signtool verify /pa "dist\Tempo-Setup-v*.exe"
```

### CI/CD (GitHub Actions — draft)

```yaml
# .github/workflows/sign.yml — DRAFT, NOT ACTIVE
# Uncomment and configure after purchasing a certificate.
#
# name: Sign Release
# on:
#   workflow_dispatch:
#     inputs:
#       version:
#         description: "Release version (e.g. 2.2.4)"
#         required: true
#
# jobs:
#   sign:
#     runs-on: windows-latest
#     steps:
#       - uses: actions/checkout@v4
#
#       - name: Setup .NET
#         uses: actions/setup-dotnet@v4
#         with:
#           dotnet-version: "10.0.x"
#
#       - name: Publish
#         run: dotnet publish Tempo.csproj -c Release -r win-x64 --self-contained false -o publish_tempo
#
#       - name: Install AzureSignTool
#         run: dotnet tool install --global AzureSignTool
#
#       - name: Sign with Azure Key Vault
#         run: |
#           AzureSignTool sign ^
#             --azure-key-vault-url "${{ secrets.VAULT_URL }}" ^
#             --azure-key-vault-client-id "${{ secrets.VAULT_CLIENT_ID }}" ^
#             --azure-key-vault-client-secret "${{ secrets.VAULT_CLIENT_SECRET }}" ^
#             --azure-key-vault-tenant-id "${{ secrets.VAULT_TENANT_ID }}" ^
#             --azure-key-vault-certificate "${{ secrets.VAULT_CERT_NAME }}" ^
#             --file-digest sha256 ^
#             --timestamp-rfc3161 http://timestamp.digicert.com ^
#             --timestamp-digest sha256 ^
#             publish_tempo\Tempo.exe
#
#       - name: Verify signature
#         run: signtool verify /pa publish_tempo\Tempo.exe
```

## Checklist

- [ ] Purchase EV or OV code signing certificate
- [ ] Store certificate securely (Azure Key Vault preferred for CI, HSB token for EV)
- [ ] Sign `Tempo.exe` and all shipped DLLs
- [ ] Sign `Tempo-Setup-v*.exe` (Inno Setup output)
- [ ] Verify with `signtool verify /pa`
- [ ] Timestamp all signatures (`/tr` + `/td sha256`) to survive certificate expiry
- [ ] Enable CI signing workflow (uncomment `.github/workflows/sign.yml`)
- [ ] Submit first signed build to VirusTotal and link in release notes
- [ ] Monitor SmartScreen reputation for first 2 weeks post-signing

## References

- [Microsoft SignTool documentation](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool)
- [AzureSignTool (GitHub)](https://github.com/vcsjones/AzureSignTool)
- [Inno Setup SignTool configuration](https://jrsoftware.org/ishelp/index.php?topic=setup_signtool)
