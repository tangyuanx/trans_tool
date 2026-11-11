[Setup]
AppId={{6B8D02BA-67EF-4D4C-8AD1-1CAD22649362}
AppName=Trans Tool
AppVersion={#AppVersion}
AppPublisher=tangyuanx
AppPublisherURL=https://github.com/tangyuanx/trans_tool
DefaultDirName={localappdata}\Programs\Trans Tool
DefaultGroupName=Trans Tool
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0.19045
UsePreviousAppDir=yes
AppMutex=Local\TransTool-6B8D02BA-67EF-4D4C-8AD1-1CAD22649362
CloseApplications=no
UninstallDisplayIcon={app}\TransTool.exe
OutputDir={#OutputPath}
OutputBaseFilename=TransTool-Windows-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "{#ApplicationPath}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Trans Tool"; Filename: "{app}\TransTool.exe"

[Run]
Filename: "{app}\TransTool.exe"; Description: "Launch Trans Tool"; Flags: nowait postinstall skipifsilent
