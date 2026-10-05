# NetParity — Status Assessment & Go-To-Public Plan

Date: 2026-10-05 · Branch: `csharp-rewrite` (uncommitted) · Base: `main` = PowerShell prototype

---

## Progress

Committed as `62235f8` "Rewrite in C# with correct metrics, latency, and a real release pipeline".

### Done

- [x] Restructured into `NetParity.Core` (logic) / `NetParity` (WPF) / `NetParity.Tests`.
      Sampling code has no UI dependency, so it is directly testable.
- [x] Fixed RAM parity: was `dwMemoryLoad` (commit charge), now `(total - available) / total`.
- [x] Fixed CPU parity: was `% Processor Utility` clamped at 100, now non-idle processor time
      via `GetSystemTimes`. Also removed a package dependency.
- [x] Fixed decimal units (was `mbps * 1024`), adapter re-enumeration, and baseline re-priming.
- [x] Removed the bogus RAM fallback that read a GC threshold as free memory.
- [x] Collapsed three unsynchronised timers into one sampling loop with one immutable snapshot.
- [x] Latency wedge: ping, jitter, packet loss, ICMP with TCP-handshake fallback, health grading.
- [x] Settings persistence, position memory, `Ctrl+Alt+N` toggle, ping target, unit modes, About.
- [x] Self-contained single-file publish profile; icon; per-monitor v2 DPI manifest.
- [x] CI that verifies the artifact is genuinely single-file and runtime-independent.
- [x] Release workflow with optional Authenticode signing and checksum.
- [x] 61 tests, including parity cross-checks.
- [x] Removed the PowerShell prototype and stale framework-dependent binary.
- [x] Rewrote README: removed the "run the .ps1" instructions and the unshipped per-process claim.
- [x] Merged the C# rewrite to `main` via PR #1 (squash `4bcc3b9`).

### Hardening pass (post-merge review)

Four defects found by reading the merged code rather than exercising it. All four read as
correct at a glance, which is why they survived the first pass:

- [x] **Latency toggle was a no-op when latency started disabled.** The sampler was built
      conditionally in the constructor, so switching the overlay on did nothing until a
      restart. Disabling it also did *not* stop probing, while the README claimed the probes
      stopped. `LatencyEnabled` is now a real property that gates the probe.
- [x] **Disposal raced an untracked probe task.** `BeginLatencyProbe` detached work with
      `CancellationToken.None` and nothing tracked it, so `Dispose` could free the `Ping`
      out from under a probe in flight. The task is now retained and awaited, and
      `Start` after `Dispose` throws instead of reviving a loop on dead samplers.
- [x] **A taken hotkey could strand the app.** The window is `WindowStyle=None` with
      `ShowInTaskbar=False`, so if `Ctrl+Alt+N` was already owned by another application and
      the user hid the overlay, there was no way back but Task Manager. Added a tray icon
      (show/hide, reset position, exit) and a balloon warning when hotkey registration fails.
      The icon needed embedding as a named resource, since `ApplicationIcon` only stamps the
      Win32 header and leaves nothing for code to load.
- [x] **Unit mode tore down the service on the UI thread.** Changing bits/bytes disposed and
      rebuilt the whole service mid-session. `UnitMode` is now a property.

New tests assert the behaviour rather than the absence of a crash, including a `ProbeCount`
counter that makes "is it still sending packets when it should not be" a testable question.
Removing the gate was confirmed to fail them, so they are not vacuous.

### Measured

| | Before | After |
|---|---|---|
| Steady-state CPU | 27% of one core | ~1.2% of one core |
| Working set | 113 MB, growing | ~44 MB |

The CPU regression was self-inflicted: the latency probe fired on every 250 ms sample instead of
once a second. Found by measuring, not by reading the code.

### Blocked / cannot complete here

- **Self-contained publish could not be run locally.** This machine's NuGet feed is offline and
  the win-x64 runtime packs are absent. CI has network access and will exercise it.
- **The GUI could not be launched on this machine.** App Control policy (0x800711C7) blocks
  unsigned binaries in this workspace; it blocks the previously committed `NetParity.exe` too.
  This is precisely the problem code signing solves, and it is why signing is wired into the
  release workflow rather than left as a manual step. Metric behaviour is covered by the test
  suite, which runs in the test host and drives every sampler live.

### Remaining, in priority order

1. **Sign the binary.** Obtain an Authenticode cert, add `CERT_PFX_BASE64` and `CERT_PASSWORD`
   secrets, cut a tag. Nothing else on this list matters as much; SmartScreen is the largest
   single cause of abandoned installs.
2. **Record the 15-second demo GIF.** Highest-impact marketing item. Script: saturate upload,
   show throughput flat, show ping and jitter turn amber/red. That single clip is the pitch.
   Needs a signed build on a machine that will run it, so it lands after step 1.
3. **Submit the winget manifest** upstream to replace the placeholder SHA256.
4. **Launch**: Show HN (the ICMP-to-TCP fallback is a genuine technical hook), r/gaming,
   r/windows, r/programming.
5. **Per-process network attribution** (wedge A), via `GetExtendedTcpTable`. This is the feature
   the old README promised and never had.
6. **ISP truth-check** (wedge C) as a follow-up campaign.
7. Lower priority: auto-update, sparkline history, localisation, history graph.

---

## Part 1 — How much work is remaining?

> Historical. This was the original assessment, written before commit `62235f8`. Every item in
> it has since been addressed except code signing, which needs a certificate you have to buy.
> See **Progress** above for the current state.


### The headline finding

The C# rewrite is ~340 lines, compiles clean, and is **functionally identical to the PowerShell prototype it replaced**. Zero net-new capability. It is a port, not progress. It also isn't committed — `NetParity/`, `publish/`, and `test_tracker.ps1` are all untracked.

**The critical problem: the product premise does not currently hold.**

The README sells *"Real-Time Parity: Matches Task Manager's timing and smoothing exactly."* That is not true. Three of the four metrics are computed differently from Task Manager, so numbers will visibly disagree when a user does the one comparison the app invites them to make.

| # | Issue | Location | Severity |
|---|---|---|---|
| 1 | RAM% uses `dwMemoryLoad` = **commit charge**, not physical working set. Task Manager shows working set. Numbers won't match. | `Services/RamMonitor.cs:60` | Critical — breaks core claim |
| 2 | CPU uses `% Processor Utility`, which includes turbo/frequency scaling and can exceed 100. It's clamped at 100, so load is silently hidden. Task Manager uses `% Processor Time`. | `Services/CpuMonitor.cs:21,64` | Critical — breaks core claim |
| 3 | Interface list captured **once** at startup. Dock/undock Ethernet, connect VPN, disable Wi-Fi → never re-enumerated. Adapter that goes down keeps feeding stale stats. | `Services/NetworkMonitor.cs:23` | High |
| 4 | Three background threads each raise `MetricsUpdated`, mutating 6 shared properties with **no synchronization**. `Dispatcher.Invoke` (blocking, not async) fires 3× per 250 ms. Torn reads + UI stalls. | `Services/MetricsService.cs`, `MainWindow.xaml.cs:38` | High |
| 5 | Kbps conversion uses `mbps * 1024` — mixes binary and decimal. 2.4% error. | `Services/NetworkMonitor.cs:112` | Medium |
| 6 | RAM fallback treats `GC.GetGCMemoryInfo().HighMemoryLoadThresholdBytes` as "available memory". It's a gen2 GC tuning threshold, not free RAM. Fallback reports nonsense. | `Services/RamMonitor.cs:74` | Medium |
| 7 | Dead no-op 100 ms DispatcherTimer burning cycles. | `MainWindow.xaml.cs:28,55` | Low |
| 8 | `_lastBytesReceived > 0` guard never resets the baseline if it starts at 0. | `Services/NetworkMonitor.cs:59` | Low |

### Release blockers (nothing ships publicly until these are done)

