---
name: cloudflare-tunnel
description: Cloudflare Tunnel (cloudflared) のセットアップ支援。アカウント / ドメイン / Tunnel ID 確認、config.yml 生成 (httpHostHeader: localhost 含む)、DNS レコード設定案内、Bypass ルール設定案内、Token Webhook 経路確立。/setup の Step 6 から呼ばれる。
---

# cloudflare-tunnel — Cloudflare Tunnel 設定支援スキル

> ⚠️ **このスキルはドラフト (v0.1.0) です。テスター環境での動作確認が必要です。**

## 目的

TradingView の Webhook を、利用者の家庭/オフィス PC に届けるための Cloudflare Tunnel を確立する。
ポート開放を必要としない安全な手段。

## 利用シーン

- `/setup` の Step 6 から呼ばれる (初回セットアップ)
- `/diagnose` の症状 1 (Webhook 不達) で Cloudflare Tunnel が原因と判明したとき
- HTTP 400 Invalid Hostname エラー時 (`docs/troubleshooting.md` §4)

---

## 前提条件

利用者が以下を所有していること:
- **Cloudflare アカウント** (無料プラン可、`dash.cloudflare.com` でサインアップ)
- **独自ドメイン** (Cloudflare で管理しているもの、年 ~1,500 円程度)

未所有の場合:
```
Claude Code 発話:
「Cloudflare 経由で Webhook を中継するには、以下の準備が必要です:
  1. Cloudflare アカウント (無料): https://dash.cloudflare.com/sign-up
  2. 独自ドメイン: お名前.com / Google Domains / Cloudflare Registrar など (年 1,500 円程度)
  3. ドメインの Cloudflare 移管 (NS レコード変更)

これらが完了してから本セットアップを再開してください。」
```

---

## 実行フロー

### Step 1: cloudflared インストール確認

```powershell
$cf = Get-Command cloudflared -ErrorAction SilentlyContinue
if ($cf) {
    "✅ cloudflared インストール済: $($cf.Source)"
    & cloudflared --version
} else {
    "❌ cloudflared 未インストール"
}
```

未インストールの場合、案内:
```
Claude Code 発話:
「cloudflared をインストールします:
  方法 A: 公式 MSI (推奨)
    https://github.com/cloudflare/cloudflared/releases から
    cloudflared-windows-amd64.msi をダウンロード → 実行

  方法 B: winget
    winget install --id Cloudflare.cloudflared

  方法 C: Scoop
    scoop install cloudflare-cloudflared

インストール後、新しい PowerShell ウィンドウで cloudflared --version を実行して確認してください。」
```

### Step 2: cloudflared ログイン (ブラウザ認証)

```powershell
cloudflared tunnel login
```
ブラウザが開き、Cloudflare アカウントでログイン → 利用するゾーン (ドメイン) を選択 → 認証完了。
認証ファイル `~/.cloudflared/cert.pem` が自動生成される (Windows: `%USERPROFILE%\.cloudflared\cert.pem`)。

```
Claude Code 発話:
「ブラウザが開きました。Cloudflare にログインし、対象ドメインを選択してください。
完了したら yes と返答してください。」
```

### Step 3: Tunnel 作成

```powershell
$tunnelName = "n225brokerbridge"  # 利用者が任意の名前を指定可
cloudflared tunnel create $tunnelName
```

出力例:
```
Created tunnel n225brokerbridge with id <tunnel-id>
```

Tunnel ID と認証ファイルパス (`%USERPROFILE%\.cloudflared\<tunnel-id>.json`) を記録。

### Step 4: 公開先 (config.yml) を作成

`C:\SPB_DATA\.cloudflared\config.yml` を生成:
```yaml
tunnel: <tunnel-id>
credentials-file: C:\Users\<utilities/USERNAME>\.cloudflared\<tunnel-id>.json

ingress:
  - hostname: webhook.<your-domain>.com
    service: http://localhost:8001
    originRequest:
      httpHostHeader: localhost     # ⚠️ 絶対必須 (なしだと 400 Invalid Hostname)
  - service: http_status:404
```

**`C:\SPB_DATA\.cloudflared\` ディレクトリ作成**:
```powershell
New-Item -ItemType Directory -Force -Path "C:\SPB_DATA\.cloudflared" | Out-Null
```

PowerShell でテンプレ生成:
```powershell
$tunnelId = "<step 3 で取得した ID>"
$domain = "<your-domain>.com"
$username = $env:USERNAME

$config = @"
tunnel: $tunnelId
credentials-file: C:\Users\$username\.cloudflared\$tunnelId.json

ingress:
  - hostname: webhook.$domain
    service: http://localhost:8001
    originRequest:
      httpHostHeader: localhost
  - service: http_status:404
"@

