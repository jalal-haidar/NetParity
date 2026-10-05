# NetParity

**Stop guessing why it lagged.**

A tiny always-on-top overlay showing CPU, RAM, network throughput, **ping, jitter and packet loss** — so you can tell whether a lag spike was your hardware, your bandwidth, or your connection, without alt-tabbing away from whatever you were doing.

[![Build](https://github.com/jalal-haidar/NetParity/actions/workflows/ci.yml/badge.svg)](https://github.com/jalal-haidar/NetParity/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

<!-- TODO: replace with a 10-15 second GIF. Desktop utilities are sold by video, and the
     lag-scenario demo needs no explanation: watch ping spike, watch jitter, "that's your
     lag, not your bandwidth". This is the single highest-impact item on the launch list. -->

## Download

Grab the latest `NetParity.exe` from [GitHub Releases](https://github.com/jalal-haidar/NetParity/releases/latest).

- **One file.** Self-contained and single-file. No installer, no .NET runtime prerequisite.
- **Windows 10 1809 or newer.**
- **SmartScreen:** Windows may warn "Windows protected your PC" because the binary is
  not yet code-signed. Choose **More info**, then **Run anyway**.

> `winget install NetParity` is not live yet. The manifest in
> [`packaging/winget`](packaging/winget) is ready to submit upstream; until it merges,
> download the exe directly.

## Why this exists

Task Manager is excellent and it is one `Alt+Tab` away. That is the whole problem:

- You cannot `Alt+Tab` mid-match, mid-call, mid-screen-share, or mid-incident.
- Task Manager's Performance tab shows **aggregate** only. To find *which process* is
  eating bandwidth you kill things one at a time and re-measure.

And bandwidth is not actually what you want to know. A link can hold a steady 80 Mbps
and still drop frames every time round-trip time swings. **Jitter is the number that
predicts stutter**, and almost no overlay shows it.

NetParity puts the four numbers that answer "was it me, my machine, or my connection?"
in one glance, on top of everything else.

## What it shows

| Metric | Meaning |
|---|---|
| **Ping** | Round-trip time to your chosen target. Green under 60 ms, amber under 150, red above. |
| **Jitter** | Mean variation between consecutive round trips. The stutter predictor. |
| **Down / Up** | Aggregate throughput across physical adapters, in bits per second. |
| **CPU / RAM** | Utilisation on the same basis Task Manager reports. |

Ping and jitter colour-code together: if they turn amber or red while throughput looks
fine, your connection is the problem, not your bandwidth.

### On the "matches Task Manager exactly" claim

It does, and that took some work. Two details worth knowing if you ever compare the two
side by side:

- **RAM** is `(total - available) / total`. Not `dwMemoryLoad`, which reports commit
  charge against the page file and disagrees with Task Manager whenever paging is
  involved — often showing near 100% while physical RAM is half empty.
- **CPU** is derived from `GetSystemTimes` as non-idle processor time. Not
  `% Processor Utility`, which folds in frequency and turbo scaling, can exceed 100%, and
  therefore has to be clamped — which hides exactly the spikes you installed this to see.

Throughput uses **decimal (SI) units**, matching both Windows and ISPs: 1000 bits is
1 Kbps. Loopback and tunnel adapters are excluded so a VPN does not double-count the
physical link underneath it.

## Using it

- **Drag** to move it. The position is remembered.
- **Double-click** to hide or show it.
- **`Ctrl` + `Alt` + `N`** toggles visibility from anywhere.
- **Right-click** for units, ping target, start-with-Windows, reset position, and exit.
- **`Esc`** exits.

Settings live in `%AppData%\NetParity\settings.json`.

## Privacy

Everything is computed locally and nothing leaves your machine. The only network traffic
is the ping probe to the target you configure, once per second. There is no telemetry,
no update check, and no account.

## Building from source

```powershell
git clone https://github.com/jalal-haidar/NetParity.git
cd NetParity

dotnet build NetParity.slnx -c Release
dotnet test NetParity.Tests\NetParity.Tests.csproj -c Release
```

To produce the shipping single-file binary:

```powershell
dotnet publish NetParity\NetParity.csproj -p:PublishProfile=win-x64 -c Release
```

Output lands in `publish\NetParity.exe`. The self-contained settings live in
`Properties\PublishProfiles\win-x64.pubxml` rather than the csproj, because they need
runtime packs that are not present on every developer machine.

Regenerate the icon after changing its artwork:

```powershell
powershell -ExecutionPolicy Bypass -File tools\Generate-Icon.ps1
```

### Layout

| Project | Role |
|---|---|
| `NetParity.Core` | All metric sampling. No UI dependency, so it is directly testable. |
| `NetParity` | The WPF shell. |
| `NetParity.Tests` | 55 tests, including cross-checks that reconcile RAM against an independently summed figure. |

## Performance

Measured on Windows 11, 16 logical cores, steady state after startup settles:

- **~1.2% of one core**
- **~44 MB** working set

CPU and RAM are sampled four times a second and the latency probe once a second. The
probe is deliberately not on the sampling loop, so a one-second probe timeout cannot
stall the other readings, and it is deliberately rate-limited, because probing four times
a second is both needlessly expensive and rude to the host being probed.

## Not built yet

Honest list of what this does not do:

- Per-process network attribution — the README of previous versions promised this and it
  was never implemented. Aggregate only, for now.
- No disk, GPU or temperature metrics.
- No sparkline history.
- No tray icon and no auto-update.
- No localisation; Windows display scaling is respected via per-monitor v2 DPI awareness.

## License

MIT. See [LICENSE](LICENSE).
