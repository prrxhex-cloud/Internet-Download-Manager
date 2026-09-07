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
  <a href="#"><img src="https://img.shields.io/badge/Version-1.0.1-107C41" alt="Version"/></a>
  <a href="#"><img src="https://img.shields.io/badge/RAM%20Footprint-~20%20MB%20Idle-brightgreen" alt="Memory"/></a>
  <a href="#"><img src="https://img.shields.io/badge/License-MIT-blue" alt="License"/></a>
</p>

---

## 🌟 Highlights & Features

### ⚡ 32-Stream Turbo Multi-Connection Acceleration
- Saturated multi-socket downloading across **32 parallel stream fragments** (`--concurrent-fragments 32`).
- 2 MB RAM adaptive stream buffering and 4 MB HTTP pipelining for maximum gigabit network saturation.

### 🎬 Universal 4K/8K Video Downloader
- Download from **YouTube, TikTok, Vimeo, Twitter/X, Instagram, SoundCloud, and 1,000+ sites**.
- Real-time resolution size estimator (1080p, 720p, 480p, 360p, 144p).
- Smart channel metadata, thumbnail preview, and duration inspector.

### 🎵 Universal Audio Converter & iPhone Ringtone Maker
- Auto-detects local video and audio sources (MP4, MKV, MOV, WAV, FLAC, etc.) or online links.
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
- Extracts 1080p Max-Resolution, 720p HD, 480p, and 360p thumbnails with dimensions and estimated weights.
- 1-click direct download and clipboard copying.

### 🌊 120Hz Fluid Physics Smooth Scrolling & Liquid Glass UI
- Direct composition hardware frame loop (`CompositionTarget.Rendering`) delivering silky-smooth 120 FPS / 144 FPS inertia scrolling.
- Modern Fluent Windows 11 Mica backdrop styling.

### 📉 Ultra-Low Memory Consumption
- Automated Win32 kernel working set trimmer reducing idle memory usage down to **~20 MB** (over 93% memory reduction).

### 🔄 Direct GitHub Releases Auto-Updater + Self-Updating Engine
- Zero-manifest direct GitHub API checking for seamless online updates.
- 1-click on-demand self-updating extraction engine (`yt-dlp --update`) without requiring full app reinstalls.

---

## 📥 Installation & Downloads

### Option 1: Web Setup Installer (< 3 MB) - *Recommended*
Download `PRRX_Internet_Download_Manager_Setup.exe` from the [Latest Releases](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/latest) page. The installer automatically downloads core engine files, creates Desktop and Start Menu shortcuts, and registers in Windows Settings.

### Option 2: Portable Package (Extract & Run)
Download `PRRX_Internet_Download_Manager_v1.0.0_Portable.zip`, extract to any folder, and launch `PRRX.InternetDownloadManager.exe`.

---

## 🛠️ Building from Source

### Prerequisites
- Windows 10 / 11 (64-bit)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build Command
```powershell
dotnet publish src/PRRX.IDM/PRRX.IDM.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

---

## 📄 License
Copyright © 2026 PRRX Cooperation. Distributed under the MIT License.
