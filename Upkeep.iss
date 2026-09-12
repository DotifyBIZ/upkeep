; Upkeep installer script (Inno Setup) — see docs/adr/0002-installer-innosetup-over-msix.md for
; why InnoSetup was chosen over MSIX. Wraps the self-contained `dotnet publish` output for
; win-x64 as-is (no MSIX identity, no single-file — see that ADR's Implementation Notes).
;
; Local build:
;   dotnet publish src\Upkeep.App\Upkeep.App.csproj -c Release -p:PublishProfile=win-x64
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" Upkeep.iss
;
; CI passes the real version: ISCC.exe /DMyAppVersion=1.2.3 Upkeep.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif

#define MyAppName "Upkeep"
#define MyAppPublisher "Dotify"
#define MyAppURL "https://github.com/DotifyBIZ/upkeep"
#define MyAppExeName "Upkeep.App.exe"
#define MyPublishDir "src\Upkeep.App\bin\Release\net9.0-windows10.0.22000.0\win-x64\publish"

[Setup]
; Fixed, never regenerate — Inno Setup uses this to recognize upgrades of the same app.
AppId={{7B3E9D42-5C18-4F6A-9E27-1D840B6C3A55}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
; Per-user install, no admin rights required to install — Upkeep runs on client machines whose
; Group Policy and admin rights Dotify doesn't control (same reasoning as ADR-0002). Elevation
; happens at runtime, per session, only for the actions that need it (ADR-0005) — never to install.
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 11 only (build 22000) — see docs/adr/0001-winui3-windows-11-only.md.
MinVersion=10.0.22000
OutputDir=Output
OutputBaseFilename=UpkeepSetup-{#MyAppVersion}
SetupIconFile=src\Upkeep.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
LicenseFile=LICENSE
; Unsigned for now — no hard install block under InnoSetup (unlike MSIX), just a SmartScreen
; warning until download reputation builds. See ADR-0002's "Consequences" section.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
