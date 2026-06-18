# /verify — 動作確認

N225BrokerBridge が正常に動作しているかを段階的に確認するコマンドです。

> ⚠️ **このコマンドはドラフト (v0.1.0) です。テスター環境での動作確認が必要です。**

## 目的

`/setup` 完了後、または日常運用中に「ちゃんと動いているか?」を 1 コマンドで確認する。
失敗項目があれば、原因を特定して `/diagnose` への誘導 or 直接修復案を提示する。

## 利用シーン

| シーン | 期待する結果 |
|---|---|
| `/setup` 直後 | 全項目 ✅ PASS |
| 日常運用中の念のため確認 | 全項目 ✅ PASS |
| 「動かない気がする」とき | 1 項目以上 ❌ FAIL → `/diagnose` 誘導 |
| TradingView の戦略本番投入前 | 全項目 ✅ PASS + 自動売買トグル OFF を確認 |

---

## 実行フロー

### Step 0: 初期挨拶

```
Claude Code 発話:
「N225BrokerBridge の動作確認を開始します。約 30 秒で完了します。
途中で見つかった問題はその場で説明し、必要に応じて修復案を提示します。
よろしいですか? (yes/no)」
```

`no` なら中断。`yes` で次へ。

---

### Step 1: ブリッジプロセス確認

```powershell
$bridge = Get-Process -Name "N225BrokerBridge.UI" -ErrorAction SilentlyContinue
if ($bridge) {
    "✅ ブリッジプロセス起動中 (PID: $($bridge.Id))"
} else {
    "❌ ブリッジプロセスが見つかりません"
}
```

- **PASS**: PID 表示 → 次へ
- **FAIL**: 「起動していません。デスクトップショートカット or ダッシュボード B から起動してください」と案内 → 利用者起動後に `/verify` 再実行

---

### Step 2: Webhook 受信ポート (8001) リッスン確認

```powershell
$listening = Get-NetTCPConnection -LocalPort 8001 -State Listen -ErrorAction SilentlyContinue
if ($listening) {
    "✅ ポート 8001 リッスン中"
} else {
    "❌ ポート 8001 がリッスンされていません"
}
```

- **PASS**: 次へ
- **FAIL**:
  - ブリッジが Listen していない可能性 → ログ参照
  - 別プロセスがポート 8001 を占有している可能性 → `Get-NetTCPConnection -LocalPort 8001` でプロセス確認
  - → `/diagnose` 誘導

---

### Step 3: kabu Station 接続確認 (Token 取得)

利用者に確認:
```
Claude Code 発話:
「現在のモードはどちらですか?
  1. 検証 (Verification, port 18081)
  2. 本番 (Production, port 18080)
※ %LOCALAPPDATA%\N225BrokerBridge\appsettings.Local.json の Kabu.Mode で設定されています」
```

選択に応じて port を決定し、Token 取得テスト:

