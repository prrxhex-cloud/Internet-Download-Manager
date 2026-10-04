# ============================================================================
# Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
# PRRX IDM (TM) - Intelligent Download Manager Engine
# Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
# Confidential and Proprietary - Licensed under PRRX Open Source Initiative
# ============================================================================

$ErrorActionPreference = 'Stop'
$version = '1.8.0'
$rootDir = (Resolve-Path "$PSScriptRoot\..").Path
$distDir = Join-Path $rootDir 'dist'
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir -Force | Out-Null }

$zipName = "PRRX_Internet_Download_Manager_v$version`_Portable.zip"
$zipPath = Join-Path $distDir $zipName
$sourceDir = Join-Path $rootDir 'publish'

if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force -ErrorAction SilentlyContinue
}

# Create a clean temporary staging directory for ultra-lean packaging
$stageDir = Join-Path $distDir "stage_v$version"
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stageDir 'bin') -Force | Out-Null

# Clean source publish directory of any previous stale assemblies (preserve bin/extension if present)
if (Test-Path $sourceDir) {
    Get-ChildItem -Path $sourceDir -File | Remove-Item -Force -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Path $sourceDir -Force | Out-Null
}

Write-Host "Publishing PRRX IDM self-contained single-file release..."
dotnet publish (Join-Path $rootDir 'src\PRRX.IDM\PRRX.IDM.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $sourceDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# 1. Copy main executable, runtime assemblies & essential native runtime components
Copy-Item (Join-Path $sourceDir 'PRRX.InternetDownloadManager.exe') $stageDir -Force
Get-ChildItem -Path $sourceDir -Filter "*.dll" | Copy-Item -Destination $stageDir -Force
Get-ChildItem -Path $sourceDir -Filter "*.json" | Copy-Item -Destination $stageDir -Force

# 2. Copy extension directory (ensure latest icons & manifest are present from source)
$publishExtDir = Join-Path $sourceDir 'extension'
if (-not (Test-Path $publishExtDir)) { New-Item -ItemType Directory -Path $publishExtDir -Force | Out-Null }
Copy-Item (Join-Path $rootDir 'extension\*') $publishExtDir -Recurse -Force
Copy-Item (Join-Path $rootDir 'extension') $stageDir -Recurse -Force

# 3. Ensure and copy all required external download engines (yt-dlp, aria2c, ffmpeg)
$publishBinDir = Join-Path $sourceDir 'bin'
if (-not (Test-Path $publishBinDir)) { New-Item -ItemType Directory -Path $publishBinDir -Force | Out-Null }

$requiredEngines = @('yt-dlp.exe', 'aria2c.exe', 'ffmpeg.exe')
foreach ($eng in $requiredEngines) {
    $enginePath = Join-Path $rootDir "bin\$eng"
    if (-not (Test-Path $enginePath)) {
        # Check fallback between C: and D: repo locations
        $altRoot = if ($rootDir -like "C:\*") { "D:\Internet Download Manager" } else { "C:\Users\sayur\Documents\GitHub\Internet-Download-Manager" }
        $altEngine = Join-Path $altRoot "bin\$eng"
        if (Test-Path $altEngine) {
            Write-Host "Restoring $eng from $altEngine..."
            Copy-Item $altEngine $enginePath -Force
        } else {
            throw "CRITICAL PACKAGING FAILURE: Required media engine '$eng' is missing at '$enginePath'. Packaging aborted to prevent defective distribution."
        }
    }
    Copy-Item $enginePath (Join-Path $publishBinDir $eng) -Force
    Copy-Item $enginePath (Join-Path $stageDir "bin\$eng") -Force
}

# 4. Clean any unwanted artifacts from stage
Get-ChildItem -Path $stageDir -Include "*.pdb", "*.xml", "*.tmp", "*.sqlite", "cookies.txt" -Recurse -Force | Remove-Item -Force

# 5. Compress using 7-Zip Ultra if available, or Compress-Archive
$sevenZipCandidates = @(
    'D:\7-Zip\7z.exe',
    "$env:ProgramFiles\7-Zip\7z.exe",
    "${env:ProgramFiles(x86)}\7-Zip\7z.exe"
)
$sevenZip = $sevenZipCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($sevenZip) {
    Write-Host "Compressing distribution package using 7-Zip LZMA Ultra ($sevenZip)..."
    & $sevenZip a -tzip -mm=LZMA -mx=9 -mfb=273 -md=64m -y $zipPath "$stageDir\*" | Out-Null
} else {
    Write-Host "Compressing distribution package using PowerShell Compress-Archive..."
    $stageItems = Get-ChildItem $stageDir | Select-Object -ExpandProperty FullName
    Compress-Archive -Path $stageItems -DestinationPath $zipPath -CompressionLevel Optimal -Force
}

# Clean staging directory
Remove-Item $stageDir -Recurse -Force

# 6. Verify and report output
$z = Get-Item $zipPath
$sizeMB = [math]::Round($z.Length / 1MB, 2)
$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash

Write-Host "======================================================"
Write-Host "Created Standalone Distribution Package:"
Write-Host "File:     $($z.Name)"
Write-Host "Size:     $sizeMB MB"
Write-Host "SHA256:   $hash"
Write-Host "Location: $($z.FullName)"
Write-Host "======================================================"

# 7. Locate and invoke Inno Setup Compiler if present
$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)

$isccExe = $null
foreach ($cand in $isccCandidates) {
    if (Test-Path $cand) {
        $isccExe = $cand
        break
    }
}

if ($isccExe) {
    Write-Host "Compiling Inno Setup Offline Installer using: $isccExe"
    & $isccExe "/O$distDir" (Join-Path $rootDir 'installer.iss')
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed for installer.iss" }

    Write-Host "Compiling Inno Setup Web Installer using: $isccExe"
    & $isccExe "/O$distDir" (Join-Path $rootDir 'web_installer.iss')
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed for web_installer.iss" }
} else {
    Write-Warning "Inno Setup Compiler (ISCC.exe) not located in standard paths."
}

# 8. Generate dist/manifest.json for in-app auto-update distribution
$manifestObj = [ordered]@{
    version      = $version
    releaseDate  = (Get-Date).ToString("yyyy-MM-dd")
    downloadUrl  = "https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v$version/$zipName"
    sha256Hash   = $hash
    releaseNotes = "PRRX IDM v${version}: Ultra-fast MTProto Telegram downloads (ParallelTransfers=16, 1MB socket buffer, 512KB part chunks), dedicated IDM Downloads Tab & Settings, Video Downloader Tab with 29+ format selector, Media Converter with live audio/video player & inspector (Internet Downloads only), Draggable Floating Grabber with format conversion, sub-150ms instant cold-start download dialog, and hardened zero-resurrection uninstaller."
    isMandatory  = $false
}

$manifestJsonPath = Join-Path $distDir 'manifest.json'
$manifestObj | ConvertTo-Json -Depth 4 | Set-Content -Path $manifestJsonPath -Encoding UTF8
Write-Host "Updated Update Manifest: $manifestJsonPath"
