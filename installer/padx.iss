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
english.RestoreIcons=Move every icon from your padx pads back to the desktop?%n%nRecommended. If you choose No, the files stay in the hidden folder Desktop\.Paddock.
turkish.RestoreIcons=padx pad'lerindeki tüm ikonlar masaüstüne geri taşınsın mı?%n%nÖnerilir. Hayır derseniz dosyalar gizli Desktop\.Paddock klasöründe kalır.
english.LaunchApp=Launch padx
turkish.LaunchApp=padx'i başlat

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
// Pads keep their files in a hidden folder on the desktop. Before padx goes,
// offer to put everything back so nothing is left out of sight.
// Pass /KEEPICONS to the uninstaller to skip this (used for testing).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep <> usUninstall then
    exit;

  if (Pos('/KEEPICONS', Uppercase(GetCmdTail)) = 0) and
     (SuppressibleMsgBox(CustomMessage('RestoreIcons'), mbConfirmation, MB_YESNO, IDYES) = IDYES) then
    Exec(ExpandConstant('{app}\{#AppExe}'), '--restore-all', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // The app writes its own "start with Windows" entry; remove it with the app.
  RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'padx');
end;
