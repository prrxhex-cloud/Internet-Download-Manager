# 🚀 PRRX Internet Download Manager — Release Notes v1.7.0

**Release Tag**: `v1.7.0`  
**Target Branch**: `main`  
**Date**: September 21, 2026  
**Target Platform**: Windows 10 / Windows 11 (x64)  
**Publisher**: PRRX Cooperation  

---

## 🌟 What's New in Version 1.7.0

### ⚡ 1. Telegram 32-Stream Turbo Acceleration Engine
- **Direct CDN Stream Resolution**: Media forwarded from public channels and posts (e.g. `NecflixsLK` and `t.me/channel/id`) automatically resolves into direct CDN stream URLs.
- **Engine Unification**: Downloads execute through the full 32-stream segmented downloader rather than the legacy single-stream plain chunk list.
- **Visual Feedback**: Real-time multi-connection blue block progress grid, transfer speeds, ETA estimation, and byte counters.
- **0 B/s Bug Elimination**: Direct asynchronous stream resolution inside `DownloadFileInfoViewModel` ensures endpoints are fully validated and probed before download initiation.

### 📥 2. Closed-App Queuing & Startup Task Hydration
- **Cloudflare D1 Persistence**: Files sent to `@PRRX_IDM_Bot` while the IDM client is closed are stored in Cloudflare D1 Edge SQLite and immediately hydrated when IDM launches.
- **Minimize-to-Tray Background Sync**: IDM maintains lightweight background synchronization while minimized to the Windows system tray.
- **One-Click Pairing**: Effortless desktop-to-bot pairing via 6-character authentication codes (`PRRX-XXXX`).

### 🛡️ 3. Server & Database Security Hardening
- **Webhook Secret Token Verification**: Mandatory enforcement of `X-Telegram-Bot-Api-Secret-Token` on Telegram webhook endpoints; requests with missing or invalid tokens are rejected immediately with `401 Unauthorized`.
- **Parameterized SQL Enforcement**: 100% parameterized queries across all Cloudflare D1 interactions to prevent SQL injection vulnerabilities.
- **Dual-Tier Rate Limiting**: Independent IP-address and Client-ID rate limiters to protect task polling and pairing endpoints against abuse.
- **Automated Expired Data GC**: Background cleanup routine purges delivered tasks and expired pairing codes older than 1 hour.

### 🔒 4. Client-Side Defense & System Protection
- **Windows DPAPI Encryption**: Client pairing identifiers (`TelegramClientId`) are hardware-encrypted on disk using Windows Data Protection APIs.
- **Strict Path & Device Sanitization**: Robust protection against directory traversal (`../`, `..\`) and legacy DOS device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`).
- **NTFS Mark-of-the-Web**: Automatic attachment of `Zone.Identifier` (ZoneId=3) alternate data streams to downloaded files for Windows Defender / SmartScreen validation.
- **Isolated Staging Storage**: Chunk assembly is performed in isolated application data directories to prevent accidental partial file exposure.

### 🧩 5. Browser Extension v1.7.0 Synchronization
- **Manifest Version Alignment**: Updated manifest to `1.7.0` with dynamic version reflection in extension popup.
- **Packaging Pipeline Hardening**: Packaging scripts updated to prevent stale extension artifacts from contaminating release bundles.

---

## 🌟 What's New in Version 1.5.0

### ☁️ 1. Cloud Install vs. Default Direct Download Mode
- **Cloud Install (Accelerated Routing)**: Fast media and multi-part downloads routed through PRRX Cloudflare Edge servers and CDN nodes with zero traffic congestion or ISP speed throttling.
- **Default Install (Direct Connection)**: Standard direct socket connection to origin servers.
- **In-App Toggle & Traffic Badge**: Easy toggle in Settings tab with live server condition badge (`⚡ Cloud Server: Normal (Fast)`).

### ⚡ 2. Automatic Server Congestion Circuit Breaker
- **Real-Time Latency & Health Evaluation**: Continuously measures server round-trip latency and edge status.
- **Zero-Friction Auto-Failover**: If the cloud edge encounters congestion (>2500ms latency), server errors, or instability, downloads automatically fall over to Default Direct mode without throwing download errors.
- **Instant Restore**: Once edge traffic normalizes, users can seamlessly switch back to Cloud Install.

### 🎯 3. Highest Process & Network Scheduling Priority by Default
- **High Priority Allocation**: Automatically elevates PRRX IDM process and network I/O threads to `ProcessPriorityClass.High` on startup, preventing network buffer starvation and packet loss.
- **Graceful Fallback**: Silently falls back to `AboveNormal` if running in restricted environments without elevation.
- **In-App Settings Selector**: Users can customize process priority anytime (`High`, `Above Normal`, `Normal`).

### 📜 4. Official EULA & Agreement in Setup Installers
- **Embedded EULA**: Official 2026 PRRX Cooperation End User License Agreement & Terms of Service (`LICENSE.txt`) integrated directly into the Inno Setup offline wizard and web installer.

### 🚀 5. 1 MB High-Speed Fiber Chunk Buffering
- **Expanded Buffers**: Chunk buffer size increased to **1048576 bytes (1 MB)** paired with **8 MB HTTP/2 initial stream windows** and up to 64 parallel sockets for gigabit bandwidth saturation.
- **Turbo Media Extraction**: Video, audio, and thumbnail extractors employ 16–32 parallel fragment pipelines in Cloud mode.

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
- **Live Edge API**: Connected the browser extension and desktop updater with the live serverless Cloudflare Workers edge API.
- **Cloudflare D1 Database**: Integrated remote database connectivity for real-time domain reputation inspection, malware threat defense, server health metrics, and instant update catalog synchronization.
- **Dual-Status Browser Extension**: Extension popup now features a real-time status matrix showing dual connectivity for both the Desktop Engine Bridge and the Cloudflare Edge API.
- **Host Permissions**: Configured extension manifest permissions for secure cloud edge access.

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
5B21E60DFA8211CC45822E419F4D2E4641FBA5C36D0B4641AA42079530859A74  PRRX_Internet_Download_Manager_v1.4.0_Setup.exe
B176FEB5FF2EC6B7DADE8161033F2CBD64675553A6A7AB679E79057035956C64  PRRX_Internet_Download_Manager_v1.4.0_Portable.zip
8D21C1CA6655F18D8975D5EF57E203E99A54FB2588BF7AEFDBB92B1D605CD79F  manifest.json
```

---

## 🧪 Verification & Test Results
- **Unit Test Suite**: 160 of 160 tests passed (`PRRX.IDM.Tests.dll`, 0 failures, 0 skipped)
- **Build Status**: Clean Release win-x64 single-file build with zero errors
- **Zero Data Loss Protection**: Verified by unit test suite — config, SQLite databases, and download files protected
- **Inno Setup Compilers**: Offline and Online installers generated cleanly without lock contention
