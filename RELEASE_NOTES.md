# 🚀 PRRX Internet Download Manager — Release Notes v1.3.0

**Release Tag**: `v1.3.0`  
**Target Branch**: `main`  
**Date**: September 13, 2026  
**Target Platform**: Windows 10 / Windows 11 (x64)  
**Publisher**: PRRX Cooperation  

---

## 🌟 What's New in Version 1.3.0

### 📜 1. Interactive Changelog & Version History in Settings
- **In-App Version History**: Added a dedicated, Fluent 2 styled **Changelog & Version History** card inside **Tab 4 (Settings & Updates)** with interactive expanders.
- **Complete Release Archive**: Inspect full historical changes for **v1.3.0**, **v1.2.0**, **v1.1.0**, and **v1.0.0** directly within the application without opening a web browser.
- **Categorized Release Notes**: Color-coded badges categorizing improvements by:
  - 🚀 **Features** (Accent Blue)
  - 🛠️ **Fixes** (Forest Green)
  - ⚡ **Performance** (Amber)
  - 🛡️ **Security** (Purple / Red)

### 🧹 2. Automatic Post-Update Cleanup (Zero Data Loss Guaranteed)
- **Automatic Startup Sweep**: Runs unobtrusively in the background on application startup to detect and purge leftover update artifacts.
- **Artifacts Cleaned**:
  - Temporary update staging folders (`%TEMP%\PRRX_IDM_Update_*`, `PRRX_Update_*`).
  - Leftover update `.zip` packages in `%TEMP%`.
  - Inno Setup temporary setup extraction directories (`is-*.tmp`).
  - Stale binary swap files (`*.bak`, `*.old`, `*.tmp`, `*.swap`) in the application installation directory.
  - Backup snapshots older than 7 days.
- **Guaranteed Zero Data Loss**: Strict protection whitelist ensures all user configurations (`config.json`, `settings.json`), download database & history (`download_history.json`, `.sqlite`, `.db`), encryption keys (`master.key`), session cookies (`cookies.txt`), and all files in user download directories (`Downloads`, `Documents`, `Desktop`) are 100% untouched.
- **Manual Cleanup Action**: Added a **Clean Update Cache** button in Settings with a real-time status banner displaying disk space reclaimed.

### 🔄 3. Resilient Online Auto-Update Engine
- **Streaming Retry Loop**: Implemented a 3-attempt automated retry loop with exponential backoff (`1s`, `2s`, `4s`) for update package downloads.
- **High-Throughput Buffering**: Upgraded to 64 KB stream chunking with live speed metrics, progress tracking, and chunk-by-chunk validation.
- **Integrity Validation**: Computes and matches SHA-256 hashes against update manifests before extraction, preventing incomplete or corrupted update payloads from being applied.
- **Robust Update Applicator**: Refined `apply_update.ps1` script to wait for process termination, safely swap executable files, purge temporary update archives, and launch the newly installed version.

### 🛡️ 4. Enterprise Hardening & Quality Assurance
- **IP Watermarking**: Embedded PRRX Cooperation intellectual property watermarks across all source headers, installers, and manifests.
- **Comprehensive Test Suite**: 65 unit tests covering update mechanics, changelog models, user data protection, and download acceleration engines (100% pass rate).

---

## 📦 Distribution Packages & Downloads

| Package | Filename | Size (MB) | Exact Size (Bytes) | Description |
| :--- | :--- | :--- | :--- | :--- |
| **Online Web Setup** *(Recommended)* | [`PRRX_IDM_Setup_Online.exe`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.3.0/PRRX_IDM_Setup_Online.exe) | **1.99 MB** | 2,081,817 bytes | Ultra-compact web installer that streams and installs the latest components with zero administrator elevation required. |
| **Full Offline Setup** | [`PRRX_Internet_Download_Manager_v1.3.0_Setup.exe`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.3.0/PRRX_Internet_Download_Manager_v1.3.0_Setup.exe) | **88.09 MB** | 92,364,373 bytes | Complete self-contained setup package with embedded 32-stream media engine and browser integration for air-gapped systems. |
| **Portable Package** | [`PRRX_Internet_Download_Manager_v1.3.0_Portable.zip`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.3.0/PRRX_Internet_Download_Manager_v1.3.0_Portable.zip) | **91.74 MB** | 96,192,935 bytes | Zero-install standalone archive. Extract anywhere and launch `PRRX.InternetDownloadManager.exe`. |
| **Update Manifest** | [`manifest.json`](https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.3.0/manifest.json) | **0.001 MB** | 866 bytes | Update metadata and checksum verification for in-app automatic updating. |

---

## 🔒 SHA-256 Checksums

```text
0FAC1876686EAA7E01BA219C2118E70406F5B98209E9B7311FDC3562B87523A9  PRRX_IDM_Setup_Online.exe
0A2CAABA3D050846DD7FCB2EA053E0B9A91CF4396E074615556F7691BEF624A5  PRRX_Internet_Download_Manager_v1.3.0_Setup.exe
1E4B6BBAA340B03F0726A8DBE23D9F13AC0E6C36373A06BD1054DA737339C823  PRRX_Internet_Download_Manager_v1.3.0_Portable.zip
462989336FFFA5858C4BAB92E880E680FF662B83C2DE6FB16FDA16339AD359E5  manifest.json
```

---

## 🧪 Verification & Test Results
- **Unit Test Suite**: 65 of 65 tests passed (`PRRX.IDM.Tests.dll`)
- **Build Status**: Clean Release win-x64 single-file build with zero errors
- **Zero Data Loss Protection**: Verified by unit test suite — config, SQLite databases, and download files protected
- **Inno Setup Compilers**: Offline and Online installers generated cleanly without lock contention
