#!/usr/bin/env pwsh
<#
.SYNOPSIS
Fills the real InstallerSha256 into the staged winget manifest from a published release.

.DESCRIPTION
The manifest ships with an all-zero placeholder hash, which fails `winget validate`.
Once a release is tagged and the release workflow publishes NetParity.exe, this script
downloads that exact asset, hashes it, and writes the value into the installer manifest.

The hash has to come from the published release asset rather than a local build. A local
build is not byte-identical to what CI produced, and a manifest that points at a hash the
release does not have will fail verification for every user.

.PARAMETER Version
Manifest version to update, for example 2.0.0. Defaults to reading the version directory.

.PARAMETER Tag
Release tag to hash. Defaults to v<Version>.

.EXAMPLE
./tools/Update-WingetManifest.ps1 -Version 2.0.0

.EXAMPLE
./tools/Update-WingetManifest.ps1 -Version 2.0.0 -Tag v2.0.0 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Version,
    [string]$Tag
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$wingetRoot = Join-Path $repoRoot 'packaging\winget'

if (-not $Version) {
    $dirs = @(Get-ChildItem -LiteralPath $wingetRoot -Directory -ErrorAction SilentlyContinue)
    if ($dirs.Count -ne 1) {
        throw "Could not infer a version. Pass -Version. Found $($dirs.Count) version directories under $wingetRoot."
    }
    $Version = $dirs[0].Name
}

if (-not $Tag) { $Tag = "v$Version" }

$manifestDir = Join-Path $wingetRoot $Version
$installerManifest = Join-Path $manifestDir 'NetParity.NetParity.installer.yaml'

if (-not (Test-Path -LiteralPath $installerManifest)) {
    throw "No installer manifest at $installerManifest"
}

$expectedUrl = "https://github.com/jalal-haidar/NetParity/releases/download/$Tag/NetParity.exe"

Write-Host "Version : $Version"
Write-Host "Tag     : $Tag"
Write-Host "Asset   : $expectedUrl"

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("netparity-hash-" + [System.Guid]::NewGuid().ToString('N') + '.exe')

try {
    Write-Host "Downloading release asset to compute its real hash..."
    # Invoking-WebRequest rather than a HEAD request: the hash is of the bytes, and we
    # want the same bytes a user will receive.
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $expectedUrl -OutFile $temp -UseBasicParsing

    if (-not (Test-Path -LiteralPath $temp)) {
        throw "Download produced no file."
    }

    $sizeMb = [math]::Round((Get-Item -LiteralPath $temp).Length / 1MB, 1)
    $hash = (Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash
    Write-Host "Downloaded $sizeMb MB"
    Write-Host "SHA256  : $hash"

    $contents = Get-Content -LiteralPath $installerManifest -Raw

    $pattern = '(?m)^(\s*InstallerSha256:\s*).*$'
    if ($contents -notmatch $pattern) {
        throw "No InstallerSha256 line found in $installerManifest"
    }

    $placeholder = '0000000000000000000000000000000000000000000000000000000000000000'
    if ($contents -notmatch $placeholder) {
        Write-Warning 'The manifest has no placeholder hash; it may already have been filled in. Overwriting anyway.'
    }

    $updated = [regex]::Replace($contents, $pattern, "`${1}$hash")

    if ($PSCmdlet.ShouldProcess($installerManifest, "Set InstallerSha256 to $hash")) {
        Set-Content -LiteralPath $installerManifest -Value $updated -NoNewline
        Write-Host "Updated $installerManifest"
    }

    Write-Host ''
    Write-Host 'Validate with:'
    Write-Host "  winget validate --manifest `"$manifestDir`""
}
finally {
    Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
}