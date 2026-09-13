# 🚀 PRRX Internet Download Manager (IDM)

<p align="center">
  <img src="https://raw.githubusercontent.com/prrxhex-cloud/Internet-Download-Manager/main/src/PRRX.IDM/Assets/app_icon.png" width="120" height="120" alt="PRRX IDM Icon"/>
  <br>
  <b>High-Performance, Next-Generation Windows Internet Download Manager & Universal Media Converter</b>
  <br>
  <i>Crafted by PRRX Cooperation</i>
</p>

<p align="center">
  <a href="#"><img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D4?logo=windows" alt="Windows"/></a>
  <a href="#"><img src="https://img.shields.io/badge/.NET-8.0%20WPF-512BD4?logo=dotnet" alt=".NET 8"/></a>
  <a href="https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/tag/v1.3.0"><img src="https://img.shields.io/badge/Version-1.3.0-107C41" alt="Version"/></a>
  <a href="#"><img src="https://img.shields.io/badge/RAM%20Footprint-~20%20MB%20Idle-brightgreen" alt="Memory"/></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-blue" alt="License"/></a>
</p>

---

## 🌟 What's New in Version 1.3.0

- 📜 **Interactive In-App Changelog & Version History in Settings**: Dedicated Fluent 2 styled changelog explorer built into Settings (Tab 4), preserving full release history for v1.3.0, v1.2.0, v1.1.0, and v1.0.0 with category badges (Features, Fixes, Performance, Security).
- 🧹 **Automatic Post-Update Cleanup (Zero Data Loss Guaranteed)**: Automated startup sweep that detects and removes stale update zip packages, staging folders (`%TEMP%\PRRX_IDM_Update_*`), Inno setup temp files (`is-*.tmp`), and binary swap files (`*.bak`, `*.old`, `*.tmp`). Includes a manual on-demand "Clean Update Cache" button in Settings. User settings, download databases, and downloads folder are 100% safeguarded.
- 🔄 **Improved Online Auto-Updater**: Resilient 3-attempt streaming retry mechanism with exponential backoff (`1s`, `2s`, `4s`), 64 KB high-speed buffering, live speed/ETA estimation, and SHA-256 chunk integrity verification.
- 🛡️ **Enterprise Security Vault & IP Watermarking**: AES-256-GCM / DPAPI protected credential vault, secure loopback IPC handshake, and complete PRRX Cooperation IP watermarks across all source assets.

---

## 🌟 What's New in Version 1.2.0

- 🎨 **Official PRRX Corporate Branding**: Unified high-definition icons and visual styling across the desktop client, extension badge, dialogs, and setup installers.
- 🌐 **Automated Chrome & Edge Integration**: Instant Native Messaging Host (`com.prrx.idm`) and browser registry configuration with clean zero-residue uninstallation.
- 🎬 **Interactive Video Grabber Panel**: Sleek floating "Download with PRRX" button injected into HTML5 video players (YouTube, Vimeo, TikTok, Bilibili) with instant resolution selection (1080p, 720p, 480p, MP3).
- ⚡ **32-Stream Turbo Multi-Connection Engine**: Saturated multi-socket downloading across 32 parallel stream fragments with 2 MB adaptive buffering and 4 MB HTTP pipelining.
- 📦 **Ultra-Compact Distribution Sizes**:
  - **Online Web Setup Installer**: Just **1.99 MB** (`PRRX_IDM_Setup_Online.exe`)
  - **Full Offline Setup Installer**: **88.09 MB** (`PRRX_Internet_Download_Manager_v1.3.0_Setup.exe`)
  - **Zero-Dependency Portable Package**: Reduced to **91.74 MB** (`PRRX_Internet_Download_Manager_v1.3.0_Portable.zip`)
- 🧭 **Interactive Welcome Onboarding Tutorial**: Built-in 5-step onboarding guide detailing automated browser integration, site access permissions, and manual unpacked loading & toolbar pinning.

---

## ⚡ Core Features & Capabilities

