# 🚀 PRRX Internet Download Manager — Release Notes v1.4.0

**Release Tag**: `v1.4.0`  
**Target Branch**: `main`  
**Date**: September 14, 2026  
**Target Platform**: Windows 10 / Windows 11 (x64)  
**Publisher**: PRRX Cooperation  

---

## 🌟 What's New in Version 1.4.0

### ⚡ 1. "More Faster Up Download Speed" — Turbo 64-Stream Concurrency Engine
- **Adaptive Multi-Socket Pooling**: Upgraded download concurrency engine supporting up to **64 parallel download sockets** (`MultiSegmentDownloader`), fully saturating gigabit fiber connections and multi-gigabit pipelines.
- **Win32 Direct Cluster Pre-Allocation**: Implemented native `SetFileInformationByHandle` with Win32 `FileAllocationInfo` (`5`) to reserve contiguous disk clusters instantly before downloading begins, eliminating NTFS disk allocation write stalls and disk fragmentation.
- **512 KB High-Speed Memory Buffering**: Upgraded pooled buffer memory blocks from 128 KB to **512 KB** (`ArrayPool<byte>.Shared.Rent(524288)`) with `FileOptions.Asynchronous | FileOptions.SequentialScan` for low-latency asynchronous disk writes.
- **HTTP/2 Stream Multiplexing**: Upgraded `SocketsHttpHandler` with `MaxConnectionsPerServer = 128` and pooled connection reuse.

### 🎬 2. Tokenless YouTube Media Extraction (No Cookies Required)
- **Multi-Client Emulation**: Integrated client emulation (`youtube:player_client=android,ios,web_creator` and `youtubetab:approximate_date`) directly into the media extraction pipeline, successfully bypassing YouTube bot-detection algorithms without requiring browser cookie exports or Netscape `cookies.txt` files.
- **Zero-Friction Out-of-the-Box Operation**: Public 4K, 1080p, 720p, and audio streams download seamlessly on clean systems with zero cookie configuration.
- **Optional Fallback**: Cookie authentication is preserved strictly as an optional fallback when downloading private or member-only playlists.

### ☁️ 3. Cloudflare Edge Server & D1 Database Integration
- **Live Edge API**: Connected the browser extension and desktop updater with the live Cloudflare Workers edge API (`https://prrx-api.sayurusenavirathna70.workers.dev`).
- **Cloudflare D1 Database**: Integrated remote database connectivity for real-time domain reputation inspection, malware threat defense, server health metrics, and instant update catalog synchronization.
- **Dual-Status Browser Extension**: Extension popup now features a real-time status matrix showing dual connectivity for both the Desktop Engine Bridge and the Cloudflare Edge API.
- **Host Permissions**: Added `https://prrx-api.sayurusenavirathna70.workers.dev/*` to extension manifest permissions.

### 🔄 4. Upgraded Online Download & Update System
- **HTTP Range-Resumed Streaming**: Auto-updater now sends `Range: bytes={existingBytes}-` headers to resume interrupted update downloads rather than restarting from 0%, saving bandwidth and user time.
- **256 KB Memory Streaming Buffer**: Downloads update packages using high-throughput pooled 256 KB memory buffers.
- **Dual Manifest Fallback**: In-app updater checks both the Cloudflare Worker manifest endpoint (`/api/manifest`) and GitHub Releases API, dynamically selecting the highest available verified version.
- **Cryptographic SHA-256 Integrity Verification**: Strict SHA-256 hashing verifies every downloaded byte against the manifest before allowing package extraction or setup execution.

### 🔒 5. End-to-End User Data Security & Zero Secrets Policy
- **Zero Secrets / Environment Variables in Client Code**: All sensitive API keys, database access tokens, and administrative credentials remain strictly on the Cloudflare edge server — zero private credentials exist in client binaries.
- **Enterprise Data Vault**: User configurations (`config.json`), download history and queue records (`download_history.json`), and categorized download paths are end-to-end encrypted using Windows DPAPI and AES-256-GCM.
- **Zero Data Loss Guarantee**: Automatic post-update cleanup specifically protects user data, download queues, and local download folders while removing stale update artifacts.

---

## 📦 Distribution Packages & Downloads

| Package | Filename | Size (MB) | Exact Size (Bytes) | Description |
| :--- | :--- | :--- | :--- | :--- |
| **Online Web Setup** *(Recommended)* | [`PRRX_IDM_Setup_Online.exe`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.4.0/PRRX_IDM_Setup_Online.exe) | **1.99 MB** | 2,081,787 bytes | Ultra-compact web installer that streams and installs the latest components with zero administrator elevation required. |
| **Full Offline Setup** | [`PRRX_Internet_Download_Manager_v1.4.0_Setup.exe`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.4.0/PRRX_Internet_Download_Manager_v1.4.0_Setup.exe) | **88.17 MB** | 92,448,467 bytes | Complete self-contained setup package with embedded 64-stream turbo engine, media extraction tools, and browser integration. |
| **Portable Package** | [`PRRX_Internet_Download_Manager_v1.4.0_Portable.zip`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.4.0/PRRX_Internet_Download_Manager_v1.4.0_Portable.zip) | **91.81 MB** | 96,269,484 bytes | Zero-install standalone archive. Extract anywhere and launch `PRRX.InternetDownloadManager.exe`. |
| **Update Manifest** | [`manifest.json`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.4.0/manifest.json) | **0.001 MB** | 832 bytes | Update metadata and SHA-256 checksum verification for in-app automatic updating. |

---

## 🔒 SHA-256 Checksums

```text
C2984AA0DD8930984D61B3A11023A534C14D164966D141028A826DCCAF5A5846  PRRX_IDM_Setup_Online.exe
5AC2DEAD4A728C58C26F8AC57CC07B3BE31232778AC79F02B39448959DDB137E  PRRX_Internet_Download_Manager_v1.4.0_Setup.exe
AC16EDD8EE6A7FF93E82DA49A19A4B7CD409BF39F995FE4F33A6FFDB0B3AD284  PRRX_Internet_Download_Manager_v1.4.0_Portable.zip
415FE3CDCFE165BED41334F9952CF17B4172FB5FE12796197D3CDB6F6AE26D4D  manifest.json
```

---

## 🧪 Verification & Test Results
- **Unit Test Suite**: 160 of 160 tests passed (`PRRX.IDM.Tests.dll`, 0 failures, 0 skipped)
- **Build Status**: Clean Release win-x64 single-file build with zero errors
- **Zero Data Loss Protection**: Verified by unit test suite — config, SQLite databases, and download files protected
- **Inno Setup Compilers**: Offline and Online installers generated cleanly without lock contention
