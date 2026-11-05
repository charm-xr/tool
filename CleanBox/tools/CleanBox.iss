#define AppName "CleanBox"
#define AppVersion GetVersionNumbersString(PayloadDir + "\CleanBox.exe")
#define DisplayVersion Copy(AppVersion, 1, RPos(".", AppVersion) - 1)

[Setup]
AppId={{A9F6C39D-09D9-4A44-B8C1-0AB5EF945C24}
AppName={#AppName}
AppVersion={#DisplayVersion}
AppPublisher=CleanBox
AppPublisherURL=https://github.com/charm-xr/tool
AppSupportURL=https://github.com/charm-xr/tool/issues
AppUpdatesURL=https://github.com/charm-xr/tool/releases
DefaultDirName={localappdata}\Programs\CleanBox
DefaultGroupName=CleanBox
DisableDirPage=no
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayIcon={app}\CleanBox.exe
SetupIconFile={#PayloadDir}\assets\cleanbox.ico
OutputBaseFilename=CleanBox-Setup-Windows-x64
VersionInfoVersion={#AppVersion}
VersionInfoDescription=CleanBox Windows Installer
WizardStyle=modern
Compression=lzma2
SolidCompression=yes

[Languages]
Name: "zhcn"; MessagesFile: "{#ChineseMessages}"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
zhcn.FrameworkRequired=安装 CleanBox 需要 .NET Framework 4.8 或更高版本。请先安装该组件。
en.FrameworkRequired=CleanBox requires .NET Framework 4.8 or later. Install it before continuing.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\CleanBox.exe"; DestDir: "{app}"
Source: "{#PayloadDir}\CleanBox.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\使用说明.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\assets\cleanbox.ico"; DestDir: "{app}\assets"; Flags: ignoreversion

[Icons]
Name: "{group}\CleanBox"; Filename: "{app}\CleanBox.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\CleanBox"; Filename: "{app}\CleanBox.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\CleanBox.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    SuppressibleMsgBox(CustomMessage('FrameworkRequired'), mbError, MB_OK, IDOK);
end;
