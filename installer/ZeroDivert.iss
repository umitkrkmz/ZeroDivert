; Inno Setup script for the ZeroDivert installer.
;
; Built by the release workflow (.github/workflows/release.yml) from the
; self-contained publish output. To build it locally, run
;   dotnet publish src/ZeroDivert.Console -p:PublishProfile=win-x64
; and then compile this file with Inno Setup 6 (ISCC.exe), e.g.
;   ISCC.exe /DAppVersion=1.2.0 /DAppNumericVersion=1.2.0 installer\ZeroDivert.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
; Windows file-version form (digits only); AppVersion may carry a suffix like -rc.1.
#ifndef AppNumericVersion
  #define AppNumericVersion AppVersion
#endif
#ifndef PublishDir
  #define PublishDir "..\src\ZeroDivert.Console\bin\publish\win-x64"
#endif

#define AppName "ZeroDivert"
#define AppExe "ZeroDivert.Console.exe"
#define RepoUrl "https://github.com/umitkrkmz/ZeroDivert"

[Setup]
; Never change AppId: it is how Windows recognises later versions as upgrades of
; the same program instead of separate installs.
AppId={{F0A582E8-58F7-4DB9-B3CF-E9CB11AC0EEF}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisher=Umit Korkmaz
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#AppNumericVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; The app itself needs admin (WinDivert), and it installs into Program Files.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename={#AppName}-v{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
; Same name as AppInfo.SingleInstanceMutexName: setup and uninstall refuse to
; continue while ZeroDivert is running (it may be sitting in the tray).
AppMutex=Global\ZeroDivert.SingleInstance
; The data-folder cleanup below uses {localappdata} on purpose.
UsedUserAreasWarning=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
; WinDivert.dll loads WinDivert64.sys from its own folder, so both sit next to the exe.
Source: "{#PublishDir}\WinDivert.dll"; DestDir: "{app}"; Flags: ignoreversion
; restartreplace / uninsrestartdelete are only a fallback for when the driver
; could not be stopped (see StopOurWinDivertDriver): the file is then replaced or
; deleted on the next reboot instead of failing the install/uninstall.
Source: "{#PublishDir}\WinDivert64.sys"; DestDir: "{app}"; Flags: ignoreversion restartreplace uninsrestartdelete
; License files: ZeroDivert (GPLv3), third-party notices, and WinDivert's own license.
Source: "{#PublishDir}\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\LICENSE-WinDivert.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Inno runs postinstall entries as the original, non-elevated user by default,
; which fails with error 740 because the exe's manifest requires admin. Run it
; with setup's own (already elevated) token instead — the app runs elevated on
; every launch anyway, so this matches a normal start without a second UAC prompt.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
; Removes the "Başlangıçta Otomatik Başlat" task (AutoStartManager) so nothing is
; left in Task Scheduler pointing at deleted files. Harmless if it was never set up.
Filename: "{sys}\schtasks.exe"; Parameters: "/delete /tn ZeroDivert /f"; Flags: runhidden; RunOnceId: "DeleteAutoStartTask"

[CustomMessages]
turkish.DeleteUserData=ZeroDivert ayarlarınız, kalibrasyon profiliniz ve loglarınız da silinsin mi?%n%nYeniden kurmayı düşünüyorsanız "Hayır" deyin; kalibrasyon profiliniz korunur.%n%nNot: Uygulama içinden değiştirdiğiniz DNS ayarları kaldırma işlemiyle geri alınmaz. Eski haline döndürmek için Ayarlar → DNS Ayarları → Otomatik (DHCP) seçeneğini kaldırmadan önce kullanabilirsiniz.
english.DeleteUserData=Also delete your ZeroDivert settings, calibration profile and logs?%n%nChoose "No" if you plan to reinstall; your calibration profile will be kept.%n%nNote: DNS settings changed from inside the app are not reverted by uninstalling. To restore them, use Settings → DNS → Automatic (DHCP) before uninstalling.

[Code]
// WinDivert's kernel driver stays loaded after ZeroDivert exits — until a reboot
// or an explicit stop — and while loaded, Windows refuses to overwrite or delete
// WinDivert64.sys ("Access denied", code 5). So stop it before touching files.
//
// The service name "WinDivert" is shared by every WinDivert-based tool, so only
// stop it when it was loaded from our own install folder; a driver loaded from
// another tool's folder (e.g. GoodbyeDPI) is left alone. ZeroDivert itself is
// guaranteed closed at this point by AppMutex.
procedure StopOurWinDivertDriver();
var
  ImagePath: String;
  ResultCode: Integer;
begin
  if not RegQueryStringValue(HKLM, 'SYSTEM\CurrentControlSet\Services\WinDivert', 'ImagePath', ImagePath) then
    exit;
  if Pos(Uppercase(AddBackslash(ExpandConstant('{app}'))), Uppercase(ImagePath)) = 0 then
    exit;
  // "net stop" (unlike "sc stop") waits until the driver has actually unloaded.
  Exec(ExpandConstant('{sys}\net.exe'), 'stop WinDivert', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopOurWinDivertDriver();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usUninstall then
    StopOurWinDivertDriver();

  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\ZeroDivert');
    if DirExists(DataDir) and not UninstallSilent() then
    begin
      if MsgBox(CustomMessage('DeleteUserData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
