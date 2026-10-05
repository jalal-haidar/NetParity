# Code Signing Notes

Signing is the single largest adoption blocker for a public download: Windows SmartScreen shows an "unknown publisher" wall on first run, which routinely kills installs. The release workflow is already wired to sign with `signtool` if secrets are present (`CERT_PFX_BASE64`, `CERT_PASSWORD`). This document keeps the current reality in one place: what's possible, what's not, and what to do when a signature lands.

## The current plan (realistic)

The working plan is to submit the package to winget unsigned first (this mirrors how many early projects launch), and **after** a valid Authenticode signature is in place, bump the manifest to a new release version (e.g., `2.0.1`) with the signed binary's SHA256. The first submission will not make the SmartScreen wall disappear; that is a deliberate trade-off to avoid blocking everything while going through an OSS signing approval process.

## Routes

| Route | Cost | Timeline | Notes |
|---|---|---|---|
| **Paid public CA** (DigiCert, Sectigo, GlobalSign) | $70-400/year | 1-3 weeks (identity/business verification) | Most credible with SmartScreen and least friction with winget reviewers. |
| **Azure Trusted Signing** | ~$10/month | 1-2 weeks (tenant + identity validation) | No PFX to rotate in the same way; credentials are Azure-issued. Requires a verified Azure tenant. |
| **SignPath Foundation** (free for OSS) | Free | Typically weeks | Requires an application and human review. Needs a release to already exist (cannot sign an unreleased binary set). |
| **Certum Open Source** (free for qualifying OSS) | Free | Typically weeks | Similar approval flow; requires qualifying OSS and an existing release. |

## Setup once you hold a certificate

If you obtain a PFX (paid CA), add these repository secrets:

1. `CERT_PFX_BASE64` – base64 of the `.pfx` (exported without a password if you prefer, but supply the password). In PowerShell: `[Convert]::ToBase64String([System.IO.File]::ReadAllBytes("cert.pfx"))`.
2. `CERT_PASSWORD` – the PFX password.

The release workflow (`.github/workflows/release.yml`) will sign the single-file `publish\NetParity.exe` using `Set-AuthenticodeSignature` with `timestamp.digicert.com`. After signing it verifies the signature and fails the job if it is not `Valid`.

For Azure Trusted Signing, the workflow must be adapted slightly (SignPath/Azure tooling). For SignPath/Certum, their submission process signs the uploaded binaries after approval rather than injecting secrets into CI — the workflow remains the same until approval.

## The release sequence

1. **Tag and release unsigned first** (`v2.0.0`). The asset is the self-contained single-file `NetParity.exe`. CI produces the exact bytes and computes its SHA256.
2. **Submit winget manifest unsigned** (manifest points to the same release, placeholder hash is filled from the release asset's real hash). Reviewers see the package is unsigned — be transparent about that in the PR description (this is captured in the staged manifests).
3. **Apply to an OSS signing program**. Both SignPath Foundation and Certum require an existing public release. Point them at the `v2.0.0` release URL and build artifact.
4. **When signed**: cut `v2.0.1`, publish the signed `NetParity.exe`, run `./tools/Update-WingetManifest.ps1 -Version 2.0.1` to get the new SHA256, validate, and open a second winget-pkgs PR updating to `2.0.1`.
5. **After the winget PR merging**: optionally deprecate or leave the unsigned release asset visible; the manifest controls what gets distributed via winget.

## A note about this workstation

The signing step itself does not need to be executed locally. Smart App Control is blocking unsigned builds on this machine, but CI runs on clean `windows-latest` runners with network access, signs if secrets are present, and uploads the artifact. That is the expected and reproducible path.