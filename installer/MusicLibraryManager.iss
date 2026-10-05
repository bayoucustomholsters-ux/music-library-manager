; Inno Setup script for Music Library Manager v1.0.0
; Compile on Windows with Inno Setup 6+ after publishing win-x64 to ..\publish\win-x64

#define MyAppName "Music Library Manager"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Bayou Custom Holsters"
#define MyAppURL "https://github.com/bayoucustomholsters/music-library-manager"
#define MyAppExeName "MusicLibraryManager.exe"

[Setup]
AppId={{A7C3E8F1-9B2D-4E5A-8C1F-0D6B9A4E2F31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\MusicLibraryManager
DefaultGroupName={#MyAppName}
OutputDir=..\dist
OutputBaseFilename=MusicLibraryManager-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
