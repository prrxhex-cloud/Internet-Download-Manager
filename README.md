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
  <a href="https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/tag/v1.8.0"><img src="https://img.shields.io/badge/Version-1.8.0-107C41" alt="Version"/></a>
  <a href="#"><img src="https://img.shields.io/badge/RAM%20Footprint-~20%20MB%20Idle-brightgreen" alt="Memory"/></a>
  <a href="LICENSE.txt"><img src="https://img.shields.io/badge/License-PRRX%20EULA-blue" alt="License"/></a>
</p>

---

## 🌟 What's New in Version 1.8.0

- 🚀 **Maximum Speed MTProto & Telegram Acceleration**: Telegram downloads now execute with 16 parallel MTProto connections (`ParallelTransfers = 16`), expanded 1 MB socket receive buffers, 512 KB part chunk sizes, zero inter-chunk latency, and multi-connection visual stream blocks for maximum bandwidth saturation.
- 📁 **Dedicated IDM Downloads Navigation Tab**: Introduced a standalone primary navigation tab housing all browser-captured downloads and complete IDM integration preferences (removed clutter from general Settings).
- 🎬 **Video Downloader Tab with 29+ Format Selector**: Target video format can now be selected prior to download across 29+ audio/video formats with MP4 set as default high-compatibility format.
- 🔄 **Media Converter Tab with Live Technical Inspector**: Conversion is now strictly limited to Internet Downloads and online streams. Includes integrated video/audio preview playback and media metadata inspection (bitrate, codec, dimensions, channels).
- 📌 **Draggable Floating Video Grabber & Direct Conversion**: Extension video grabber can now be freely dragged across the viewport with automatic coordinate persistence. Users can directly select video and audio conversion targets from the floating grabber.
- ⚡ **Sub-150ms Cold-Start Instant Dialog**: Browser download intercept and payload requests now launch the download dialog within 100ms without waiting for main window hydration.
- 🛡️ **Zero-Resurrection Hardened Uninstallation**: Forcefully terminates any remaining background processes and purges all `%AppData%` / `%LocalAppData%` caches so no residual history can resurrect.

---

## 🌟 What's New in Version 1.7.0

- ⚡ **Dynamic 32-Stream Telegram Turbo Acceleration**: Solved the Telegram 0 B/s and plain chunk list display bug. Telegram media forwarded from public channels and posts (including `NecflixsLK` and `t.me/channel/id`) automatically resolves into direct CDN stream URLs, powering downloads through the standard 32-stream segmented engine with vibrant blue connection progress blocks and maximum bandwidth saturation.
- 📥 **Closed-App Queuing & Startup Task Hydration**: Tasks sent to `@PRRX_IDM_Bot` while PRRX IDM was closed are safely stored in Cloudflare D1 Edge SQLite and hydrated immediately upon startup. Minimized-to-tray background polling keeps remote Telegram and browser sync active.
- 🛡️ **Server & Database Security Hardening**:
  - Enforced strict `X-Telegram-Bot-Api-Secret-Token` validation on Telegram webhook endpoints, rejecting missing or invalid tokens with `401 Unauthorized`.
  - Comprehensive parameterized SQL queries across Cloudflare D1 database interactions.
  - Dual-tier rate limiting by client IP and client ID on `/api/telegram/tasks`.
  - Automated background garbage collection cleaning up expired pairing codes and delivered tasks older than 1 hour.
- 🔒 **Client-Side Security Hardening**:
  - Hardware-tied Windows DPAPI encryption for stored `TelegramClientId`.
  - Strict path sanitization blocking directory traversal (`../`, `..\`) and reserved DOS device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`).
  - Windows NTFS Mark-of-the-Web (`Zone.Identifier` ZoneId=3) applied to all completed downloads.
  - Isolated temporary staging directories for chunk assembly preventing partial download exposure.
- 🧩 **Extension v1.7.0 & Build Synchronization**: Synchronized Chrome extension manifest and popup to v1.7.0 with real-time version reflection and clean packaging pipelines.

---

## 🌟 What's New in Version 1.5.0