```powershell
$port = 18081  # 検証モード時 (本番は 18080)
$apiPwd = Read-Host -AsSecureString "kabu API パスワードを入力 (画面に出ません)"
$bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($apiPwd)
$plainPwd = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
[System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)

$body = @{ APIPassword = $plainPwd } | ConvertTo-Json
try {
    $response = Invoke-RestMethod -Uri "http://localhost:$port/kabusapi/token" `
        -Method Post -Body $body -ContentType "application/json" -TimeoutSec 5
    if ($response.Token) {
        "✅ kabu Station 接続成功 (Token 取得)"
    } else {
        "❌ Token が返ってきません"
    }
} catch {
    "❌ kabu Station 接続失敗: $($_.Exception.Message)"
}
$plainPwd = $null
```

- **PASS**: 次へ
- **FAIL**:
  - kabu Station 未起動 / API 設定無効 / パスワード違い / ファイアウォール → `/diagnose` 誘導

---

### Step 4: Cloudflare Tunnel 経由疎通確認 (オプション)

利用者の Cloudflare 設定を確認:
```
Claude Code 発話:
「Cloudflare Tunnel 経由の Webhook URL (例: https://webhook.your-domain.com/webhook/) を入力してください。
※ TradingView 連携を使わない場合は skip と入力」
```

skip でなければ:
```powershell
$webhookUrl = "<利用者入力>"
try {
    # HEAD で疎通だけ確認 (本物の POST は Step 5 で)
    $response = Invoke-WebRequest -Uri $webhookUrl -Method Get -TimeoutSec 5 -ErrorAction Stop
    "✅ Cloudflare Tunnel 経由で 200 系または許容 4xx (404/405) を受信"
} catch [System.Net.WebException] {
    if ($_.Exception.Response.StatusCode -in @(404, 405)) {
        "✅ Cloudflare Tunnel 経由疎通成功 (GET は 405 で正常)"
    } elseif ($_.Exception.Message -match "400") {
        "❌ HTTP 400 Invalid Hostname — Cloudflare 側 httpHostHeader 設定漏れの可能性"
    } else {
        "❌ Cloudflare Tunnel 疎通失敗: $($_.Exception.Message)"
    }
}
```

- **PASS**: 次へ
- **400 Invalid Hostname**: `docs/troubleshooting.md` の該当節を案内
- **FAIL (その他)**: cloudflared プロセス確認 → `/diagnose` 誘導

---

### Step 5: ローカル Webhook 発火テスト (POST)

`%LOCALAPPDATA%\N225BrokerBridge\appsettings.Local.json` から passphrase を読み (DPAPI 復号は UI 経由のみのため、ここは利用者に聞く):

```powershell
$passphrase = Read-Host -AsSecureString "Webhook passphrase を入力 (画面に出ません)"
$bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($passphrase)
$plainPass = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
[System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)

$body = @{
    alert_name = "VerifyTest"
    interval = 5
    trade_type = "new"
    side = "buy"
    price = 0
    passphrase = $plainPass
} | ConvertTo-Json

try {
    $response = Invoke-WebRequest -Uri "http://localhost:8001/webhook/" `
        -Method Post -Body $body -ContentType "application/json" -TimeoutSec 5
    "✅ Webhook 受信 OK (HTTP $($response.StatusCode), Body: $($response.Content))"
} catch {
    "❌ Webhook 発火失敗: $($_.Exception.Message)"
}
$plainPass = $null
```

期待される Body:
- `Ignored_*` → 戦略未登録の正常応答 (テスト戦略は登録されていないため)
- `Unauthorized` → passphrase 不一致 (要確認)
- `NewOrderDispatched_*` → 戦略登録済かつ自動売買 ON だった場合 (テスト中は想定外)

---

### Step 6: 自動売買トグル状態確認 (UI で目視)

```
Claude Code 発話:
「ブリッジ UI 右上のステータスバーで「自動売買」トグルが OFF (灰色) になっていることを目視確認してください。
※ Verify 中は OFF を推奨 (テスト POST で誤発注しないため)。
確認できましたか? (yes/no)」
```

利用者の回答を記録。

---

### Step 7: ログ確認 (直近 10 件)

```powershell
$logDir = "$env:LOCALAPPDATA\N225BrokerBridge\logs"
$latestLog = Get-ChildItem -Path $logDir -Filter "*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($latestLog) {
    "📝 最新ログファイル: $($latestLog.FullName)"
    Get-Content $latestLog.FullName -Tail 10
} else {
    "⚠️ ログファイルが見つかりません"
}
```

エラー行 (`ERR ` `FATAL ` を含む) があれば抽出して提示。

---

### Step 8: 結果サマリ

```
✅ PASS: N 項目
❌ FAIL: M 項目
⚠️ WARN: K 項目

[全 PASS の場合]
「動作確認完了。問題ありません。
本番運用に進む場合:
  1. UI 設定で Kabu.Mode を Production に変更
  2. 自動売買トグルを ON に
  3. TradingView の戦略アラートを有効化」

[FAIL がある場合]
「以下に問題があります:
  ❌ Step X: <症状>
  → 推奨アクション: /diagnose を実行してください」
```

---

## 出力フォーマット例

```
=== N225BrokerBridge /verify 結果 ===
Step 1  ブリッジプロセス       ✅ PASS (PID 18908)
Step 2  ポート 8001 リッスン    ✅ PASS
Step 3  kabu Station 接続       ✅ PASS (Verification)
Step 4  Cloudflare Tunnel       ⏭️ SKIP (利用者選択)
Step 5  ローカル Webhook 発火   ✅ PASS (Body: Ignored_unknown_strategy)
Step 6  自動売買トグル          ✅ PASS (OFF 確認)
Step 7  ログ                    ⚠️ WARN (直近 1 件の警告あり: ...)

合計: 5 PASS / 0 FAIL / 1 WARN / 1 SKIP
推奨アクション: 警告内容を確認、本番投入可
```

---

## 設計メモ (開発者向け)

### このコマンドの設計原則

1. **副作用なし**: 読み取り専用。設定変更・発注は一切しない
2. **約 30 秒で完走**: 各 Step に 5 秒タイムアウトを設定
3. **失敗時は次へ進む**: 1 項目失敗しても残りは確認する (全体像を把握するため)
4. **秘密情報は SecureString**: パスワード入力は伏字、メモリから即時クリア

### TBD

- [ ] `.claude/skills/verify-*` への分割 (各 Step を独立 skill 化)
- [ ] 結果を JSON で出力するオプション (CI 連携用)
- [ ] 自動修復モード (`--fix` で簡単な問題は自動対処)

### バージョン

- v0.1.0 (2026-05-22、初版ドラフト)
