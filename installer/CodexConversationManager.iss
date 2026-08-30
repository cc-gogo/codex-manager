; Inno Setup installer for Codex Conversation Manager.
#define MyAppName "Codex Manager"
#define MyAppVersion "0.2.3"
#define MyAppPublisher "Codex Conversation Manager"
#define MyAppExeName "CodexConversationManager.App.exe"

[Setup]
AppId={{B6C5F20B-92D2-4C97-A7D7-BE2E55F2D091}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\CodexConversationManager
DefaultGroupName={#MyAppName}
OutputDir=..\installer-output
OutputBaseFilename=CodexConversationManager-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\assets\codex-manager.ico

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\assets\codex-manager.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "install-mode.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\codex-manager.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\codex-manager.ico"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent
