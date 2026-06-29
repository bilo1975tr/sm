[Setup]
AppName=StreamMesh
AppVersion=0.0 alfa 00060
DefaultDirName={localappdata}\StreamMesh
DefaultGroupName=StreamMesh
UninstallDisplayIcon={app}\StreamMesh.exe
Compression=lzma2
SolidCompression=yes
OutputDir=Output
OutputBaseFilename=StreamMesh_Setup
PrivilegesRequired=lowest

[Files]
Source: "app\StreamMesh.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "logos\*"; DestDir: "{app}\logos"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "icons\*"; DestDir: "{app}\icons"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\StreamMesh"; Filename: "{app}\StreamMesh.exe"
Name: "{group}\Uninstall StreamMesh"; Filename: "{uninstallexe}"
Name: "{userdesktop}\StreamMesh"; Filename: "{app}\StreamMesh.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Masaüstü kısayolu oluştur"; GroupDescription: "Ek Seçenekler:"

[Run]
Filename: "{app}\StreamMesh.exe"; Description: "StreamMesh uygulamasını başlat"; Flags: nowait postinstall skipifsilent