$config | Out-File -FilePath "C:\SPB_DATA\.cloudflared\config.yml" -Encoding UTF8
```

### Step 5: DNS レコード設定

Tunnel と DNS を紐づけ:
```powershell
cloudflared tunnel route dns $tunnelName webhook.<your-domain>.com
```

Cloudflare のダッシュボードで以下のレコードが自動追加されるはず:
```
Type:  CNAME
Name:  webhook
Value: <tunnel-id>.cfargotunnel.com
Proxy: ON (橙色クラウド)
```

確認:
```
Claude Code 発話:
「Cloudflare ダッシュボード (https://dash.cloudflare.com) で
あなたのドメインの DNS タブを開き、
webhook の CNAME レコードが追加されていることを確認してください。
Proxy が ON (橙色クラウド) になっていれば OK です。
完了したら yes と回答してください。」
```

### Step 6: Bypass ルール設定 (重要)

TradingView の User-Agent が Cloudflare の WAF (Web Application Firewall) に弾かれることがある。
`/webhook` パスをセキュリティ機能から除外するルールを必ず設定する:

```
Claude Code 発話:
「Cloudflare ダッシュボード → セキュリティ → WAF → カスタムルール → 「ルールを作成」

  Rule name: TV Webhook Bypass
  Expression: (http.host eq "webhook.<your-domain>.com" and http.request.uri.path matches "^/webhook")
  Action: Skip (and select: All security WAF features)

このルールがないと、TradingView の Webhook が 403 や 530 で弾かれます (恒久対処)。
作成完了したら yes と回答してください。」
```

> **重要ノウハウ** (N225 プロジェクトで 2026-05-13 に確立): 詳細は著者向けメモリ [`reference_cloudflare_webhook_bypass.md`](../../memory/reference_cloudflare_webhook_bypass.md) を参照。

### Step 7: Tunnel 起動

通常起動 (フォアグラウンド、確認用):
```powershell
cloudflared tunnel --config "C:\SPB_DATA\.cloudflared\config.yml" run
```

起動成功なら以下のようなログ:
```
INF Connection registered connIndex=0 ...
INF Connection registered connIndex=1 ...
```

Ctrl+C で停止。

### Step 8: Windows サービス化 (バックグラウンド常駐)

ブリッジと連動して常駐させるため、Windows サービスとして登録:
```powershell
cloudflared service install --config "C:\SPB_DATA\.cloudflared\config.yml"
```

サービス状態確認:
```powershell
Get-Service cloudflared
```

`Status: Running` なら成功。

または、起動スクリプト (タスクスケジューラ + 起動時実行) で代替する手も:
```powershell
$action = New-ScheduledTaskAction -Execute "cloudflared.exe" `
    -Argument 'tunnel --config "C:\SPB_DATA\.cloudflared\config.yml" run'
$trigger = New-ScheduledTaskTrigger -AtStartup
Register-ScheduledTask -TaskName "cloudflared-N225" -Action $action -Trigger $trigger -RunLevel Highest
```

### Step 9: 疎通テスト

ブリッジが起動している前提で:
```powershell
Invoke-WebRequest -Uri "https://webhook.<your-domain>.com/webhook/" `
    -Method Post `
    -Body (@{ alert_name="ConnTest"; interval=5; trade_type="new"; side="buy"; price=0; passphrase="<your_passphrase>" } | ConvertTo-Json) `
    -ContentType "application/json"
```

HTTP 200 + ブリッジログに `Received` が記録されれば疎通成功。

エラー時:
| エラー | 原因 |
|---|---|
| HTTP 400 Invalid Hostname | `httpHostHeader: localhost` 設定漏れ |
| HTTP 403 / 530 | Bypass ルール未設定 (Step 6) |
| HTTP 502 / Connection refused | ブリッジが Listen していない (port 8001) |
| HTTP 1033 / 1016 | DNS レコード未反映 (数分待つ) or Tunnel 未起動 |
| Timeout | cloudflared プロセス停止 |

---

## 重要ファイル一覧

| ファイル | 役割 |
|---|---|
| `%USERPROFILE%\.cloudflared\cert.pem` | Cloudflare アカウント認証 (Step 2 で生成) |
| `%USERPROFILE%\.cloudflared\<tunnel-id>.json` | Tunnel 個別の credentials |
| `C:\SPB_DATA\.cloudflared\config.yml` | Tunnel 動作設定 (本ブリッジ用) |

これらは秘密情報を含むため、GitHub にコミットしないこと。

---

## トラブル時のクイック確認

```powershell
# 1. cloudflared プロセス起動中か
Get-Process cloudflared -EA SilentlyContinue

# 2. config.yml の文法
cloudflared tunnel --config "C:\SPB_DATA\.cloudflared\config.yml" ingress validate

# 3. DNS 反映確認
Resolve-DnsName webhook.<your-domain>.com

# 4. Cloudflare 経由疎通 (HEAD only)
Invoke-WebRequest -Uri "https://webhook.<your-domain>.com/webhook/" -Method Head -ErrorAction SilentlyContinue
```

---

## 設計メモ (開発者向け)

### このスキルの設計原則

1. **httpHostHeader を絶対案内**: 400 Invalid Hostname は最頻トラブル
2. **Bypass ルールを絶対案内**: 403/530 で詰む。Cloudflare 無料プランでも設定可
3. **MSI 推奨**: winget / Scoop でもインストールできるが、公式 MSI が一番無難
4. **サービス化推奨**: 利用者が PC 再起動するたびに手動起動するのは現実的でない

### TBD

- [ ] cloudflared 自動アップデート機能の案内
- [ ] 複数 Tunnel の管理 (将来、複数戦略用に別 Tunnel を使うケース)
- [ ] config.yml の自動 backup / restore

### バージョン

- v0.1.0 (2026-05-22、初版ドラフト)
