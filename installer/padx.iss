; padx installer (Inno Setup 6). Built by build-release.ps1, which passes the
; version from Directory.Build.props: ISCC /DAppVersion=2.0.0 installer\padx.iss

#define AppName "padx"
#define AppExe "padx.exe"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif

[Setup]
AppId={{8F3C2A5E-6B1D-4C7A-9E2F-5A1B3C4D7E90}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=axzelx
AppPublisherURL=https://github.com/serkanyardim/padx
AppSupportURL=https://github.com/serkanyardim/padx/issues
AppUpdatesURL=https://github.com/serkanyardim/padx/releases
VersionInfoVersion={#AppVersion}

; Per-user install: no administrator prompt.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.18362

; Close a running padx before replacing it (matches the app's single-instance mutex).
AppMutex=Local\Paddock.SingleInstance
CloseApplications=yes

LicenseFile=..\LICENSE
SetupIconFile=..\assets\padx.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir=..\artifacts
OutputBaseFilename=padx-setup-v{#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[CustomMessages]
english.LaunchApp=Launch padx
turkish.LaunchApp=padx'i başlat

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
// Pad items stay on the desktop, so there is nothing to ask. Before padx goes
// it tidies up after itself: Windows' desktop icons back on, and anything an
// old version (before 3.0) left in the hidden Desktop\.Paddock folder back
// onto the desktop. Pass /KEEPICONS to the uninstaller to skip this (testing).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep <> usUninstall then
    exit;

  if Pos('/KEEPICONS', Uppercase(GetCmdTail)) = 0 then
    Exec(ExpandConstant('{app}\{#AppExe}'), '--restore-all', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // The app sets up its own "start with Windows" (a logon task, or a Run
  // value where tasks are not allowed); remove both with the app.
  RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'padx');
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN "padx - ' + GetUserNameString + '" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
