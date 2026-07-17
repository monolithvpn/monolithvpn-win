#ifndef PublishDir
  #define PublishDir "..\bin\Release\publish"
#endif
#ifndef AppVersion
  #define AppVersion "1.2.1"
#endif

#define AppName "MonolithVPN"
#define AppPublisher "MonolithVPN"
#define AppExeName "MonolithVPN.exe"

[Setup]
AppId={{B4E3B9B0-6D2A-4A3E-9C4E-2F6C6D6E7B10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=..\bin\Release
OutputBaseFilename=MonolithVPN-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
DisableProgramGroupPage=yes
WizardStyle=modern
SetupIconFile=..\Assets\app.ico
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#PublishDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: postinstall nowait shellexec

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\MonolithVPN"

[Code]
const
  DotNetDesktopRuntimeUrl = 'https://aka.ms/dotnet/9.0/windowsdesktop-runtime-win-x64.exe';
  WireGuardInstallerUrl = 'https://download.wireguard.com/windows-client/wireguard-installer.exe';

function IsDotNetDesktopRuntimeInstalled(): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetSubkeyNames(HKEY_LOCAL_MACHINE,
       'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App',
       Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 2) = '9.' then
      begin
        Result := True;
        Exit;
      end;
  end;
end;

function IsWireGuardInstalled(): Boolean;
begin
  Result := FileExists(ExpandConstant('{pf}\WireGuard\wireguard.exe'))
    or FileExists(ExpandConstant('{pf32}\WireGuard\wireguard.exe'));
end;

function DownloadFile(Url, DestPath: String): Boolean;
var
  ResultCode: Integer;
  Command: String;
begin
  Command := '-NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference=''SilentlyContinue''; Invoke-WebRequest -Uri ''' + Url + ''' -OutFile ''' + DestPath + '''"';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Command, '',
    SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) and FileExists(DestPath);
end;

procedure InstallPrerequisites();
var
  TempFile: String;
  ResultCode: Integer;
begin
  if not IsDotNetDesktopRuntimeInstalled() then
  begin
    WizardForm.StatusLabel.Caption := 'Downloading the .NET Desktop Runtime...';
    TempFile := ExpandConstant('{tmp}\windowsdesktop-runtime.exe');
    if DownloadFile(DotNetDesktopRuntimeUrl, TempFile) then
    begin
      WizardForm.StatusLabel.Caption := 'Installing the .NET Desktop Runtime...';
      if not Exec(TempFile, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
        MsgBox('Couldn''t install the .NET Desktop Runtime automatically. ' + #13#10 +
          'MonolithVPN needs it to run - get it from https://dotnet.microsoft.com/download/dotnet/9.0 if launching the app fails.',
          mbInformation, MB_OK);
    end
    else
      MsgBox('Couldn''t download the .NET Desktop Runtime (no internet access during setup?). ' + #13#10 +
        'MonolithVPN needs it to run - get it from https://dotnet.microsoft.com/download/dotnet/9.0 if launching the app fails.',
        mbInformation, MB_OK);
  end;

  if not IsWireGuardInstalled() then
  begin
    WizardForm.StatusLabel.Caption := 'Downloading WireGuard for Windows...';
    TempFile := ExpandConstant('{tmp}\wireguard-installer.exe');
    if DownloadFile(WireGuardInstallerUrl, TempFile) then
    begin
      WizardForm.StatusLabel.Caption := 'WireGuard for Windows needs installing too - continue in its setup window.';
      Exec(TempFile, '', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode);
      if not IsWireGuardInstalled() then
        MsgBox('WireGuard for Windows still isn''t detected. MonolithVPN won''t be able to connect until it''s installed - ' +
          'get it from wireguard.com/install, or use the "Install WireGuard" button in the app''s Settings tab.',
          mbInformation, MB_OK);
    end
    else
      MsgBox('Couldn''t download WireGuard for Windows (no internet access during setup?). ' + #13#10 +
        'Get it from wireguard.com/install, or use the "Install WireGuard" button in the app''s Settings tab.',
        mbInformation, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallPrerequisites();
end;
