# /setup — 全自動セットアップ

N225BrokerBridge を新しい PC でゼロからセットアップするための対話的コマンドです。

> ⚠️ **このコマンドはドラフト (v0.1.0) です。テスター環境での動作確認が必要です。**

## 目的

利用者の PC 環境を診断し、不足している前提条件を順次解決して、最終的に N225BrokerBridge を稼働可能な状態にする。

## 実行フロー (Claude Code が辿るステップ)

### Step 0: 初期挨拶 + 同意確認

```
Claude Code 発話:
「N225BrokerBridge の初期セットアップを開始します。
このコマンドは以下を行います:
  1. 環境前提条件の診断 (Windows / .NET / 各種ソフトウェア)
  2. 不足ソフトウェアのインストール案内 (実行はあなたの確認後)
  3. 設定ファイルの生成 (パスワード等は伏字入力)
  4. kabu Station / TradingView / Cloudflare の設定支援
  5. テスト実行で動作確認

途中で中断しても、再度 /setup を実行すれば続きから再開できます。
始めてよろしいですか? (yes/no)」
```

`no` なら中断。`yes` で次へ。

---

### Step 1: 環境診断

並列で以下を確認:

| 項目 | 確認方法 | 必須 |
|---|---|---|
| **Windows バージョン** | `[System.Environment]::OSVersion.Version` で 10.0.17763 以上 | ✅ |
| **.NET 8 SDK** | `dotnet --list-sdks` で 8.x.x が含まれるか | ✅ |
| **.NET 8 Desktop Runtime** | `dotnet --list-runtimes` で `Microsoft.WindowsDesktop.App 8.x` | ✅ |
| **PowerShell バージョン** | `$PSVersionTable.PSVersion` で 5.1+ | ✅ |
| **kabu Station** | `C:\kabusapi\` フォルダ存在 / プロセス確認 | ✅ |
| **cloudflared** | `where.exe cloudflared` で実行ファイル発見 | ✅ |
| **Visual Studio / VS Code** | `where.exe code` または VS インストール確認 | △ (開発用、配布版では不要) |

不足項目があれば、利用者に「これらが不足しています。インストール手順を案内しますか?」と確認。

---

### Step 2: 不足ソフトウェアのインストール案内

#### .NET 8 SDK / Runtime
```
案内: https://dotnet.microsoft.com/download/dotnet/8.0
→ "x64 installer" をダウンロード → 実行
→ インストール後、新しい PowerShell で `dotnet --version` で確認
```

#### kabu Station
```
案内: auカブコム証券にログイン → 「kabuステーションのダウンロード」
→ インストール後、起動 → 「API 設定」タブで API 利用を有効化
→ パスワードを設定 (本番・検証それぞれ)
```

#### cloudflared
```
案内: https://github.com/cloudflare/cloudflared/releases
→ "cloudflared-windows-amd64.msi" をダウンロード・インストール
```

---

### Step 3: ビルド + インストーラー実行

リポジトリ内のインストーラーを使う or ソースからビルド:

#### オプション A: インストーラー使用 (推奨)
```powershell
# installer/output/N225BrokerBridge-Setup-x.x.x.exe を実行
Start-Process -FilePath "installer\output\N225BrokerBridge-Setup-0.1.0.exe"
```

#### オプション B: ソースからビルド
```powershell
cd src\N225BrokerBridge.UI
dotnet publish -c Release -r win-x64 --self-contained true -o ..\..\publish
```

---

### Step 4: 設定ファイル生成

`%LOCALAPPDATA%\N225BrokerBridge\appsettings.Local.json` を生成。
**パスワード類は DPAPI で暗号化**する必要があるため、初回起動時に UI から入力させるのが正攻法。

```powershell
# フォルダ作成
$localAppData = "$env:LOCALAPPDATA\N225BrokerBridge"
New-Item -ItemType Directory -Force -Path $localAppData | Out-Null

