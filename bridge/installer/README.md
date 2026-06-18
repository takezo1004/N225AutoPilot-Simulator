# N225 Broker Bridge — インストーラー (Inno Setup)

このフォルダは N225 Broker Bridge の配布用インストーラー (Inno Setup) を作成するための一式。

## ファイル構成

```
installer/
├── N225BrokerBridge.iss   ← Inno Setup スクリプト本体
├── build.ps1              ← publish + iscc 一発ビルドスクリプト
├── README.md              ← このファイル
├── publish/               ← (自動生成) dotnet publish の出力先
└── output/                ← (自動生成) 完成したインストーラー .exe
```

## 前提

- **.NET 8 SDK** がインストールされていること (`dotnet --version` で 8.x が表示される)
- **Inno Setup 6** がインストールされていること ([公式](https://jrsoftware.org/isinfo.php))
  - インストール時に「ISCC コマンドライン」も含めること
  - パス例: `C:\Program Files (x86)\Inno Setup 6\iscc.exe`

## ビルド手順

### 一発ビルド (推奨)

N225BrokerBridge ディレクトリで PowerShell を開き:

```powershell
.\installer\build.ps1
```

- 旧 publish フォルダを削除
- `dotnet publish` (self-contained, win-x64) を実行
- `iscc` で Inno Setup コンパイル
- `installer/output/N225BrokerBridge-Setup-x.x.x.exe` 生成

### 手動ビルド (個別実行する場合)

```powershell
# 1. publish (self-contained で .NET ランタイム同梱)
dotnet publish src/N225BrokerBridge.UI `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o installer/publish

# 2. Inno Setup コンパイル
& "C:\Program Files (x86)\Inno Setup 6\iscc.exe" installer/N225BrokerBridge.iss
```

## 配布物

`installer/output/N225BrokerBridge-Setup-0.1.0.exe` がインストーラー本体。

- **サイズ**: 約 80-120 MB (self-contained で .NET ランタイム同梱のため)
- **対応 OS**: Windows 10 1809 以降 / Windows 11 (x64)
- **管理者権限**: 必要 (Program Files にインストールするため)

## インストーラーの動作

1. **言語選択** (日本語)
2. **使用許諾** (現状なし)
3. **インストール先選択** (デフォルト `C:\Program Files\N225BrokerBridge`)
4. **追加タスク**: デスクトップアイコン (オプション) / スタートメニュー (デフォルト ON)
5. **インストール実行**
6. **完了時に起動** (チェックボックス)

## バージョン更新時

[N225BrokerBridge.iss](N225BrokerBridge.iss) の以下を変更:

```iss
#define MyAppVersion    "0.1.0"  ← ここを更新 (例: "0.1.1")
```

`MyAppId` (GUID) は **絶対に変更しない**。同じ ID なら「上書きインストール」、変えると「別アプリ扱い」になり旧バージョンと並列インストールされる。

## ユーザーデータの扱い

- **アプリ本体**: `C:\Program Files\N225BrokerBridge\`
- **ユーザー設定**: `%LOCALAPPDATA%\N225BrokerBridge\`
  - `appsettings.Local.json` — passphrase、kabu API パスワード等
  - `auto-positions.json` — 自動売買の建玉メタストア
  - `strategies.json` — 戦略レジストリ
  - `logs\` — Serilog のログ
- **アンインストール時**: アプリ本体のみ削除、ユーザー設定は保持 (再インストール時に継続)
- **ユーザー設定も削除したい場合**: 手動で `%LOCALAPPDATA%\N225BrokerBridge` を削除

## トラブルシューティング

### `iscc.exe が見つかりません`
Inno Setup 6 が PATH に通っていない。`build.ps1` の `$isccCandidates` にパスを追加するか、PATH 環境変数に追加。

### `publish` が遅い / 大きい
self-contained ビルドは .NET ランタイム同梱のため 80-120 MB になる。配布先にも .NET 8 ランタイムを入れさせて良い場合は `--self-contained false` にすればサイズが 1/4 程度になる。

### `appsettings.Local.json` を初期配布したい
`installer/publish/` フォルダに `appsettings.Local.json` を手動で配置するか、`[Files]` セクションに以下を追記:

```iss
Source: "appsettings.Local.json.template"; DestDir: "{app}"; Flags: onlyifdoesntexist
```

ただしユーザーごとに違う設定 (passphrase 等) を含むので、配布版にはテンプレートのみが安全。

## 参考リンク

- [Inno Setup 公式](https://jrsoftware.org/isinfo.php)
- [Inno Setup ドキュメント](https://jrsoftware.org/ishelp/)
- [.NET Publish オプション](https://learn.microsoft.com/ja-jp/dotnet/core/deploying/)
