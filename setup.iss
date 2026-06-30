#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppName=Partner Relation Manager
AppVersion={#AppVersion}
AppPublisher=Onitio
DefaultDirName={localappdata}\Programs\PartnerRelationManager
DefaultGroupName=Partner Relation Manager
OutputDir=.
OutputBaseFilename=PartnerRelationManagerSetup-{#AppVersion}
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
UninstallDisplayIcon={app}\PartnerRelationManager.exe
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"

[Files]
Source: "bin\Release\net9.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Partner Relation Manager"; Filename: "{app}\PartnerRelationManager.exe"
Name: "{userdesktop}\Partner Relation Manager"; Filename: "{app}\PartnerRelationManager.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PartnerRelationManager.exe"; Description: "{cm:LaunchProgram,Partner Relation Manager}"; Flags: nowait postinstall skipifsilent