1. **Framework-dependent publish.** `publish/NetParity.runtimeconfig.json` requires `Microsoft.WindowsDesktop.App 10.0.0`. A user without .NET 10 Desktop Runtime downloads your exe and gets a crash dialog. For a "download and run" utility this is the single biggest adoption killer. → **self-contained, single-file publish.**
2. **No code signing.** Unsigned exe → SmartScreen "Windows protected your PC" → most users click away. Effectively a wall.
3. **No icon.** `<ApplicationIcon></ApplicationIcon>` is empty in the csproj.
4. **No `.github/workflows`.** No CI, no release automation. Every release is a manual local build + upload.
5. **No tests.** Zero. For an app whose entire value is "the numbers are right," there's nothing asserting the numbers are right.
6. **`.gitignore` hygiene.** `*.txt` is over-broad; `bin/`, `obj/`, `publish/` are not ignored.
7. **Branding leak.** Window title and context menu still say "SpeedTracker".
8. **No position persistence** — window resets to (10,10) every launch.
9. **No settings, no about, no update mechanism, no uninstall path.**

### Feature gaps vs. the README's own promises

The README claims *"Quickly identify when other processes are saturating your bandwidth."* **The app shows aggregate throughput only. It cannot identify processes at all.** That's an unshipped claim in the marketing copy — either build it or cut the line.

Missing entirely: per-process network, disk I/O, GPU, temperatures, latency/ping/jitter, FPS overlay, history sparklines, themes, opacity, multi-monitor, hotkeys, localization, DPI/scaling correctness, accessibility.

### Rough remaining effort

| Phase | Scope | Estimate |
|---|---|---|
| 0 | Fix bugs 1–8, add metric-correctness tests | 2–3 days |
| 1 | Self-contained publish, icon, CI, signing setup, version/about | 2–3 days |
| 2 | Position persistence, settings, context menu, hotkey, tray | 3–5 days |
| 3 | Differentiating feature (see Part 2) | 1–3 weeks |
| 4 | winget manifest, landing page, demo GIF, docs | 3–5 days |

**Phase 0–2 is ~2 weeks to a genuinely shippable, trustworthy v1.** Phase 3 depends on which direction you pick.

---

## Part 2 — Marketing: the honest version

### The problem worth solving

> **Task Manager is excellent and requires an Alt-Tab. Alt-Tab is destructive when you can't afford it.**

That's the real pain. You can't Alt-Tab when you're mid-match, mid-call, mid-screen-share, or mid-incident. And Task Manager's Performance tab shows **aggregate only** — to answer *"which process is eating my bandwidth"* you have to kill processes one at a time and re-measure.

### Why would someone switch? (And why most answers are wrong)

**"It's a smaller Task Manager" is not a reason to switch.** Nobody switches tools to get less. The current README leads with lightweight/small, which argues *against* itself as a value proposition.

Real reasons to switch:
1. **Zero context switch** — the one metric that matters, while you can't afford a context switch.
2. **Attribution** — "which app stole my bandwidth" is the question Task Manager answers *worst*.
3. **Masks/presets** — "while gaming I care about ping + jitter, nothing else."

### The competitive problem you must face

Throughput overlays are a **commodity**. TrafficMonitor (~20k GitHub stars) does exactly "taskbar network speed overlay" and is already well known. Rainmeter, HWiNFO, MSI Afterburner, CrystalDiskInfo all overlap. There is **no marketing plan that rescues a commodity feature.** Any plan that leads with "we also show network speed" loses on comparison-shopping alone.

You must own a wedge.

### Three wedges

**A. Per-process network attribution** — "who's stealing my bandwidth."
Genuinely underserved on Windows; no good free tool. Directly completes the README's existing unfulfilled claim. Doable via `GetExtendedTcpTable`/`WinDivert` polling (approximate but useful). *Highest product value, highest engineering cost.*

