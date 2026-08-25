; Inno Setup Script for StreamMesh
#ifndef AppVersion
#define AppVersion "0.1.0"
#endif

[Setup]
AppId={{D8F91A2B-3C4E-5F6A-7B8C-9D0E1F2A3B4C}
AppName=StreamMesh
AppVersion={#AppVersion}
AppPublisher=StreamMesh
DefaultDirName={localappdata}\Programs\StreamMesh
DefaultGroupName=StreamMesh
OutputBaseFilename=StreamMesh-Setup-v{#AppVersion}
SetupIconFile=logos\StreamMesh_Icon.ico
OutputDir=.
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
DisableProgramGroupPage=yes
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\logos\StreamMesh_Icon.ico

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "logos\*"; DestDir: "{app}\logos"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\StreamMesh"; Filename: "{app}\StreamMesh.exe"; IconFilename: "{app}\logos\StreamMesh_Icon.ico"
Name: "{userdesktop}\StreamMesh"; Filename: "{app}\StreamMesh.exe"; IconFilename: "{app}\logos\StreamMesh_Icon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\StreamMesh.exe"; Description: "{cm:LaunchProgram,StreamMesh}"; Flags: nowait postinstall skipifsilent
