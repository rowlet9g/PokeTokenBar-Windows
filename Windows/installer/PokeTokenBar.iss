#define MyAppVersion GetEnv("PTB_RELEASE_VERSION")
#define PublishDir GetEnv("PTB_PUBLISH_DIR")

#if MyAppVersion == ""
  #error PTB_RELEASE_VERSION is required
#endif

#if PublishDir == ""
  #error PTB_PUBLISH_DIR is required
#endif

#define MyAppName "PokeTokenBar"
#define MyAppExeName "PokeTokenBar.Windows.exe"
#define MyAppMutex "Local\io.github.chattymin.PokeTokenBar.Windows"

[Setup]
AppId={{23B554C2-59D9-4283-AEF1-9863587ABDFA}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=rowlet9g
AppPublisherURL=https://github.com/rowlet9g/PokeTokenBar-Windows
AppSupportURL=https://github.com/rowlet9g/PokeTokenBar-Windows/issues
AppMutex={#MyAppMutex}
DefaultDirName={localappdata}\Programs\PokeTokenBar
DefaultGroupName=PokeTokenBar
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputBaseFilename=PokeTokenBar-{#MyAppVersion}-win-x64-setup
SetupIconFile=..\..\assets\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern dynamic
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousTasks=no
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany=rowlet9g
VersionInfoDescription=PokeTokenBar for Windows installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면 바로가기 만들기"; GroupDescription: "추가 바로가기:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\PokeTokenBar"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\PokeTokenBar"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "PokeTokenBar 실행"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'PokeTokenBar');
end;
