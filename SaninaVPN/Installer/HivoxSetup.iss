; Inno Setup 6 script for Hivox (native ~90-95 MB installer without .NET runtime overhead)
; Compile with: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" Installer\HivoxSetup.iss

#define MyAppName "Hivox"
#define MyAppVersion "2.1.1"
#define MyAppPublisher "Hivox"
#define MyAppExeName "Hivox.exe"

[Setup]
AppId={{8D6B2A1E-9C41-4F2A-8C19-4F0D7E21B3A4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\github-release
OutputBaseFilename=Hivox-Setup-v{#MyAppVersion}-win-x64
SetupIconFile=..\Assets\Hivox.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\github-release\Hivox-Portable-v{#MyAppVersion}-win-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