**B. Latency-first HUD** — ping, jitter, packet loss to a target.
Conceptually the strongest differentiator: **bandwidth does not predict lag.** Jitter does. Every competitor shows throughput; almost none show the number that actually predicts whether your game will stutter. Turns "we show Mbps too" into a supporting detail instead of the headline — which structurally escapes the TrafficMonitor comparison.

**C. ISP truth-check** — "your ISP advertises 300 Mbps, you're getting 94."
Public-good framing, shareable, gives the launch a *reason* and a news hook. Viral mechanics for a desktop utility.

### Recommendation

**Position as the lag explainer: B as headline, A as the second act, C as the launch campaign.**

Why: a desktop utility is sold by a 15-second video. B produces a visceral, instantly-understood demo (watch the ping spike, watch the jitter — "that's your lag, not your bandwidth") that needs no explanation. It names an enemy (the Alt-Tab tax). And it makes the numbers you're weakest on (Task Manager parity) into table stakes rather than the pitch.

Tagline direction: **"Stop guessing why it lagged."**

**Critical sequencing note:** do not launch a marketing push on top of unfixed parity bugs. The first person to compare your RAM% against Task Manager and post "these numbers are wrong" ends the run. Phase 0 is a marketing prerequisite, not an engineering chore.

---

## Part 3 — Making it accessible to the public

Non-negotiable mechanics, roughly in order:

1. **Self-contained single-file exe.** No runtime prerequisite. Hard requirement.
2. **Code signing.** Without it SmartScreen eats your conversion rate. An OV/EV cert, or at minimum a documented, reputable install path so binaries gain reputation.
3. **15-second GIF above the fold in the README.** Screen recording of the lag-scenario demo. Desktop utilities are a video market.
4. **`winget install <id>`** as the primary install line. This is the real unlock — it puts you in the package manager people already trust, sidesteps SmartScreen, and enables auto-update.
4b. GitHub Releases as the manual fallback.
5. **First-run that teaches itself.** Show the HUD, one line of "drag to move, right-click to exit." Zero documentation required to get value.
6. **Auto-update.** A utility that ages badly is a bad install for someone who can't easily uninstall it.
7. **Fix the README.** Kill the stale "run the .ps1" instructions and the broken release link. Add an honest screenshot, honest feature list, and the claims you actually ship.
8. **A one-command uninstall** and a clear privacy statement (all metrics local, zero network calls) — for a system-level utility, this drives installs.
9. **Launch surfaces:** r/windows, r/gaming, r/programming, Hacker News Show HN, and a well-shot demo post. Show HN rewards a crisp technical story — the ETW/per-process work gives you one.

---

## Part 4 — Suggested sequence

**Now (fix the foundation)**
- [x] Fix RAM% → working-set basis; CPU% → `% Processor Time`. Re-verify against Task Manager side by side.
- [x] Replace 3-thread fan-out with one timer; use `Dispatcher.InvokeAsync`; delete the dead timer.
- [x] Re-enumerate network interfaces on change; fix 1024→1000; fix or delete the RAM fallback.
- [x] Add tests asserting CPU/RAM/network match Task Manager within tolerance.
- [x] Commit the rewrite; tighten `.gitignore`.

**Then (make it installable)**
- [x] Self-contained single-file publish + icon + About/version.
- [x] GitHub Actions: build + test + auto-publish release.
- [ ] Signing.
- [x] Position persistence, settings, hotkey/tray.
- [x] Rewrite README.
- [ ] Record the demo GIF.
- [x] winget manifest (staged; upstream submission still pending).

**Then (differentiate)**
- [x] Latency/jitter/packet-loss monitor (wedge B).
- [ ] Per-process network attribution (wedge A).
- [ ] ISP truth-check report for shareability (wedge C).
- [ ] Launch campaign.

---

## Open decision

Part 2 hinges on one choice that changes the engineering plan, not just the copy. **Which wedge do you want to lead with — B (latency), A (per-process), or C (ISP truth-check)?**

My recommendation is B, then A. If you'd rather ship faster, C alone is the cheapest launch (an overnight throughput-to-ISP-speedtest comparison) but is the weakest long-term position.
