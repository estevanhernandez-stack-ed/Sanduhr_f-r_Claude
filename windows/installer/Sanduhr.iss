; Inno Setup script for Sanduhr für Claude.
; Requires Inno Setup 6+. Run: iscc Sanduhr.iss

#define MyAppName "Sanduhr für Claude"
#define MyAppShortName "Sanduhr"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "626Labs"
#define MyAppURL "https://github.com/estevanhernandez-stack-ed/Sanduhr_f-r_Claude"
#define MyAppExeName "Sanduhr.exe"

[Setup]
AppId={{7B4E0E2F-8F2B-4A12-9C9A-626LABSSANDUHR}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppShortName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=
OutputDir=..\build
OutputBaseFilename=Sanduhr-Setup-v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
WizardImageFile=banner.bmp
WizardSmallImageFile=banner-small.bmp

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startmenu"; Description: "Create a Start Menu shortcut"; GroupDescription: "Shortcuts:"; Flags: checkedonce
Name: "desktop";   Description: "Create a Desktop shortcut";   GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "autostart"; Description: "Start {#MyAppName} automatically when you sign in"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\dist\Sanduhr\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenu
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktop

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppShortName}"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Always clear Credential Manager entries on uninstall. Best-effort -- failures non-fatal.
Filename: "{cmd}"; Parameters: "/c cmdkey.exe /delete:com.626labs.sanduhr"; RunOnceId: "ClearCreds"; Flags: runhidden

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
var
  RemoveDataCheckbox: TNewCheckBox;

procedure InitializeUninstallProgressForm();
var
  Page: TSetupForm;
begin
  Page := UninstallProgressForm;
  RemoveDataCheckbox := TNewCheckBox.Create(Page);
  RemoveDataCheckbox.Parent := Page;
  RemoveDataCheckbox.Left := 8;
  RemoveDataCheckbox.Top := Page.Height - 80;
  RemoveDataCheckbox.Width := Page.Width - 24;
  RemoveDataCheckbox.Height := 20;
  RemoveDataCheckbox.Caption := 'Also remove my settings and history';
  RemoveDataCheckbox.Checked := False;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppData: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if Assigned(RemoveDataCheckbox) and RemoveDataCheckbox.Checked then
    begin
      AppData := ExpandConstant('{userappdata}\Sanduhr');
      DelTree(AppData, True, True, True);
    end;
  end;
end;
