;============================================================================
; AntiZapretDPI — установщик (Inno Setup)
;
; Поддерживаемые режимы:
;   • Интерактивный — мастер установки (wizard).
;   • Тихий (скрытый) режим:
;       Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
;           /VERYSILENT        — полная установка без показа окон
;           /SILENT            — установка с индикатором, но без вопросов
;           /SUPPRESSMSGBOXES  — автоматический ответ на все сообщения
;           /DIR="путь"        — каталог установки
;           /COMPONENTS=...    — выбор компонентов
;           /TASKS=...         — выбор задач (например, /TASKS=desktopicon)
;
; Деинсталлятор тоже поддерживает тихий режим:
;       Uninstall.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
;
; Сборка (из src\AntiZapretDPI.Installer):
;   ISCC.exe installer.iss /DSourceDir="..\..\artifacts\publish" /DAppVersion=1.0.0
;
; Параметры (задаются через /D на этапе сборки):
;   SourceDir      — каталог с файлами приложения (результат dotnet publish)
;   AppVersion     — версия установщика/приложения
;   AppIcon        — путь к иконке
;   LicenseFile    — путь к лицензии
;   SelfContained  — если определён, пропускается проверка .NET Runtime
;============================================================================

#ifndef SourceDir
  #define SourceDir "..\..\artifacts\publish"
#endif
#ifndef AppVersion
  #define AppVersion "1.0.1"
#endif
#ifndef AppIcon
  #define AppIcon "..\AntiZapretDPI.Desktop\Assets\Icons\AntiZapretDPI-Icon-Multi.ico"
#endif
#ifndef LicenseFile
  #define LicenseFile "..\..\LICENSE"
#endif

#define MyAppName "AntiZapretDPI"
#define MyAppExe "AntiZapretDPI.exe"
#define MyAppPublisher "AntiZapretDPI Project"
#define MyAppVersion AppVersion

[Setup]
AppId={{8B0E6C7A-1A4F-4D92-9C3E-5F6D2A7B8C01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={commonpf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\..\dist
OutputBaseFilename=AntiZapretDPI-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
SetupIconFile={#AppIcon}
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
LicenseFile={#LicenseFile}
CloseApplications=yes
SetupLogging=yes
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} установщик
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные ярлыки:"
Name: "startmenuicon"; Description: "Создать ярлык в меню «Пуск»"; GroupDescription: "Дополнительные ярлыки:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: startmenuicon
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  DotNet10Url = 'https://dotnet.microsoft.com/download/dotnet/10.0';

var
  ResultCode: Integer;

{ Проверка наличия .NET 10 Desktop Runtime }
function DotNet10DesktopInstalled(): Boolean;
var
  RuntimeDir: String;
  FindRec: TFindRec;
begin
  if IsWin64 then
    RuntimeDir := ExpandConstant('{pf64}\dotnet\shared\Microsoft.WindowsDesktop.App')
  else
    RuntimeDir := ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App');

  Result := FindFirst(RuntimeDir + '\10.*', FindRec);
  if Result then
    FindClose(FindRec);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;

#ifndef SelfContained
  if not DotNet10DesktopInstalled() then
  begin
    { не прерываем установку: предупреждаем только в интерактивном режиме }
    if not WizardSilent then
      MsgBox('Внимание: на этом компьютере не найден .NET 10 Desktop Runtime.' + #13#10 +
        'Приложение может не запуститься, пока он не будет установлен.' + #13#10 + #13#10 +
        'Скачать его можно здесь:' + #13#10 + DotNet10Url, mbInformation, MB_OK);
  end;
#endif
end;

{ Остановка работающих процессов перед установкой }
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  Exec('taskkill.exe', '/F /T /IM {#MyAppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /T /IM winws.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

{ Остановка процессов, удаление автозапуска и служб при деинсталляции }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec('taskkill.exe', '/F /T /IM {#MyAppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/F /T /IM winws.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    Exec('schtasks.exe', '/Delete /TN "{#MyAppName}" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    Exec('sc.exe', 'stop zapret', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('sc.exe', 'delete zapret', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('sc.exe', 'stop WinDivert14', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('sc.exe', 'delete WinDivert14', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('sc.exe', 'stop WinDivert', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('sc.exe', 'delete WinDivert', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
