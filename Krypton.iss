; ============================================================
;  Krypton Setup Script — Inno Setup
;  Packages the self-contained win-x64 publish output produced by:
;    dotnet publish Krypton\Krypton.csproj -c Release -r win-x64 --self-contained true -o publish
;
;  IMPORTANT: this script assumes the "publish" folder sits directly
;  next to this .iss file. Adjust SourcePath below if yours lives
;  elsewhere (e.g. Krypton\publish or a different relative path).
; ============================================================

#define MyAppName "Krypton"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "CodeVynix"
#define MyAppURL "https://github.com/CodeVynix/Krypton"
#define MyAppExeName "Krypton.exe"
#define SourcePath "publish"

[Setup]
AppId={{B7E4C9A2-6F1D-4A8E-9C3B-1D5E7F2A8B90}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=LICENSE
OutputBaseFilename=KryptonSetup-{#MyAppVersion}
SetupIconFile=Krypton\krypton.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

; CefSharp is native x64 only — do not offer this installer on x86/ARM
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Uninstall entry shows the app icon instead of a generic one
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Recursive copy of the ENTIRE publish output — CefSharp needs every loose
; file (libcef.dll, icudtl.dat, *.pak, the locales\ folder, etc.), not just
; the .exe. Do not cherry-pick individual files here.
Source: "{#SourcePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up CefSharp's per-user cache so a reinstall starts fresh
; (matches the CachePath/RootCachePath under LocalAppData from Program.cs)
Type: filesandordirs; Name: "{localappdata}\{#MyAppName}"
