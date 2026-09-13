; ============================================================================
; Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
; PRRX IDM (TM) - Intelligent Download Manager Installer
; Watermark: PRRX-IDM-INNO-WATERMARK-SECURE-VAULT-2026
; Confidential and Proprietary - Licensed under PRRX Open Source Initiative
; ============================================================================
; PRRX Internet Download Manager - Ultra-Lightweight Web Installer (< 3 MB)
; Uses Inno Setup 6 Native Download Engine & PowerShell Automatic Extraction

#define MyAppName "PRRX Internet Download Manager"
#define MyAppVersion "1.3.0"
#define MyAppPublisher "PRRX Cooperation"
#define MyAppURL "https://github.com/prrxhex-cloud/Internet-Download-Manager"
#define MyAppExeName "PRRX.InternetDownloadManager.exe"
#define MyAppExtId "mjcomdjfgmiphnekplhmgdepbhafbjal"
#define LegacyExtId "jpnkdblibibkbnllncikdeijkbdnmpem"
#define SecondaryExtId "mjcomdjfgmiphnekplhmgdepbhafbjal"
#define PackageUrl "https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.3.0/PRRX_Internet_Download_Manager_v1.3.0_Portable.zip"

[Setup]
AppId={{D8146F25-8A11-47A1-8E2E-73E9623D7091}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Install to LocalAppData (No Administrator prompt required)
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Output Web Setup Executable (< 3 MB)
OutputDir=D:\Internet Download Manager\dist
OutputBaseFilename=PRRX_IDM_Setup_Online
SetupIconFile=D:\Internet Download Manager\src\PRRX.IDM\Assets\app_icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Only embeds the custom icon and uninstaller stub - All heavy engines downloaded live from GitHub!
Source: "D:\Internet Download Manager\src\PRRX.IDM\Assets\app_icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#MyAppExeName}"

[Registry]
; Google Chrome Extension Automatic Registration
Root: HKCU; Subkey: "Software\Google\Chrome\Extensions\{#MyAppExtId}"; ValueType: string; ValueName: "path"; ValueData: "{app}\extension"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Google\Chrome\Extensions\{#MyAppExtId}"; ValueType: string; ValueName: "version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey

; Microsoft Edge Extension Automatic Registration
Root: HKCU; Subkey: "Software\Microsoft\Edge\Extensions\{#MyAppExtId}"; ValueType: string; ValueName: "path"; ValueData: "{app}\extension"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Edge\Extensions\{#MyAppExtId}"; ValueType: string; ValueName: "version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey

; Google Chrome Native Messaging Host
Root: HKCU; Subkey: "Software\Google\Chrome\NativeMessagingHosts\com.prrx.idm"; ValueType: string; ValueName: ""; ValueData: "{localappdata}\PRRX Cooperation\NativeMessaging\com.prrx.idm.json"; Flags: uninsdeletekey

; Microsoft Edge Native Messaging Host
Root: HKCU; Subkey: "Software\Microsoft\Edge\NativeMessagingHosts\com.prrx.idm"; ValueType: string; ValueName: ""; ValueData: "{localappdata}\PRRX Cooperation\NativeMessaging\com.prrx.idm.json"; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\PRRX Cooperation\NativeMessaging"
Type: files; Name: "{tmp}\PRRX_Payload.zip"

[Code]
var
  DownloadPage: TDownloadWizardPage;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if ProgressMax > 0 then
    WizardForm.StatusLabel.Caption := Format('Downloading PRRX core files from GitHub... %d of %d KB', [Progress div 1024, ProgressMax div 1024]);
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ZipPath: String;
  PowerShellCmd: String;
  ResultCode: Integer;
begin
  if CurPageID = wpReady then begin
    DownloadPage.Clear;
    ZipPath := ExpandConstant('{tmp}\PRRX_Payload.zip');
    
    // Live cloud download from GitHub Releases
    DownloadPage.Add('{#PackageUrl}', 'PRRX_Payload.zip', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        WizardForm.StatusLabel.Caption := 'Extracting and configuring PRRX engines...';
        
        // Extract downloaded zip cleanly to installation folder
        PowerShellCmd := Format('-NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -Path ''%s'' -DestinationPath ''%s'' -Force"', [ZipPath, ExpandConstant('{app}')]);
        Exec('powershell.exe', PowerShellCmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        
        Result := True;
      except
        if DownloadPage.AbortedByUser then
          Log('Download cancelled by user.')
        else
          SuppressibleMsgBox('Failed to download release payload from GitHub. Please check your internet connection and try again.', mbCriticalError, MB_OK, IDOK);
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end else
    Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  PowerShellCmd: String;
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    // Create Native Messaging host manifest & configure Chrome/Edge browser integration
    PowerShellCmd := Format(
      '-NoProfile -ExecutionPolicy Bypass -Command "' +
      '$appDir = ''%s''; ' +
      '$exePath = Join-Path $appDir ''{#MyAppExeName}''; ' +
      '$extDir = Join-Path $appDir ''extension''; ' +
      '$natDir = Join-Path ''%s'' ''PRRX Cooperation\NativeMessaging''; ' +
      'if (-not (Test-Path $natDir)) { New-Item -ItemType Directory -Path $natDir -Force | Out-Null }; ' +
      '$manifestPath = Join-Path $natDir ''com.prrx.idm.json''; ' +
      '$jsonObj = @{ ' +
      '  name = ''com.prrx.idm''; ' +
      '  description = ''PRRX Internet Download Manager Native Messaging Host''; ' +
      '  path = $exePath; ' +
      '  type = ''stdio''; ' +
      '  allowed_origins = @(' +
      '    ''chrome-extension://{#MyAppExtId}/'', ' +
      '    ''chrome-extension://{#LegacyExtId}/'', ' +
      '    ''chrome-extension://{#SecondaryExtId}/''' +
      '  ) ' +
      '}; ' +
      '$jsonStr = $jsonObj | ConvertTo-Json -Depth 4; ' +
      'Set-Content -Path $manifestPath -Value $jsonStr -Encoding UTF8; ' +
      'New-Item -Path ''HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.prrx.idm'' -Force | Out-Null; ' +
      'Set-ItemProperty -Path ''HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.prrx.idm'' -Name ''(default)'' -Value $manifestPath -Force; ' +
      'New-Item -Path ''HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.prrx.idm'' -Force | Out-Null; ' +
      'Set-ItemProperty -Path ''HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.prrx.idm'' -Name ''(default)'' -Value $manifestPath -Force; ' +
      'New-Item -Path ''HKCU:\Software\Google\Chrome\Extensions\{#MyAppExtId}'' -Force | Out-Null; ' +
      'Set-ItemProperty -Path ''HKCU:\Software\Google\Chrome\Extensions\{#MyAppExtId}'' -Name ''path'' -Value $extDir -Force; ' +
      'Set-ItemProperty -Path ''HKCU:\Software\Google\Chrome\Extensions\{#MyAppExtId}'' -Name ''version'' -Value ''{#MyAppVersion}'' -Force; ' +
      'New-Item -Path ''HKCU:\Software\Microsoft\Edge\Extensions\{#MyAppExtId}'' -Force | Out-Null; ' +
      'Set-ItemProperty -Path ''HKCU:\Software\Microsoft\Edge\Extensions\{#MyAppExtId}'' -Name ''path'' -Value $extDir -Force; ' +
      'Set-ItemProperty -Path ''HKCU:\Software\Microsoft\Edge\Extensions\{#MyAppExtId}'' -Name ''version'' -Value ''{#MyAppVersion}'' -Force; ' +
      '"', [ExpandConstant('{app}'), ExpandConstant('{localappdata}')]);
    Exec('powershell.exe', PowerShellCmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

// Clean uninstaller with complete registry, extension, manifest, and data cleanup
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataPath: String;
  LocalDataPath: String;
  NativeMessagingDir: String;
  MsgResult: Integer;
  PowerShellCmd: String;
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    // 1. Remove all registry keys for Chrome & Edge extensions and NativeMessaging hosts
    PowerShellCmd := 
      '-NoProfile -ExecutionPolicy Bypass -Command "' +
      'Remove-Item -Path ''HKCU:\Software\Google\Chrome\Extensions\{#MyAppExtId}'' -Recurse -Force -ErrorAction SilentlyContinue; ' +
      'Remove-Item -Path ''HKCU:\Software\Microsoft\Edge\Extensions\{#MyAppExtId}'' -Recurse -Force -ErrorAction SilentlyContinue; ' +
      'Remove-Item -Path ''HKCU:\Software\Google\Chrome\Extensions\{#LegacyExtId}'' -Recurse -Force -ErrorAction SilentlyContinue; ' +
      'Remove-Item -Path ''HKCU:\Software\Microsoft\Edge\Extensions\{#LegacyExtId}'' -Recurse -Force -ErrorAction SilentlyContinue; ' +
      'Remove-Item -Path ''HKCU:\Software\Google\Chrome\Extensions\{#SecondaryExtId}'' -Recurse -Force -ErrorAction SilentlyContinue; ' +
      'Remove-Item -Path ''HKCU:\Software\Microsoft\Edge\Extensions\{#SecondaryExtId}'' -Recurse -Force -ErrorAction SilentlyContinue; ' +
      'Remove-Item -Path ''HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.prrx.idm'' -Recurse -Force -ErrorAction SilentlyContinue; ' +
      'Remove-Item -Path ''HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.prrx.idm'' -Recurse -Force -ErrorAction SilentlyContinue; ' +
      '"';
    Exec('powershell.exe', PowerShellCmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Google\Chrome\Extensions\{#MyAppExtId}');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Edge\Extensions\{#MyAppExtId}');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Google\Chrome\Extensions\{#LegacyExtId}');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Edge\Extensions\{#LegacyExtId}');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Google\Chrome\Extensions\{#SecondaryExtId}');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Edge\Extensions\{#SecondaryExtId}');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Google\Chrome\NativeMessagingHosts\com.prrx.idm');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Edge\NativeMessagingHosts\com.prrx.idm');

    // 2. Remove Native Messaging host manifest directory
    NativeMessagingDir := ExpandConstant('{localappdata}\PRRX Cooperation\NativeMessaging');
    if DirExists(NativeMessagingDir) then
      DelTree(NativeMessagingDir, True, True, True);

    // 3. User prompt for user settings / download history purge
    AppDataPath := ExpandConstant('{userappdata}\PRRX Cooperation');
    LocalDataPath := ExpandConstant('{localappdata}\PRRX Cooperation');

    if DirExists(AppDataPath) or DirExists(LocalDataPath) then
    begin
      MsgResult := SuppressibleMsgBox(
        'Do you also want to permanently delete all PRRX user settings, download history, and temporary cache?' + #13#10 + #13#10 +
        '• Click [Yes] to completely wipe all files and history from this PC.' + #13#10 +
        '• Click [No] to keep your settings in case you reinstall later.',
        mbConfirmation, MB_YESNO, IDNO);

      if MsgResult = IDYES then
      begin
        if DirExists(AppDataPath) then
          DelTree(AppDataPath, True, True, True);
        if DirExists(LocalDataPath) then
          DelTree(LocalDataPath, True, True, True);
      end;
    end;
  end;
end;
