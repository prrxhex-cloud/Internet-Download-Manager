# 🚀 PRRX Internet Download Manager — Release Notes v1.2.0

**Release Tag**: `v1.2.0`  
**Date**: September 12, 2026  
**Target Platform**: Windows 10 / Windows 11 (x64)  
**Publisher**: PRRX Cooperation  

---

## 🌟 What's New in This Release

### 🧭 1. Interactive 5-Step Onboarding & Browser Integration Tutorial
- **Built-in First-Run Experience**: A modern, guided 5-step onboarding wizard (`OnboardingWindow.xaml`) that welcomes users and helps them set up their optimal download environment.
  - **Step 1: Welcome Overview**: High-level introduction to 32-stream acceleration, universal media grabbing, and kernel memory compaction.
  - **Step 2: Theme Setup**: 1-click selection between System Theme, Light Mode, and Dark Mode.
  - **Step 3: Dynamic Windows 11 Transparency**: Real-time Mica / Acrylic opacity tuning slider.
  - **Step 4: Browser Integration & Grabber Tutorial**: Interactive 3-part tabbed guide detailing:
    1. *Auto-Integration*: Automated registration of Native Messaging Host (`com.prrx.idm`) for Chrome and Microsoft Edge.
    2. *Video Grabber on All Sites*: Setting extension site permissions and using the floating "Download with PRRX" button above HTML5 video players.
    3. *Manual Loading & Pinning*: Step-by-step 4-action guide for Brave, Vivaldi, and Chromium browsers with 1-click folder launcher and auto-clipboard copying.
  - **Step 5: Download Location & Quick Launch**: Choose default download directory and launch directly into the app.
- **On-Demand Replay**: Both the **Setup & Browser Tutorial** and the **Feature Quick Tour** can be replayed at any time directly from **Tab 4 (Settings & Updates)**.

### 🌐 2. Automated Chrome & Edge Extension Integration
- Automatic configuration of Windows Registry entries for Chrome and Edge native messaging hosts (`HKCU\Software\Google\Chrome\NativeMessagingHosts\com.prrx.idm` and `HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.prrx.idm`).
- High-speed local loopback IPC bridge on port `46543` for zero-latency communication.
- Clean uninstallation engine that purges host definitions and registry keys with zero system residue.

### 🎬 3. Floating Video Grabber & Universal Media Converter
- Floating "Download with PRRX" button injected into HTML5 video players across YouTube, Vimeo, TikTok, Bilibili, and 1,000+ sites.
- Quick resolution selector (1080p Full HD, 720p HD, 480p, and MP3 audio extraction).
- Universal audio converter supporting 10+ formats (MP3 up to 320 kbps Studio Extreme, WAV Lossless, M4R iPhone Ringtone, FLAC, AAC, OPUS).
- Ultra HD thumbnail grabber extracting 1080p maximum-resolution artwork in 1 click.

### ⚡ 4. 32-Stream Turbo Multi-Connection Engine
- Saturated multi-socket downloading across 32 parallel stream fragments (`--concurrent-fragments 32`).
- 2 MB dynamic RAM adaptive stream buffering and 4 MB HTTP pipelining for maximum gigabit network saturation.
- Automatic byte-range stitching, pause, and resume capabilities.

### 📉 5. Ultra-Lean Memory Footprint (~20 MB Idle)
- Background Win32 kernel working set compactor reducing idle RAM usage down to ~20 MB (over 90% memory reduction compared to standard desktop applications).

---

## 📦 Distribution Packages & Downloads

| Package | Filename | Size (MB) | Exact Size (Bytes) | Description |
| :--- | :--- | :--- | :--- | :--- |
| **Online Web Setup** *(Recommended)* | [`PRRX_IDM_Setup_Online.exe`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.2.0/PRRX_IDM_Setup_Online.exe) | **1.99 MB** | 2,081,816 bytes | Ultra-compact online installer that downloads and installs the latest engine files with zero admin prompt required. |
| **Full Offline Setup** | [`PRRX_Internet_Download_Manager_v1.2.0_Setup.exe`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.2.0/PRRX_Internet_Download_Manager_v1.2.0_Setup.exe) | **88.03 MB** | 92,309,714 bytes | Complete self-contained setup package with embedded media engines and browser extension for air-gapped systems. |
| **Portable Package** | [`PRRX_Internet_Download_Manager_v1.2.0_Portable.zip`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.2.0/PRRX_Internet_Download_Manager_v1.2.0_Portable.zip) | **91.68 MB** | 96,131,345 bytes | Zero-install standalone archive. Extract anywhere and launch `PRRX.InternetDownloadManager.exe`. |
| **Update Manifest** | [`manifest.json`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.2.0/manifest.json) | **0.001 MB** | 660 bytes | Metadata for in-app automatic update distribution. |

---

## 🔒 SHA-256 Checksums

```text
F6E009378A39D521C1E25B1DE05286AD30D967C41A16599861FD8BFEC1E34B10  PRRX_IDM_Setup_Online.exe
7CF93E220D3506C8BA3428840E2B6C235206537EB19FD63E488789A21284E1F2  PRRX_Internet_Download_Manager_v1.2.0_Setup.exe
A24100955C61DA62D68F6E7EC0019E66CD1C5940C486BDF91B6CF411057057C4  PRRX_Internet_Download_Manager_v1.2.0_Portable.zip
8C9E25A0082B521263AD41E3AF82C37F26DEDEEA399B3188D354B6F26179210A  manifest.json
```

---

## 🧪 Test Verification & Quality Assurance
- **Unit Test Suite**: 39 of 39 tests passed (`PRRX.IDM.Tests.dll`)
- **Compilation**: Clean Release win-x64 single-file build with zero warnings
- **BAML Resource Check**: `views/onboardingwindow.baml` verified embedded in `PRRX.InternetDownloadManager.g.resources`
- **Native Host Verification**: Native messaging manifest and registry integration confirmed functional