### 🚀 32-Stream Turbo Multi-Connection Acceleration
- Saturated multi-socket downloading across **32 parallel stream fragments** (`--concurrent-fragments 32`).
- 2 MB dynamic RAM adaptive stream buffering and 4 MB HTTP pipelining for maximum gigabit network saturation.
- Automatic recovery, segmented byte-range stitching, and pause/resume capabilities.

### 🎬 Universal 4K/8K Video Downloader
- Download from **YouTube, TikTok, Vimeo, Twitter/X, Instagram, SoundCloud, Bilibili, and 1,000+ sites**.
- Real-time resolution size estimator (4K, 1440p, 1080p Full HD, 720p HD, 480p, 360p, 144p).
- Smart publisher metadata, high-resolution thumbnail preview, and duration inspector.

### 🎵 Universal Audio Converter & iPhone Ringtone Maker
- Auto-detects local video and audio sources (MP4, MKV, MOV, WAV, FLAC, etc.) or online streaming URLs.
- Converts to **10+ Studio Audio Formats**:
  - **MP3** (MPEG-3 Universal • up to 320 kbps Studio Extreme)
  - **WAV** (Lossless Bit-Perfect PCM Studio Master)
  - **M4R** (Apple iOS iPhone Custom Ringtone)
  - **M4A / AAC** (Apple High-Definition AAC)
  - **FLAC** (Audiophile High-Res Master)
  - **OGG** (Vorbis High Compression)
  - **MP2** (Broadcast Layer II)
  - **AMR** (Voice Recording Speech Codec)
  - **OPUS** (Ultra-low latency audio)
- 1-click **Play Audio** and **Show in Folder** actions.

### 🖼️ Ultra HD Thumbnail Grabber
- Extracts 1080p Max-Resolution, 720p HD, 480p, and 360p thumbnails with exact dimensions and file weights.
- 1-click direct download and clipboard copying.

### 🌊 120Hz Fluid Physics Smooth Scrolling & Liquid Glass UI
- Direct composition hardware frame loop (`CompositionTarget.Rendering`) delivering silky-smooth 120 FPS / 144 FPS inertia scrolling.
- Modern Fluent Windows 11 Mica backdrop styling with customizable opacity factors.

### 📉 Ultra-Low Memory Footprint (~20 MB Idle)
- Automated Win32 kernel working set trimmer reducing idle memory usage down to **~20 MB** (over 93% memory reduction compared to standard WPF apps).

### 🔄 Direct GitHub Releases Auto-Updater + Self-Updating Engine
- Zero-manifest direct GitHub API checking for seamless online updates.
- 1-click on-demand self-updating extraction engine (`yt-dlp --update`) without requiring full app reinstalls.

---

## 🏗️ System Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           Web Browsers                                  │
│         Google Chrome  •  Microsoft Edge  •  Brave  •  Chromium         │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ PRRX IDM Extension (MV3)                                          │  │
│  │ ├─ Floating Video Grabber Panel (YouTube, Vimeo, TikTok)          │  │
│  │ ├─ Automatic Download Link Interception                           │  │
│  │ └─ Native Messaging & Local HTTP Bridge (Port 46543)              │  │
│  └─────────────────────────────────┬─────────────────────────────────┘  │
└────────────────────────────────────┼────────────────────────────────────┘
                                     │ Named Pipe / Loopback IPC
                                     ▼
┌─────────────────────────────────────────────────────────────────────────┐
│               PRRX Internet Download Manager Client                     │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ Presentation Layer (WPF .NET 8.0)                                 │  │
│  │ ├─ Fluent Windows 11 Mica / Acrylic Glass Backdrop                │  │
│  │ ├─ 120Hz Direct Composition Hardware Render Loop                  │  │
│  │ └─ Interactive 5-Step Onboarding & Quick Tour Wizard              │  │
│  ├───────────────────────────────────────────────────────────────────┤  │
│  │ Core Services & Download Acceleration Engine                      │  │
│  │ ├─ SegmentedDownloadEngine (32 Multi-Socket Parallel Streams)     │  │
│  │ ├─ MediaEngineService (Universal 4K/8K Video & 320kbps Audio)     │  │
│  │ ├─ ThumbnailService (Ultra HD 1080p Artwork Extractor)            │  │
│  │ └─ Win32 Kernel Working Set Compactor (~20 MB Idle Footprint)     │  │
│  └─────────────────────────────────┬─────────────────────────────────┘  │
└────────────────────────────────────┼────────────────────────────────────┘
                                     │ Multi-Threaded File I/O
                                     ▼
                      ┌─────────────────────────────┐
                      │ Local Storage / Filesystem  │
                      └─────────────────────────────┘
