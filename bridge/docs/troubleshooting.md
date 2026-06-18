# トラブルシューティング集

> このドキュメントは `/diagnose` コマンドが参照する主要トラブル事例集です。
> 利用者は **まず `/diagnose` を実行**してください。本文書は症状別の詳細手順リファレンスとして機能します。
>
> ⚠️ **v0.1.0 ドラフト** — テスター環境で内容を順次検証・追記中。

---

## 目次

1. [Webhook が届かない](#1-webhook-が届かない)
2. [kabu API 接続エラー](#2-kabu-api-接続エラー)
3. [Webhook は受信されるが発注されない](#3-webhook-は受信されるが発注されない)
4. [HTTP 400 Invalid Hostname](#4-http-400-invalid-hostname)
5. [起動時例外 / アプリが起動しない](#5-起動時例外--アプリが起動しない)
6. [kabu が「銘柄が見つからない」(Code=4002001) で拒否する](#6-kabu-が銘柄が見つからない-code4002001-で拒否する)
7. [手動発注で枚数を 2 枚以上にしても 1 枚しか発注されない](#7-手動発注で枚数を-2-枚以上にしても-1-枚しか発注されない)
8. [限月ロール後に縦玉一覧・約定一覧へ旧限月のデータが残る (限月管理)](#8-限月ロール後に縦玉一覧約定一覧へ旧限月のデータが残る-限月管理)
9. [リサイズした画面サイズが次回起動時に保持されず既定に戻る](#9-リサイズした画面サイズが次回起動時に保持されず既定に戻る)
10. [その他の症状](#10-その他の症状)

---

## 1. Webhook が届かない

### 症状

- TradingView の戦略アラートが発火しても、ブリッジが受信しない
- ブリッジのログに `Received` 行が出ない
- TradingView 側のアラート履歴には「送信済」と表示されている

### 主な原因 (確率順)

| 順位 | 原因 | 確認方法 |
|---|---|---|
| 1 | Cloudflare Tunnel (cloudflared) 停止 | `Get-Process cloudflared` |
| 2 | ブリッジが Listen していない | `Get-NetTCPConnection -LocalPort 8001` |
| 3 | Cloudflare 側のセキュリティ設定で `/webhook` パスをブロック | Cloudflare ダッシュボード確認 |
| 4 | TradingView の Webhook URL が間違っている | アラート設定画面の Notifications タブ |
| 5 | passphrase 不一致 | ブリッジログに `Unauthorized` が出る (届いてはいる) |

### 確認手順

#### Step 1: cloudflared が起動しているか
```powershell
Get-Process cloudflared -ErrorAction SilentlyContinue
```
出力がなければ起動:
```powershell
Start-Process "C:\Program Files (x86)\cloudflared\cloudflared.exe" `
    -ArgumentList "tunnel", "--config", "C:\SPB_DATA\.cloudflared\config.yml", "run" `
    -WindowStyle Hidden
```

#### Step 2: ブリッジが 8001 Listen しているか
```powershell
Get-NetTCPConnection -LocalPort 8001 -State Listen
```
出力がなければ、ブリッジ UI 再起動を試す。

#### Step 3: ローカルへ直接 POST してブリッジが受信するか
```powershell
$body = @{
    alert_name = "TestSignal"; interval = 5; trade_type = "new"
    side = "buy"; price = 0; passphrase = "<your_passphrase>"
} | ConvertTo-Json
Invoke-WebRequest -Uri "http://localhost:8001/webhook/" -Method Post `
    -Body $body -ContentType "application/json"
```
HTTP 200 + Body が返れば**ブリッジ自体は正常**。問題は Cloudflare 経路。

#### Step 4: Cloudflare 経由で疎通テスト
```powershell
Invoke-WebRequest -Uri "https://webhook.your-domain.com/webhook/" -Method Post `
    -Body $body -ContentType "application/json"
```
ここで失敗するなら Cloudflare 側の問題。`webhook.your-domain.com` の DNS / Tunnel ingress / Bypass ルールを確認。

### Bypass ルール (Cloudflare WAF)

`/webhook` パスを全セキュリティから除外するカスタムルールが必要 (TradingView の UA は通常 WAF にひっかかる):

```
Rule name: TV Webhook Bypass
Expression: (http.host eq "webhook.your-domain.com" and http.request.uri.path matches "^/webhook")
Action: Skip (all WAF features)
```

詳細は [N225 メモリ `reference_cloudflare_webhook_bypass.md`](../../memory/reference_cloudflare_webhook_bypass.md) (著者向け補足) 参照。

---

## 2. kabu API 接続エラー

### 症状

- ブリッジ起動時にステータスバーが「ブローカー: 切断」と表示
- ログに `KabuApiError` `TokenAcquisitionFailed` `Unauthorized` 等

### 主な原因

| 順位 | 原因 | 対処 |
|---|---|---|
| 1 | kabu Station 未起動 | kabu Station を起動 (デスクトップショートカット) |
| 2 | kabu Station の API 設定無効 | kabu Station GUI → 設定 → 「API 設定」タブ → API 利用を有効化 |
| 3 | 本番/検証モード不一致 | appsettings.Local.json の `Kabu.Mode` と kabu Station GUI のモード切替を一致させる |
| 4 | API パスワード不一致 | UI 設定画面から再入力 |
| 5 | Token 期限切れ (8 時間) | ブリッジを再起動 (起動時に再取得) |

### 確認手順

#### Step 1: kabu Station 起動確認
```powershell
Get-Process kabusapi -ErrorAction SilentlyContinue
```

#### Step 2: モード確認
```powershell
$config = Get-Content "$env:LOCALAPPDATA\N225BrokerBridge\appsettings.Local.json" | ConvertFrom-Json
$config.Kabu.Mode  # "Production" or "Verification"
```
ブリッジのモード = kabu Station GUI のモード が一致していること。

#### Step 3: Token 取得テスト
```powershell
$port = if ($config.Kabu.Mode -eq "Production") { 18080 } else { 18081 }
$body = @{ APIPassword = "<api_password>" } | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:$port/kabusapi/token" `
    -Method Post -Body $body -ContentType "application/json"
```
`Token` が返らない場合、kabu Station 側の API 設定 or パスワードを再確認。

### 注意点

- **本番/検証のパスワードは別物**: kabu Station GUI 側でそれぞれ独立に設定
- **Token は約 8 時間で期限切れ**: ブリッジは自動再取得するが、長時間放置すると一時的に失敗するシーンあり
- **kabu Station 起動直後 30 秒は API 不安定**: 起動完了を待ってからブリッジを動かす

---

## 3. Webhook は受信されるが発注されない

### 症状

- ブリッジログに `Received TradingViewSignal` の行は出る
- でも kabu に発注リクエストが飛んでいない (kabu Station の注文一覧に出てこない)

### 主な原因 (確率順)

| 順位 | 原因 | ログメッセージ例 |
|---|---|---|
| 1 | **自動売買トグルが OFF** | `Ignored_AutoTradeDisabled` |
| 2 | 戦略が未登録 (戦略管理画面に追加していない) | `Ignored_unknown_strategy: <name>` |
| 3 | 戦略の `IsEnabled` チェックが OFF | `Ignored_strategy_disabled: <name>` |
| 4 | passphrase 不一致 | `Unauthorized` (HTTP 401) |
| 5 | kabu 接続エラー | `KabuApiError` / `OrderDispatchFailed` |
| 6 | 残高不足・取引時間外などの kabu 側拒否 | `OrderRejected: <kabu のエラーコード>` |

### 確認手順

#### Step 1: ブリッジ UI のステータスバーで自動売買トグルを確認
**最頻原因**。トグルが灰色 (OFF) なら ON に切り替え。

#### Step 2: 戦略管理画面で戦略を確認
ファイル → 戦略 → 戦略管理 で:
- 受信した `alert_name` と一致する戦略が登録済か?
- その戦略の `有効` チェックが ON か?

未登録なら、戦略管理画面の「追加」から登録。

#### Step 3: 直近ログのフィルタ
```powershell
$logDir = "$env:LOCALAPPDATA\N225BrokerBridge\logs"
$latestLog = Get-ChildItem $logDir -Filter "*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Get-Content $latestLog.FullName | Select-String -Pattern "Received|Ignored|Dispatched|Rejected" | Select-Object -Last 20
```

`Ignored_AutoTradeDisabled` が連続していれば → 自動売買 OFF が確定。

#### Step 4: 戦略の発注テスト (手動発注)
ブリッジ UI 左の「手動発注」パネルから 1 枚買い → 実際に kabu に届くか確認。
- 届く: ブリッジ↔kabu は正常、Webhook 側の問題
- 届かない: kabu 接続問題 → §2 へ

---

## 4. HTTP 400 Invalid Hostname

### 症状

- Cloudflare 経由の Webhook が 400 で返る
- TradingView 側で「Webhook delivery failed: 400」が表示される
- ブリッジ側のログには `Received` が出ない (届く前に Cloudflare で弾かれている)

### 原因

Cloudflare Tunnel の `config.yml` で `httpHostHeader` を設定していない。
ブリッジは `Host: localhost` 以外の HTTP リクエストを受け付けない設定になっているため、
Cloudflare が `Host: webhook.your-domain.com` のまま転送すると 400 を返す。

### 対処

`C:\SPB_DATA\.cloudflared\config.yml` を編集:

```yaml
tunnel: <tunnel-id>
credentials-file: C:\SPB_DATA\.cloudflared\<tunnel-id>.json

ingress:
  - hostname: webhook.your-domain.com
    service: http://localhost:8001
    originRequest:
      httpHostHeader: localhost     # ← この 1 行を必ず追加
  - service: http_status:404
```

編集後、cloudflared プロセスを再起動:
```powershell
Get-Process cloudflared | Stop-Process -Force
Start-Process "cloudflared.exe" -ArgumentList "tunnel", "--config", "C:\SPB_DATA\.cloudflared\config.yml", "run" -WindowStyle Hidden
```

### 補足

これは N225 プロジェクトでも 2026-05-13 に発覚した恒久対処。
詳細は著者向けメモリ [`reference_cloudflare_webhook_bypass.md`](../../memory/reference_cloudflare_webhook_bypass.md) 参照。

---

## 5. 起動時例外 / アプリが起動しない

### 症状

- ダブルクリックしてもブリッジ UI が出てこない
- 「.NET Runtime が見つかりません」ダイアログ
- 起動直後にクラッシュ
- Windows Event Log にアプリ例外

### 主な原因

| 順位 | 原因 | 対処 |
|---|---|---|
| 1 | **.NET 8 Desktop Runtime 未インストール** | https://dotnet.microsoft.com/download/dotnet/8.0 から Desktop Runtime をインストール |
| 2 | `appsettings.Local.json` が壊れている (JSON syntax error) | バックアップ取って再生成 (`/setup` の Step 4) |
| 3 | DPAPI 復号失敗 (別 PC からコピーした設定) | DPAPI はユーザー/PC 固有のため、新 PC では `/setup` 再実行 |
| 4 | ポート 8001 が他プロセスに占有されている | `Get-NetTCPConnection -LocalPort 8001` でプロセス特定 → 停止 or ポート変更 |
| 5 | UI レイアウト永続化ファイルが壊れている (ui-layout.json) | `%LOCALAPPDATA%\N225BrokerBridge\ui-layout.json` を削除 (初期状態で再起動) |

### 確認手順

#### Step 1: .NET Runtime 確認
```powershell
dotnet --list-runtimes
```
`Microsoft.WindowsDesktop.App 8.x.x` が含まれていることを確認。

#### Step 2: 設定ファイルの JSON 妥当性
```powershell
try {
    $config = Get-Content "$env:LOCALAPPDATA\N225BrokerBridge\appsettings.Local.json" -Raw | ConvertFrom-Json
    "✅ JSON valid"
} catch {
    "❌ JSON 不正: $($_.Exception.Message)"
}
```
不正なら旧ファイルをバックアップしてから `/setup` の Step 4 を再実行。

#### Step 3: ポート競合確認
```powershell
$conn = Get-NetTCPConnection -LocalPort 8001 -State Listen -EA SilentlyContinue
if ($conn) {
    Get-Process -Id $conn.OwningProcess
}
```
別プロセスが占有していれば停止 or `appsettings.Local.json` の `Webhook.Port` を別ポート (例: 8002) に変更。

#### Step 4: Windows Event Log
```powershell
Get-EventLog -LogName Application -Source ".NET Runtime" -Newest 5 -EA SilentlyContinue
```
直近のアプリ例外スタックトレースを確認。

---

## 6. kabu が「銘柄が見つからない」(Code=4002001) で拒否する

### 症状

- ブリッジから kabu へ `/sendorder/future` を投げると **HTTP 400** が返ってくる
- レスポンス body に `"Code":4002001, "Message":"銘柄が見つからない"`
- TradingView 側のアラートは正常に届き、ブリッジでは `Signal → NewOrder` までは出ている
- kabu Station の注文一覧には何も乗らない (発注成立していない)

### 主な原因 (確率順)

| 順位 | 原因 | 確認ポイント |
|---|---|---|
| 1 | **ブリッジで選択中の銘柄が未解決** (起動直後の数秒間) | ログに `自動売買対象銘柄が未解決 (起動直後/kabu API 応答待ち) のためシグナルを拒否` が出ているか |
| 2 | kabu Station の銘柄解決自体が失敗 (kabu の API パスワード不一致・kabu Station が落ちている等) | 起動時ログに `銘柄解決失敗` が出ていないか・kabu Station が立ち上がっているか |
| 3 | **旧バージョンのブリッジを使っている** (TradingView ティッカー `NK225M1!` を直接 kabu に投げていた) | 送信 body の `"Symbol"` が文字列ティッカーか、数値の銘柄コードか |

### 確認手順

#### Step 1: ブリッジで「自動売買対象銘柄」が確定しているかログで確認

```powershell
$logDir = "$env:LOCALAPPDATA\N225BrokerBridge\logs"
$latestLog = Get-ChildItem $logDir -Filter "*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Get-Content $latestLog.FullName | Select-String -Pattern "自動売買対象銘柄|銘柄解決" | Select-Object -Last 10
```

期待されるログ:
```
銘柄解決成功: 先物コード=NK225micro (限月=202606) → 銘柄コード=161060023 "日経225マイクロ先物 26/06" 2026年6月限
自動売買対象銘柄: 日経225Micro 2026年6月限 (シンボルコード=161060023)
```

`自動売買対象銘柄: 未確定` のままなら kabu の銘柄解決が失敗している。kabu Station の起動と API パスワードを確認 (§2 参照)。

#### Step 2: 送信 body の `Symbol` フィールドを確認

```powershell
Get-Content $latestLog.FullName | Select-String -Pattern "/sendorder/future 送信 body" | Select-Object -Last 1
```

正常な body (修正版以降):
```
/sendorder/future 送信 body={"Password":"***","Symbol":"161060023","Exchange":24,"TradeType":1,...}
```
- `Symbol` が **数値の銘柄コード** (例: `"161060023"` = 日経225Micro 2026年6月限)
- `Password` は `***` でマスクされている

異常な body (旧バージョン、修正前):
```
/sendorder/future 送信 body={"Password":"...","Symbol":"NK225M1!","Exchange":24,...}
```
- `Symbol` が **TradingView ティッカー文字列** になっている → 旧バージョン。最新版に更新してください。

#### Step 3: ブリッジの銘柄選択 UI を確認

ブリッジ UI の手動発注パネル上部の「銘柄」ドロップダウンが、**意図した銘柄** (Mini / Micro) になっているかを確認。
本ブリッジは「**手動発注パネルで選択中の銘柄 = 自動売買の発注先銘柄**」を兼用しているため、ここを切り替えると自動売買の発注先も同時に切り替わる (architecture.md §3.5 参照)。

### 背景・運用上のルール

- TradingView Webhook の `SymbolTicker` フィールド (`"OSE:NK225M1!"` 等) は **発注時に一切使用しない**
- 発注先銘柄は常に「ブリッジで選択中の銘柄の Resolved Symbol Code (kabu 数値コード)」
- これは利用者が「TV では Mini を見ながら、口座資金の都合で Micro を発注する」運用を許容するための仕様
- 詳細は [architecture.md §3.5 自動売買の銘柄ルーティング](architecture.md#35-自動売買の銘柄ルーティング-tv-ティッカー--kabu-銘柄コード) 参照

---

## 7. 手動発注で枚数を 2 枚以上にしても 1 枚しか発注されない

### 症状

- 手動発注パネルの「数量 (枚)」に **2 以上を入力** して買/売/返済ボタンを押しても、**1 枚しか発注されない**
- ログの `PlaceManualNewOrder invoked ... qty=1` が、入力したはずの枚数ではなく `qty=1` になっている

### 原因

UI フレームワーク **WPF-UI 4.3 の `NumberBox` は「Enter キー」または「フォーカス喪失」でしか入力テキストを `Value` プロパティに確定しない**。
そのため「数量に 2 と入力 → そのまま発注ボタンを押す」操作では、値が未確定のまま `OrderQty` が初期値 1 のまま発注されていた。

> 補足: 当初 (2026-05-28) に `OrderQty` を `int` → `double` へ変更する修正を入れたが、これは「double?→int の書き戻し変換失敗」を直したもので、上記の「確定タイミング」問題は別物として残っていた。2026-06-01 の実機 (qty=1 ログ) で再発を確認し、恒久対策を実施。

### 恒久対策 (2026-06-01 実装済み)

発注ボタンの `Click` は WPF の `ButtonBase.OnClick` 仕様上 `Command` 実行より **前** に発火する。これを利用し、押下時に入力値を確定タイミングに依存せず確実に反映する。

- [src/N225BrokerBridge.UI/Views/MainWindow.xaml.cs](../src/N225BrokerBridge.UI/Views/MainWindow.xaml.cs) — `CommitOrderInputs` / `CommitNumberBox` を追加。買/売/返済ボタン押下時に、数量・指値・逆指値の各 NumberBox の **表示テキストを直接パースして `Value` を確定** → `BindingExpression.UpdateSource()` で ViewModel (`OrderQty` 等) へ確実に push する。
- [src/N225BrokerBridge.UI/Views/MainWindow.xaml](../src/N225BrokerBridge.UI/Views/MainWindow.xaml) — 3 つの NumberBox に `x:Name` を付与。数量にも `UpdateSourceTrigger=PropertyChanged` を追加 (指値・逆指値と統一し、同根の弱点を解消)。買/売/返済ボタンに `Click="CommitOrderInputs"` を配線。

副次効果: `Maximum="100"` のクランプが効くため桁間違いの誤発注防止になり、発注確認ダイアログにも正しい枚数が表示される。

### 確認手順 (シミュレータ推奨)

`--simulator` (Mock ブローカー・全フロー稼働) で起動 → 数量に 2 を入力 → 買注文 → 確認ダイアログに「2 枚」と出て、ログに `PlaceManualNewOrder invoked ... qty=2` が出れば修正が効いている。
本修正はブローカーより手前の UI 層 (NumberBox → `OrderQty` の確定) なので、Mock でも実 kabu でも同一経路を通る = 実トレードなしで検証できる。

> ⚠ `--demo` はスクリーンショット専用でバックグラウンドサービスが起動せず**手動発注ボタンが動かない**。動作テストには必ず `--simulator` を使う ([simulator-mode.md](simulator-mode.md) 参照)。

```powershell
$logDir = "$env:LOCALAPPDATA\N225BrokerBridge\logs"
$latestLog = Get-ChildItem $logDir -Filter "*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Get-Content $latestLog.FullName | Select-String -Pattern "PlaceManualNewOrder invoked" | Select-Object -Last 5
```

> 注意: ソース修正後は必ず **リビルド** が必要。アプリ起動中は bin がロックされるため、ブリッジを閉じてからビルドする。

---

## 8. 限月ロール後に縦玉一覧・約定一覧へ旧限月のデータが残る (限月管理)

### 症状

- SQ を跨いで現月 (限月) が変わったのに、ブリッジの**縦玉一覧・約定一覧に前月や旧限月 (例: 3月限・6月限) の建玉/注文が出続ける**
- 株ステーション (kabu Station) 側は新限月で縦玉ゼロ・約定ゼロなのに、ブリッジだけ古い限月のデータを表示している
- 「現月が変わってもブリッジが追従しない」

### 原因

ブリッジのライブ表示が **現月にスコープされていなかった**。kabu の `/positions` (`product=3`) と `/orders` は**全限月を一括で返す**ため、SQ 決済待ちの前月建玉や、過去に手動で建てた旧限月の建玉/注文がそのまま混ざって表示されていた。

加えて、ライブ建玉は従来「起動時の一回 (`BrokerSessionInitializerService` Step 3) + ブリッジ経由の約定差分」だけで更新され、**ブリッジを介さず口座側で消えた建玉 (SQ 限月決済・株ステーション GUI からの手動決済) を追従できず残り続けていた**。

> 補足: 限月決済の口座反映は SQ 当日 (金曜) の引け後に効くことが多く、SQ 当日朝の時点では kabu `/positions` がまだ旧限月建玉を返すことがある。

### 恒久対策 (2026-06-12 実装済み)

**設計方針**: 株ステーション (kabu) を常に正 (source of truth) とし、**ライブ表示は「現在解決済みの現月銘柄」だけにスコープする**。`position-history.json` / `orders-metadata.json` などの**履歴は一切削除しない** (過去ポジションは引き続きポジション履歴で参照可能)。

1. **限月スコープ (現月管理)** — [src/N225BrokerBridge.UI/ViewModels/MainViewModel.cs](../src/N225BrokerBridge.UI/ViewModels/MainViewModel.cs)
   - 現月解決済みの `AvailableInstruments` の銘柄コード集合 (`CurrentMonthSymbolCodes()`) を正とし、建玉一覧・注文一覧への行追加を `IsCurrentMonth()` で現月だけに限定。
   - 限月解決の完了直後に `ApplyContractMonthScope()` を呼び、解決前に積まれた旧限月の行を一覧から除去。
   - 約定一覧の行を限月で判別できるよう `OrderRow` に生の `SymbolCode` を追加 (`PositionRow` は既に保持)。
   - 限月**未解決の間はフィルタしない** (誤って全消ししない安全側)。解決後に旧限月だけ落ちる。
2. **建玉の定期リコンサイル** — [src/N225BrokerBridge.Application/Sync/PositionReconciliationService.cs](../src/N225BrokerBridge.Application/Sync/PositionReconciliationService.cs) (新規 HostedService)
   - 30 秒間隔で kabu `/positions` を取得し、ブリッジのライブ建玉を **kabu のミラー**に保つ (kabu に無い建玉を除去・kabu のみの建玉を追加)。SQ 決済・外部決済を追従。
   - 安全策: 追加・除去とも **2 ティック連続で同状態が続いて初めて確定** (kabu 反映ラグや発注直後の一時的不一致で誤操作しない)。発注は一切しない。自動取引メタの prune はここでは行わず既存フロー任せ (発注直後メタの誤削除回避)。

### 確認手順

ブリッジ再起動 → 起動直後は旧限月が一瞬出ることがあるが、現月解決 (数秒後) で**縦玉一覧・約定一覧が現月のみ**になる。

```powershell
$logDir = "$env:LOCALAPPDATA\N225BrokerBridge\logs"
$latestLog = Get-ChildItem $logDir -Filter "*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Get-Content $latestLog.FullName | Select-String -Pattern "建玉定期リコンサイル|建玉リコンサイル|自動売買対象銘柄" | Select-Object -Last 10
```

期待ログ例:
```
建玉定期リコンサイル起動 (初回=20s 後・間隔=30s・確定=2ティック連続)。
建玉リコンサイル: kabu に存在しない建玉を除去 id=... (SQ 決済/外部決済の追従)
```

### 背景・運用上のルール

- 旧限月の建玉/注文は**履歴として残す** (削除しない)。ライブ一覧の表示だけを現月に絞る。
- 手動建玉は SQ 前に決済するのが原則だが、決済せず SQ 自動決済に任せる運用も許容する。その反映 (金曜引け後) までは kabu が旧限月を返すため、定期リコンサイルが反映後に自動で掃除する。

---

## 9. リサイズした画面サイズが次回起動時に保持されず既定に戻る

### 症状

- ウィンドウサイズを変更して終了 → 次回起動時に変更したサイズではなく**既定サイズ (1280×820) に戻ってしまう**
- 直前まで (数日前まで) は正しく保持されていたのに、急に効かなくなった

### 原因

UI レイアウト保存ファイル `%LOCALAPPDATA%\N225BrokerBridge\ui-layout.json` の `Window` に**極小の壊れ値 (例: `Width=160, Height=28`) が保存**されていた。復元側 (`ApplyUILayout`) は妥当性チェック (幅 400〜4000 / 高さ 300〜3000) でこれを**無効として無視**するため、XAML 既定の 1280×820 に戻っていた。

壊れ値の原因は `SaveUILayout` が**ウィンドウサイズだけ `Width`/`Height` (依存プロパティ) を使っていた**こと (他のレイアウト項目は全て `ActualWidth`/`ActualHeight`)。`MinWidth=1024 / MinHeight=600` があるため `ActualWidth`/`ActualHeight` は決して 160×28 にならないが、**`Width`/`Height` の依存プロパティは MinWidth でクランプされず**、`WindowStyle="None"` + `WindowChrome` のリサイズ経路でサブ最小値を保持したまま保存され破損していた (実描画サイズとは無関係)。

### 恒久対策 (2026-06-12 実装済み)

[src/N225BrokerBridge.UI/Views/MainWindow.xaml.cs](../src/N225BrokerBridge.UI/Views/MainWindow.xaml.cs) — `SaveUILayout` のウィンドウサイズ保存を **`ActualWidth`/`ActualHeight` に統一** (他項目と同じ。MinWidth でクランプされるので壊れ値が原理的に出ない)。最大化中は `RestoreBounds`、最小化中は保存スキップ (前回の良い値を温存)。

### 自己回復

次回起動時、壊れた 160×28 は妥当性チェックで弾かれて一旦既定サイズになるが、5 秒後の自動保存で正しい実サイズが書き込まれ、以降はリサイズしたサイズがそのまま保持される。手動で直す場合は `ui-layout.json` を削除して再起動すれば初期化される (§10.A / §5 の対処と同じ)。

---

## 10. その他の症状

該当する症状がなければ、以下を順に試してください:

### A. 全体的に動作が遅い・固まる
- ログ容量肥大化の可能性 → `%LOCALAPPDATA%\N225BrokerBridge\logs\` の古いログを削除
- ui-layout.json の破損 → ファイル削除して再起動

### B. UI 表示が崩れている
- 解像度・DPI 設定が極端 (4K + 150% スケーリング等) → ui-layout.json を削除して初期化
- Wpf.Ui のテーマファイル不整合 → アプリ再インストール

### C. 注文が「キャンセル」される
- kabu 側で「取引時間外」「呼値刻みエラー」「呼出余力不足」等 → kabu Station の注文履歴で詳細確認

### D. それでも解決しない場合

GitHub Issue で報告してください:
- 連絡先: `<TBD - 著者の GitHub or サポートメール>`
- 含めるべき情報:
  - 症状 (具体的に)
  - `/diagnose` の出力
  - 直近のログ (`%LOCALAPPDATA%\N225BrokerBridge\logs\` の最新 1 ファイル)
  - 環境情報 (OS バージョン、.NET バージョン、kabu Station バージョン)

**含めてはいけない情報** (秘密扱い):
- passphrase
- kabu API パスワード
- 約定 ID / 注文 ID (個別取引情報、必要なら伏字)

---

## 関連ドキュメント

- [CLAUDE.md](../CLAUDE.md) — Claude Code 用プロジェクトガイド
- [README.md](../README.md) — 利用者向け概要 (TBD)
- [.claude/commands/setup.md](../.claude/commands/setup.md) — 初期セットアップコマンド
- [.claude/commands/verify.md](../.claude/commands/verify.md) — 動作確認コマンド
- [.claude/commands/diagnose.md](../.claude/commands/diagnose.md) — 自動診断コマンド
- [docs/adapters/kabu.md](adapters/kabu.md) — kabu API 仕様 + ハマりポイント集

---

## バージョン

- v0.1.0 (2026-05-22、初版ドラフト)
- v0.1.1 (2026-06-01、§7「手動発注で枚数が 1 枚に固定される」恒久対策を追記)
- v0.1.2 (2026-06-12、§8「限月ロール後に旧限月データが残る (限月管理 + 建玉定期リコンサイル)」・§9「リサイズした画面サイズが保持されない」の恒久対策を追記)
