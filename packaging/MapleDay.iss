#ifndef PackageVersion
  #error PackageVersion must be supplied by Build-Installer.ps1
#endif
#ifndef ReleaseDirectory
  #error ReleaseDirectory must be supplied by Build-Installer.ps1
#endif

[Setup]
AppId={{07CD2C71-5D46-4A1F-A330-4722417E9042}
AppName=메요일
AppVersion={#PackageVersion}
LicenseFile=..\LICENSE
AppPublisher=MapleYoil
AppPublisherURL=https://github.com/MapleYoil/MapleDay
AppSupportURL=https://github.com/MapleYoil/MapleDay/issues
AppUpdatesURL=https://github.com/MapleYoil/MapleDay/releases
DefaultDirName={localappdata}\Programs\MapleDay
DefaultGroupName=메요일
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputBaseFilename=MapleDay-Setup-{#PackageVersion}-x64
SetupIconFile=..\src\MapleDay\Assets\Branding\mapleday.ico
UninstallDisplayIcon={app}\MapleDay.exe
UninstallFilesDir={app}\App\Uninstall
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 바로 가기 만들기"; Flags: unchecked

[Files]
Source: "{#ReleaseDirectory}\MapleDay.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseDirectory}\App\*"; DestDir: "{app}\App"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\메요일"; Filename: "{app}\MapleDay.exe"
Name: "{autodesktop}\메요일"; Filename: "{app}\MapleDay.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MapleDay.exe"; Description: "메요일 실행"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'MapleDay', Command) then
      if CompareText(Command, '"' + ExpandConstant('{app}\MapleDay.exe') + '" --startup') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'MapleDay');
end;
