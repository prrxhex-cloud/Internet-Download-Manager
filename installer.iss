; Script generated for PRRX Internet Download Manager
; Professional Inno Setup Script

#define MyAppName "PRRX Internet Download Manager"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "PRRX Cooperation"
#define MyAppURL "https://github.com/PRRX-Cooperation/Internet-Download-Manager"
#define MyAppExeName "PRRX.InternetDownloadManager.exe"

[Setup]
; Basic Application Info
AppId={{D8146F25-8A11-47A1-8E2E-73E9623D7091}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Installation Paths
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; Permissions: Supports both Standard User and Administrator
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Output Configuration
OutputDir=D:\Internet Download Manager\dist
OutputBaseFilename=PRRX_Internet_Download_Manager_v1.0.0_Setup
SetupIconFile=D:\Internet Download Manager\src\PRRX.IDM\Assets\app_icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; Modern Wizard Styling & Ultra Compression
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
; Main Executable
Source: "D:\Internet Download Manager\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; Bundled Media Engines and Tools
Source: "D:\Internet Download Manager\publish\bin\*"; DestDir: "{app}\bin"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
