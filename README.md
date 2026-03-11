# 🚀 Internet Speed Tracker (Task Manager Parity)

A lightweight, high-precision Windows floating UI that tracks CPU, RAM, and Network speeds in real-time. Designed to match the **Windows Task Manager Performance Tab** exactly.

![SpeedTracker Preview](https://via.placeholder.com/220x100.png?text=SpeedTracker+UI) *[Replace with real screenshot]*

## 🎯 Why NetParity? (The Problem We Solve)

For both developers and gamers, the question is often: **"Why is my experience lagging right now?"** 

`NetParity` is a **Diagnostic HUD** that eliminates the need to Alt-Tab. It provides real-time, Task Manager-accurate metrics in a tiny floating window to help you instantly diagnose performance bottlenecks.

### 👩‍💻 For Developers
- **Network Debugging**: Instantly see if your API calls or file transfers are pushing data at the expected bit-rate without a second monitor.
- **Low-Overhead Monitoring**: Built with lightweight PowerShell/.NET, it won't skew your build times or slow down your IDE like a heavy profiling tool.
- **Memory Leak Detection**: Keep a transparent gauge of RAM and CPU usage over your editor to catch inefficient code early.

### 🎮 For Gamers
- **Lag Spike Diagnosis**: Instantly tell if a "lag spike" is a **Network issue** (background update) or a **PC issue** (CPU/RAM bottleneck).
- **No Alt-Tab Required**: Keep your focus on the match. Monitor your "Upload" and "Download" throughput without losing focus.
- **Ping Stability**: Quickly identify when other processes are saturating your bandwidth, causing high latency in-game.

---

## ✨ Features
- **Real-Time Parity**: Matches Task Manager's timing and smoothing exactly.
- **High Precision**: Uses .NET `NetworkInterface` calls for zero-latency monitoring.
- **Dynamic Scaling**: Automatically switches between **Mbps** and **Kbps** based on speed.
- **Task Manager Style**: Displays network throughput in **Bits**, matching standard ISP reports and Windows metrics.
- **Lightweight**: Minimal RAM/CPU footprint.

## 🛠️ How to Use
1. **Direct Run**: Right-click `NetParity.ps1` and select **Run with PowerShell**.
2. **Easy Launcher**: Double-click `StartTracker.bat` to launch without entering the console.

## 📦 How to Share
To share this with others, you can:
1. **Send the Folder**: Zip the `NetParity.ps1` and `StartTracker.bat` files.
2. **Convert to EXE**: Use the `ps2exe` module in PowerShell to create a standalone `.exe`.

## 📜 Requirements
- Windows 10/11
- PowerShell 5.1 or later (enabled by default)