```

---

## 🧩 Browser Extension Setup Guide

The PRRX IDM extension seamlessly captures video links and accelerates file downloads.

### 1. Automatic Auto-Registration
PRRX IDM automatically registers the Native Messaging Host (`com.prrx.idm`) and extension registry keys in Windows Registry (`HKCU\Software\Google\Chrome` and `HKCU\Software\Microsoft\Edge`). Simply start PRRX IDM, launch Chrome or Edge, and enable the extension when prompted.

### 2. Ensuring Site Access on All Websites
1. In your browser toolbar, click the **Extensions (Puzzle)** icon.
2. Right-click **PRRX IDM Integration Module** and select **This Can Read and Change Site Data**.
3. Choose **On all sites** so the floating grabber button appears above video players on YouTube, Vimeo, TikTok, Bilibili, and other streaming platforms.

### 3. Manual Installation (Brave / Vivaldi / Chromium) & Toolbar Pinning
If you need to install the extension manually:
1. Open `chrome://extensions` or `edge://extensions` in your browser.
2. Toggle ON **Developer mode** in the top-right corner.
3. Click **Load unpacked** and select the extension directory:
   `D:\Internet Download Manager\publish\extension\` (or `D:\Internet Download Manager\extension\`).
4. Click the Extensions (Puzzle) icon on your toolbar and click the **Pin** icon next to PRRX IDM.

---

## 📥 Installation & Downloads (v1.3.0)

| Package | Filename | Size | Description |
| :--- | :--- | :--- | :--- |
| **Online Web Setup** *(Recommended)* | [`PRRX_IDM_Setup_Online.exe`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.3.0/PRRX_IDM_Setup_Online.exe) | ~1.99 MB | Lightweight installer that downloads and installs the latest engine files and sets up shortcuts. |
| **Offline Setup** | [`PRRX_Internet_Download_Manager_v1.3.0_Setup.exe`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.3.0/PRRX_Internet_Download_Manager_v1.3.0_Setup.exe) | ~88.09 MB | Complete self-contained installer for offline environments. |
| **Portable Package** | [`PRRX_Internet_Download_Manager_v1.3.0_Portable.zip`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.3.0/PRRX_Internet_Download_Manager_v1.3.0_Portable.zip) | ~91.74 MB | Zero-install standalone archive. Extract anywhere and run `PRRX.InternetDownloadManager.exe`. |

### 🔒 SHA-256 Checksums

```
0FAC1876686EAA7E01BA219C2118E70406F5B98209E9B7311FDC3562B87523A9  PRRX_IDM_Setup_Online.exe
47E6DDA04F409274D2EC66BE680B05F6DCF8BB07AC0A6986809A187B7FFD6064  PRRX_Internet_Download_Manager_v1.3.0_Setup.exe
0EB59221DA4DD25A0C2FA025FCA5A09710613D2AA48BD83AC6331E1B05830D79  PRRX_Internet_Download_Manager_v1.3.0_Portable.zip
0E5E5FF6F08AE6B097714B6210A4F9389D7B71675854D138E7B35C71B265C1C6  manifest.json
```

---

## 🛠️ Building from Source

### Prerequisites
- Windows 10 / 11 (64-bit)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Clone Repository
```powershell
git clone https://github.com/prrxhex-cloud/Internet-Download-Manager.git
cd Internet-Download-Manager
```

### Run Tests
```powershell
dotnet test PRRX.InternetDownloadManager.sln
```

### Build & Publish Single-File Executable
```powershell
dotnet publish src/PRRX.IDM/PRRX.IDM.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

---

## 📄 License
Copyright © 2026 PRRX Cooperation. Distributed under the MIT License. See [LICENSE](LICENSE) for details.