- ☁️ **Cloud Install vs. Default Direct Download Mode Toggle**: Users can toggle between **Cloud Install** (ultra-fast video, audio, thumbnail, and file downloading routed through PRRX Cloudflare Edge servers with zero traffic congestion) and **Default Install** (direct native origin connection).
- ⚡ **Automatic Server Congestion Circuit Breaker**: Continuous real-time server health and latency monitoring automatically fails over active downloads to Default Direct mode if the Cloud server encounters heavy traffic (>2500ms latency) or instability. When conditions return to normal, users can toggle back to Cloud mode with one click.
- 🎯 **Highest Process & Network Scheduling Priority by Default**: PRRX IDM automatically requests Windows `High` process and thread scheduling priority on startup, preventing network socket starvation and packet drops during intense gaming or multitasking. Includes an in-app selector in Settings (`High`, `Above Normal`, `Normal`).
- 📜 **Official EULA & License Integration in Setup Installers**: Official PRRX Cooperation End User License Agreement & Terms of Service (`LICENSE.txt`) are now embedded directly into both the full offline Inno Setup installer and the web installer wizard.
- 🚀 **1 MB High-Throughput Buffering Engine**: Expanded segmented download chunk buffers to **1048576 bytes (1 MB)** paired with **8 MB HTTP/2 initial stream windows**, dramatically increasing fiber network saturation and reducing system interrupt latency.
- 🎬 **Accelerated Media Streaming**: Enhanced fragment extraction pipelines with 16–32 parallel streams in Cloud mode for lightning-fast YouTube, audio, and thumbnail saving.

---

- ⚡ **"More Faster Up Download Speed" — Turbo 64-Stream Concurrency Engine**: Upgraded download engine concurrency with dynamic connection pooling (clamped up to 64 parallel threads), expanded 512 KB pooled stream buffers (`ArrayPool<byte>.Shared`), and `SocketsHttpHandler.MaxConnectionsPerServer = 128` with HTTP/2 stream multiplexing.
- 💾 **Win32 Direct Cluster Pre-allocation (Zero Disk Allocation Stalls)**: Integrated Win32 `SetFileInformationByHandle` (`FileAllocationInfo = 5`) for pre-allocating sparse contiguous disk space ahead of high-speed multi-part downloads, eliminating file fragmentation and disk write bottlenecks.
- 🎬 **Tokenless YouTube Media Extraction (No Cookies Required)**: Bypasses YouTube bot-detection throttling via multi-client emulation (`android`, `ios`, `web_creator`) without requiring browser cookie export or Netscape `cookies.txt` files. Cookies are now strictly an optional fallback for private content.
- ☁️ **Cloudflare Edge Worker & D1 Database Connectivity**: Connected browser extension with the serverless Cloudflare Workers edge API and D1 database for pre-flight domain health checking, malicious host reputation inspection, and instant telemetry, while retaining local HTTP loopback (`127.0.0.1:46543`) and Native Messaging (`com.prrx.idm`).
- 🔄 **Upgraded Range-Resumed Update Engine**: In-app updater upgraded with HTTP Range header byte resumption (`bytes={existing}-`), 256 KB memory streaming buffers, and strict SHA-256 cryptographic verification against Cloudflare & GitHub release manifests.
- 🛡️ **End-to-End User Data Encryption Vault**: Zero plaintext storage and zero hardcoded credentials; user configurations (`config.json`), download history (`download_history.json`), categories, and queue states are protected via Windows DPAPI and AES-256-GCM.

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
  - **Full Offline Setup Installer**: **88.17 MB** (`PRRX_Internet_Download_Manager_v1.4.0_Setup.exe`)
  - **Zero-Dependency Portable Package**: Reduced to **91.81 MB** (`PRRX_Internet_Download_Manager_v1.4.0_Portable.zip`)
- 🧭 **Interactive Welcome Onboarding Tutorial**: Built-in 5-step onboarding guide detailing automated browser integration, site access permissions, and manual unpacked loading & toolbar pinning.

---

## ⚡ Core Features & Capabilities

### 🚀 Turbo 64-Stream Multi-Connection Acceleration
- Saturated multi-socket downloading across **up to 64 parallel stream fragments**.
- 512 KB dynamic RAM pooled stream buffering (`ArrayPool<byte>.Shared`) and 4 MB HTTP pipelining for maximum gigabit network saturation.
- Automatic recovery, segmented byte-range stitching, and pause/resume capabilities.
- Direct Win32 cluster pre-allocation for zero disk allocation stalls.

