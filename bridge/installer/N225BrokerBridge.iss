; ============================================================
;  N225 Broker Bridge — Inno Setup Script
;  ビルド手順:
;    1. dotnet publish src/N225BrokerBridge.UI -c Release -r win-x64 ^
;         --self-contained true -p:PublishSingleFile=false -o installer/publish
;    2. iscc installer/N225BrokerBridge.iss
;  あるいは: installer/build.ps1 を実行
; ============================================================

#define MyAppName       "N225 AutoPilot"
#define MyAppVersion    "0.1.0"
#define MyAppPublisher  "Takao"
#define MyAppURL        ""
#define MyAppExeName    "N225BrokerBridge.UI.exe"
; AppId は GUID 固定。再インストール時はこの ID で「既存版を検出 → アップグレード」と判定される。
; 新規アプリ用なら新規 GUID 生成、既存と同じアプリのアップグレードなら絶対に変えない。
#define MyAppId         "{{A0B1C2D3-E4F5-6789-ABCD-EF0123456789}}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\N225BrokerBridge
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
OutputDir=output
OutputBaseFilename=N225BrokerBridge-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=n225_autopilot.ico

; 64bit 専用 (.NET 8 self-contained は x64)
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64

; 管理者権限が必要 (Program Files に書き込むため)
PrivilegesRequired=admin

; アンインストーラーや「プログラムと機能」に表示するアイコン
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

; 最小対応 OS: Windows 10
MinVersion=10.0.17763

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startmenuicon"; Description: "スタートメニューにショートカットを作成"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; publish フォルダ以下を {app} (例: C:\Program Files\N225BrokerBridge) にコピー
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; スタートメニュー
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; Tasks: startmenuicon
; デスクトップ (オプション)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; インストール完了後の起動オプション
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; アンインストール時に、インストール先に残ったユーザーログや一時ファイルも削除
Type: filesandordirs; Name: "{app}\logs"

; ※ %LOCALAPPDATA%\N225BrokerBridge 配下のユーザーデータ
;   (appsettings.Local.json、auto-positions.json、strategies.json 等) は
;   アンインストール後も残す。再インストール時に設定が継続するため。
;   完全削除したい場合は手動で %LOCALAPPDATA%\N225BrokerBridge を削除すること。
