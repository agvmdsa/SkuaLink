; SkuaLink installer (Inno Setup 6).
;
; Built by .github/workflows/release.yml after the single-file publish, with the version from the tag:
;   iscc /DMyAppVersion=0.2.0 /DMyAppVersionLabel=0.2.0 installer\SkuaLink.iss
; The publish folder (out\publish) holds SkuaLink.exe, renamed from AqwCompanion.exe by the workflow.
;
; SkuaLink runs as the normal user; the installer asks for administrator rights only because it installs into Program Files.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef MyAppVersionLabel
  #define MyAppVersionLabel MyAppVersion
#endif
#define MyAppName "SkuaLink"
#define MyAppExe "SkuaLink.exe"
#define MyAppUrl "https://github.com/agvmdsa/SkuaLink"
; The folder that already holds the settings and saved data of earlier versions (the assembly kept its first name).
#define MyDataFolder "AqwCompanion"

[Setup]
; Never change this id: it is how Windows knows later versions replace this one.
AppId={{BB950C7C-C50F-463F-9FAD-8EBBECA33840}
AppName={#MyAppName}
AppVersion={#MyAppVersionLabel}
VersionInfoVersion={#MyAppVersion}
AppPublisher=agvmdsa
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Program Files needs administrator rights to write; the app itself runs as the normal user.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=SkuaLink-Setup-v{#MyAppVersionLabel}
SetupIconFile=..\src\AqwCompanion.App\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; The app hides in the notification area when its window is closed, so ask the player to exit it from there before updating.
AppMutex=Local\{#MyDataFolder}
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\out\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
; runasoriginaluser: the setup runs elevated, the app must not.
Filename: "{app}\{#MyAppExe}"; Description: "Start {#MyAppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; "Start with Windows" in the app's settings creates this scheduled task.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""{#MyAppName}"" /F"; Flags: runhidden; RunOnceId: "RemoveSkuaLinkTask"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\{#MyDataFolder}');
    if (not UninstallSilent) and DirExists(DataDir) then
      if MsgBox('Also remove what SkuaLink saved on this computer (settings, saved characters, logs)?',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