### 🎬 Universal 4K/8K Video Downloader (Tokenless Client Emulation)
- Download from **YouTube, TikTok, Vimeo, Twitter/X, Instagram, SoundCloud, Bilibili, and 1,000+ sites**.
- No cookies required for standard public videos thanks to `android,ios,web_creator` client emulation.
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

### 🔄 Dual Cloudflare & GitHub In-App Auto-Updater
- In-app updater querying both the live Cloudflare edge API and GitHub Releases API.
- HTTP Range-resumed partial downloads and strict SHA-256 integrity verification.
- 1-click on-demand self-updating extraction engine (`yt-dlp --update`) without requiring full app reinstalls.

---

## 🏗️ System Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           Web Browsers                                  │
│         Google Chrome  •  Microsoft Edge  •  Brave  •  Chromium         │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ PRRX IDM Extension (MV3) v1.4.0                                   │  │
│  │ ├─ Floating Video Grabber Panel (YouTube, Vimeo, TikTok)          │  │
│  │ ├─ Automatic Download Link Interception                           │  │
│  │ ├─ Native Messaging & Local HTTP Bridge (Port 46543)              │  │
│  │ └─ Cloudflare Workers & D1 Edge API Integration                   │  │
│  └──────────────────┬───────────────────────────────┬────────────────┘  │
└─────────────────────┼───────────────────────────────┼───────────────────┘
                      │ Local Loopback IPC            │ HTTPS Cloud API
                      ▼                               ▼
┌──────────────────────────────────────────┐  ┌───────────────────────────┐
│  PRRX IDM Desktop Client (.NET 8 WPF)    │  │ Cloudflare Workers Edge   │
│  ├─ Fluent Windows 11 Mica Glass UI      │  │ & Cloudflare D1 Database  │
│  ├─ MultiSegmentDownloader (64 Sockets)  │  │ (Domain Reputation,       │
│  ├─ Native Win32 Cluster Pre-allocation  │  │  Telemetry & Manifests)   │
│  ├─ Tokenless YouTube Extraction         │  └───────────────────────────┘
│  ├─ End-to-End Encrypted Data Vault      │
│  └─ Resilient Range-Resume Auto-Updater  │
└─────────────────────┬────────────────────┘
                      │ High-Speed File I/O
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
3. Click **Load unpacked** and select the extension directory inside your PRRX IDM installation folder (e.g., `<InstallDir>\extension`).
4. Click the Extensions (Puzzle) icon on your toolbar and click the **Pin** icon next to PRRX IDM.

---

## 📥 Installation & Downloads (v1.8.0)

| Package | Filename | Description |
| :--- | :--- | :--- |
| **Online Web Setup** *(Recommended)* | [`PRRX_IDM_Setup_Online.exe`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.8.0/PRRX_IDM_Setup_Online.exe) | Lightweight installer that downloads and installs the latest engine files and sets up shortcuts. |
| **Offline Setup** | [`PRRX_Internet_Download_Manager_v1.8.0_Setup.exe`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.8.0/PRRX_Internet_Download_Manager_v1.8.0_Setup.exe) | Complete self-contained installer with embedded turbo engine and browser integration. |
| **Portable Package** | [`PRRX_Internet_Download_Manager_v1.8.0_Portable.zip`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.8.0/PRRX_Internet_Download_Manager_v1.8.0_Portable.zip) | Zero-install standalone archive. Extract anywhere and run `PRRX.InternetDownloadManager.exe`. |
| **Update Manifest** | [`manifest.json`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.8.0/manifest.json) | Update metadata and checksum verification for in-app automatic updating. |

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

### Build Solution
```powershell
dotnet build PRRX.InternetDownloadManager.sln -c Release
```

### Build & Publish Single-File Executable
```powershell
dotnet publish src/PRRX.IDM/PRRX.IDM.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

---

## 📄 License
Copyright © 2026 PRRX Cooperation. Distributed under the MIT License. See [LICENSE](LICENSE) for details.
