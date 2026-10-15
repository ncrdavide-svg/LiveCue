; Inno Setup script for LiveCue.
; Build with installer\build-installer.ps1 (publishes the app, then compiles this script).
; The app is published self-contained, so target PCs need neither .NET nor VLC installed.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
; Keep this AppId fixed forever: it is how new versions recognize and upgrade an existing install.
AppId={{6C0B6F0E-7E1F-4C3B-9A51-2B8E4E7D0C11}
AppName=LiveCue
AppVersion={#AppVersion}
AppVerName=LiveCue {#AppVersion}
AppPublisher=LiveCue
DefaultDirName={autopf}\LiveCue
DefaultGroupName=LiveCue
DisableProgramGroupPage=yes
SetupIconFile=..\src\LiveCue.App\Assets\LiveCue.ico
UninstallDisplayIcon={app}\LiveCue.exe
OutputDir=..\artifacts\installer
OutputBaseFilename=LiveCue-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Install for all users (asks for admin), or let the user pick "only for me" (no admin needed).
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
; 64-bit Windows 10/11 only (x64, and ARM64 PCs through x64 emulation).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Only the x64 build of LibVLC is needed: LiveCue.exe is an x64 process.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,\libvlc\win-x86,\libvlc\win-arm64"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\LiveCue"; Filename: "{app}\LiveCue.exe"
Name: "{autodesktop}\LiveCue"; Filename: "{app}\LiveCue.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\LiveCue.exe"; Description: "{cm:LaunchProgram,LiveCue}"; Flags: nowait postinstall skipifsilent