# テンプレ JSON 生成 (パスワードは空、起動後 UI で入力)
$template = @{
    Webhook = @{
        Port = 8001
        Passphrase = ""  # ← 初回起動時 UI で入力
    }
    Kabu = @{
        Mode = "Verification"  # 初回は検証モード推奨
    }
    Behavior = @{
        RequireConfirmBeforeOrder = $true
    }
} | ConvertTo-Json
$template | Out-File -FilePath "$localAppData\appsettings.Local.json" -Encoding UTF8
```

---

### Step 5: kabu Station 接続確認

利用者の確認:
```
Claude Code 発話:
「kabu Station を起動し、API 機能が有効になっていることを確認してください。
本番モード (18080) または検証モード (18081) のどちらで接続しますか?
初回は検証モードを推奨します (実発注なし)。」
```

接続テスト:
```powershell
# 検証モード接続テスト (Token 取得)
$body = @{ APIPassword = "(利用者入力)" } | ConvertTo-Json
$response = Invoke-RestMethod -Uri "http://localhost:18081/kabusapi/token" `
    -Method Post -Body $body -ContentType "application/json"
if ($response.Token) { "✅ kabu Station 接続成功" } else { "❌ 接続失敗" }
```

---

### Step 6: Cloudflare Tunnel セットアップ

利用者の Cloudflare アカウントとドメインが必要。詳細は `.claude/skills/cloudflare-tunnel/` を参照 (TBD)。

最低限の設定例 (`C:\SPB_DATA\.cloudflared\config.yml`):

```yaml
tunnel: <tunnel-id>
credentials-file: <利用者の認証ファイル>

ingress:
  - hostname: webhook.<利用者ドメイン>
    service: http://localhost:8001
    originRequest:
      httpHostHeader: localhost
  - service: http_status:404
```

---

### Step 7: TradingView アラート設定案内

```
Claude Code 発話:
「TradingView にログインし、戦略のアラートを設定してください。
Webhook URL: https://webhook.<利用者ドメイン>/webhook/
Message (JSON):
{
  \"alert_name\": \"<戦略名>\",
  \"interval\": <分>,
  \"trade_type\": \"new\",
  \"side\": \"buy\",
  \"price\": 0,
  \"passphrase\": \"<設定した passphrase>\"
}
※ TradingView Webhook 機能は有料プラン (Pro 以上) が必要です。」
```

---

### Step 8: 動作確認 (テスト POST)

ローカルに直接 POST して動作確認:

```powershell
$body = @{
    alert_name = "TestStrategy"
    interval = 5
    trade_type = "new"
    side = "buy"
    price = 0
    passphrase = "(設定した passphrase)"
} | ConvertTo-Json

Invoke-WebRequest -Uri "http://localhost:8001/webhook/" `
    -Method Post -Body $body -ContentType "application/json"
```

期待: HTTP 200、Body `Ignored_` (戦略未登録なら) or `NewOrderDispatched_` (登録済みなら)

---

### Step 9: 完了報告 + 次のアクション

```
Claude Code 発話:
「セットアップ完了です。次に以下のいずれかを実施してください:

  ✅ /verify     — 詳細な動作確認 (全シナリオ自動テスト)
  ✅ /diagnose   — 何か異常があれば自動診断
  
本番モード切替時の注意:
  1. UI の設定画面で `Kabu.Mode` を `Production` に変更
  2. 本番パスワードを入力
  3. ブリッジを再起動
  4. 自動売買トグルを ON にする (デフォルト OFF、安全側設計)
  
取引終了時は自動売買トグルを OFF に戻す習慣を推奨します。」
```

---

## エラーハンドリング

### .NET 8 が無い場合
```
案内 → 利用者がインストール → /setup を再実行 (Step 1 から再診断、既存項目はスキップ)
```

### kabu Station 接続失敗
```
1. プロセス確認 (Get-Process kabusapi)
2. API 設定確認 (GUI で「API 利用」が有効か)
3. ファイアウォール確認
4. 解決しない場合 /diagnose に誘導
```

### Cloudflare Tunnel 設定不明
```
利用者に「Cloudflare Tunnel をご存知ですか? (yes/no)」
→ no の場合: 簡易解説 + 公式ドキュメント案内
→ yes の場合: 既存設定の確認 + 必要な ingress 追加
```

---

## 設計メモ (開発者向け)

### このコマンドが想定する利用者

- 技術スキル中級 (PowerShell の基本コマンドが分かる)
- kabu Station / TradingView アカウント所持済み
- 本ブリッジを買って初めて使う

### TBD (改善余地)

- [ ] 各 Step の自動テスト (Pester 等で確認)
- [ ] `.claude/skills/kabu-config/`, `.claude/skills/cloudflare-tunnel/`, `.claude/skills/tv-webhook/` の本実装
- [ ] 利用者が「途中の Step だけ再実行したい」場合のショートカット (`/setup-step 3` 等)
- [ ] 多言語対応 (英語版の利用者向け)

### バージョン

- v0.1.0 (2026-05-21、初版ドラフト)
- テスト環境: 著者の新 PC でテスト予定
