# Windows Package Manager manifest

Staged for submission to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs).

## Layout

`winget-pkgs` expects a **multi-file** manifest: a `version` file, an `installer` file, and
one `defaultLocale` file, living in a directory named for the version. So the layout here
mirrors the upstream path exactly:

```
packaging/winget/2.0.0/NetParity.NetParity.yaml                    # version
packaging/winget/2.0.0/NetParity.NetParity.installer.yaml          # installer
packaging/winget/2.0.0/NetParity.NetParity.locale.en-US.yaml       # defaultLocale
```

Which maps to `manifests/n/NetParity/NetParity/2.0.0/` upstream.

A single combined `.yaml` does not validate, even though older documentation and a lot of
third-party guides still show that form. `winget validate` reports:

```
Manifest Error: The multi file manifest is incomplete.
```

This was verified against real upstream manifests rather than assumed: the current
`Git.Git` and `WireGuard.WireGuard` packages are all multi-file, and feeding a genuine
single-file upstream manifest to the validator fails the same way.

## Before submitting

`InstallerSha256` ships as an all-zero placeholder, which fails validation. Fill it from the
published release asset:

```powershell
./tools/Update-WingetManifest.ps1 -Version 2.0.0
```

It downloads the release binary and hashes those exact bytes. Do not hash a local build: a
local build is not byte-identical to what CI produced, and a manifest pointing at a hash the
release does not have fails verification for every user.

Then validate:

```powershell
winget validate --manifest ./packaging/winget/2.0.0
```

Expected: `Manifest validation succeeded.`

## Not live yet

Until the upstream PR is merged, `winget install NetParity.NetParity` reports no package.