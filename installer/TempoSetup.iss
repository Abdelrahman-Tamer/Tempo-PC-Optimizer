; Tempo PC Optimizer - Official Inno Setup 6 Script
#define MyAppName "Tempo PC Optimizer"
#define MyAppVersion "2.2.5"
#define MyAppPublisher "Eng. Abdelrahman Emam"
#define MyAppURL "https://abdelrahman-tamer.github.io/Tempo-PC-Optimizer/"
#define MyAppExeName "Tempo.exe"

[Setup]
AppId={{D3F9B7A2-7B45-4D1C-8A9E-52174B8F3E90}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=Tempo-Setup-v{#MyAppVersion}
SetupIconFile=..\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=
CloseApplications=force
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish_tempo\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall runascurrentuser

[Code]
function IsDotNet10DesktopInstalled(): Boolean;
var
  SearchRec: TFindRec;
  NetDir: String;
begin
  Result := False;
  NetDir := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if DirExists(NetDir) then
  begin
    if FindFirst(NetDir + '\10.*', SearchRec) then
    begin
      try
        Result := True;
      finally
        FindClose(SearchRec);
      end;
    end;
  end;
  
  if not Result then
  begin
    NetDir := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');
    if DirExists(NetDir) then
    begin
      if FindFirst(NetDir + '\10.*', SearchRec) then
      begin
        try
          Result := True;
        finally
          FindClose(SearchRec);
        end;
      end;
    end;
  end;
end;

procedure CleanOldAppDataInstallation();
var
  OldDir: String;
begin
  OldDir := ExpandConstant('{localappdata}\Programs\Tempo PC Optimizer');
  if DirExists(OldDir) then
  begin
    DelTree(OldDir, True, True, True);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    CleanOldAppDataInstallation();
  end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
  MsgText: String;
begin
  Result := True;
  if not IsDotNet10DesktopInstalled() then
  begin
    if ActiveLanguage = 'arabic' then
      MsgText := 'يتطلب برنامج Tempo وجود حزمة (.NET 10.0 Desktop Runtime) لتشغيله.' + #13#10 + #13#10 +
                 'هل ترغب في فتح صفحة التحميل الرسمية من مايكروسوفت لتثبيتها؟'
    else
      MsgText := 'Tempo PC Optimizer requires the .NET 10.0 Desktop Runtime to run.' + #13#10 + #13#10 +
                 'Would you like to open the official Microsoft download page to install it?';
                 
    if MsgBox(MsgText, mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0/runtime', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataDir: String;
  MsgText: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    AppDataDir := ExpandConstant('{userappdata}\Tempo');
    if DirExists(AppDataDir) then
    begin
      if ActiveLanguage = 'arabic' then
        MsgText := 'هل ترغب في حذف سجلات وبيانات تطبيق Tempo من مجلد AppData؟'
      else
        MsgText := 'Do you want to remove Tempo logs and application data from AppData?';

      if MsgBox(MsgText, mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(AppDataDir, True, True, True);
      end;
    end;
  end;
end;
