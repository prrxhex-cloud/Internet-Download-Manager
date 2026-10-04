$srcDir = (Resolve-Path "$PSScriptRoot\..").Path
$targetDir = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'GitHub\Internet-Download-Manager'

# Copy solution file, source code, extension, installer, and test projects
if (Test-Path "$srcDir\PRRX.InternetDownloadManager.sln") {
    Copy-Item -Path "$srcDir\PRRX.InternetDownloadManager.sln" -Destination "$targetDir\PRRX.InternetDownloadManager.sln" -Force
}
Copy-Item -Path "$srcDir\src" -Destination $targetDir -Recurse -Force
if (Test-Path "$srcDir\tests") {
    Copy-Item -Path "$srcDir\tests" -Destination $targetDir -Recurse -Force
}
Copy-Item -Path "$srcDir\extension" -Destination $targetDir -Recurse -Force
Copy-Item -Path "$srcDir\web_installer.iss" -Destination "$targetDir\web_installer.iss" -Force
if (Test-Path "$srcDir\installer.iss") {
    Copy-Item -Path "$srcDir\installer.iss" -Destination "$targetDir\installer.iss" -Force
}
if (Test-Path "$srcDir\README.md") {
    Copy-Item -Path "$srcDir\README.md" -Destination "$targetDir\README.md" -Force
}
if (Test-Path "$srcDir\RELEASE_NOTES.md") {
    Copy-Item -Path "$srcDir\RELEASE_NOTES.md" -Destination "$targetDir\RELEASE_NOTES.md" -Force
}
if (Test-Path "$srcDir\LICENSE.txt") {
    Copy-Item -Path "$srcDir\LICENSE.txt" -Destination "$targetDir\LICENSE.txt" -Force
}
if (Test-Path "$srcDir\.gitignore") {
    Copy-Item -Path "$srcDir\.gitignore" -Destination "$targetDir\.gitignore" -Force
}
if (Test-Path "$srcDir\bin") {
    if (-not (Test-Path "$targetDir\bin")) { New-Item -ItemType Directory -Path "$targetDir\bin" -Force | Out-Null }
    Copy-Item -Path "$srcDir\bin\*" -Destination "$targetDir\bin" -Recurse -Force
}

# Clean build artifacts in git folder (only under src and tests, plus publish and dist; preserve root bin)
Get-ChildItem -Path "$targetDir\src", "$targetDir\tests" -Include 'bin','obj' -Recurse -Directory -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
Get-ChildItem -Path "$targetDir" -Include 'publish','dist' -Directory -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force

Write-Host "Synchronized solution, source code, extension, and installer to GitHub repository directory!"

$gitCmd = Get-Command git -ErrorAction SilentlyContinue
$gitExe = if ($gitCmd) { $gitCmd.Source } else { $null }
if (-not $gitExe) {
    $foundGit = Get-ChildItem -Path "$env:LOCALAPPDATA\GitHubDesktop" -Filter "git.exe" -Recurse -Depth 5 -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like "*\cmd\git.exe" } | Select-Object -First 1
    if ($foundGit) { $gitExe = $foundGit.FullName }
}
if (-not $gitExe -and (Test-Path "$env:ProgramFiles\Git\cmd\git.exe")) {
    $gitExe = "$env:ProgramFiles\Git\cmd\git.exe"
}

if ($gitExe) {
    Write-Host "Local Git detected: $gitExe"
    & $gitExe -C $targetDir add -A
    $status = & $gitExe -C $targetDir status --short
    if ($status) {
        Write-Host "Staging and committing repository updates..."
        & $gitExe -C $targetDir commit -m "release(v1.8.0): ultra-fast Telegram MTProto download acceleration, dedicated IDM tab, 29+ video formats, internet-only converter, draggable floating grabber, sub-150ms cold start, zero-resurrection uninstaller"
    } else {
        Write-Host "No unstaged changes in repository."
    }
    Write-Host "Pushing updates to remote origin..."
    try {
        $env:GIT_TERMINAL_PROMPT = "0"
        & $gitExe -C $targetDir push origin main
    } catch {
        Write-Warning "git push notice: $_"
    } finally {
        $env:GIT_TERMINAL_PROMPT = "1"
    }
    & $gitExe -C $targetDir status
} else {
    Write-Warning "git.exe could not be located on this machine."
}
