# N225BrokerBridge Webhook テスト手順 (Stage 1 / Stage 2)

**作成日**: 2026-05-21
**最終更新**: 2026-05-21 (検証完遂・改訂)
**対象**: 新 N225BrokerBridge の Webhook 受信機能テスト
**ゴール**: Stage 1 (ローカル直接 POST) で経路を確認 → Stage 2 (Cloudflare 経由 POST) で本番経路確認 → 市場時間中の自然発火で最終運用確認

> **2026-05-21 更新**:
> - 受信ポートは **8001** (旧 webhook_server.py と同じ。手順内の「8000」記述は読み替え必要)
> - cloudflared config 実体: `C:\SPB_DATA\.cloudflared\config.yml` (locally-managed)。`originRequest.httpHostHeader: localhost` 必須
> - **自動売買トグル**: ブリッジ UI で ON にしないと全シグナルが `AutoTradeDisabled_` で弾かれる (起動毎に OFF に戻る安全設計)
> - Stage 3 (TV テスト発火) は実環境では実施不能。市場時間中の自然発火を待って実機確認に置き換える

---

## 0. 仕様サマリー

| 項目 | 値 |
|---|---|
| 受信 URL | `http://localhost:8001/webhook/` (POST) ※ 2026-05-21 改訂、旧記述の 8000 から変更 |
| 自動売買ゲート | ブリッジ UI トグルが ON でないと `AutoTradeDisabled_` で全シグナル弾く |
| Content-Type | `application/json` |
| パスフレーズ | テストは `abcdefg` (TV 実テンプレと同じ値、設定ダイアログで先に保存して再起動) |
| 必須フィールド | `alert_name`, `interval` (正の整数文字列), `ticker`, `strategy.order_action`, `strategy.market_position`, `strategy.prev_market_position` |
| 認証スキップ条件 | 設定パスフレーズが空 |
| 戦略登録チェック | `IStrategyRegistry.IsEnabled(alert_name, interval)` が true でないと **Ignored_** で 200 だけ返る |
| 応答 (成功時) | `200 + outcome 名` (例: `NewOrderDispatched_`) |
| 応答 (失敗時) | `400 Bad Request: <reason>` / `405` / `500` |

### シグナル遷移 → Intent

| prev | current | action | Intent |
|---|---|---|---|
| flat | long | buy | NewOrder Buy |
| flat | short | sell | NewOrder Sell |
| long | flat | sell | ExitOrder (Long 全量) |
| short | flat | buy | ExitOrder (Short 全量) |
| long | long | sell | ExitOrder (Long 部分) |
| short | short | buy | ExitOrder (Short 部分) |
| short | long | buy | Doten (Short→Long) |
| long | short | sell | Doten (Long→Short) |
| その他 | — | — | Ignore |

---

## 1. 事前準備

### 1-1. 新ブリッジ Settings ダイアログで passphrase 保存

1. 新ブリッジ `N225BrokerBridge.UI.exe` を起動 (ダッシュボード B → 本番起動でも可)
2. 「設定」を開く
3. **Webhook 受信 → パスフレーズ** に `abcdefg` を入力
4. 動作セクションで「すべての注文発注の前に確認ダイアログを表示する」が ON のままでもよい (テスト用の `Ignored_`/`Authenticated_Failed` ケースは発注しないため出ない、発注ケース 3〜5 でのみ確認ダイアログが出る)
5. 「保存」
6. **新ブリッジを再起動** (パスフレーズは Options 注入なので起動時にのみ読まれる)

### 1-2. StrategyRegistry に TestStrategy 登録

ブリッジ UI 上部の「戦略管理」ボタンから:
- AlertName: `TestStrategy`
- Interval: `5`
- IsEnabled: ✅ チェック
- 保存

> または直接ファイル編集: `%LOCALAPPDATA%\N225BrokerBridge\strategies.json` を以下のようにする (再起動必須):
> ```json
> [
>   {
>     "alertName": "TestStrategy",
>     "interval": 5,
>     "isEnabled": true,
>     "description": "Webhook テスト用"
>   }
> ]
> ```

### 1-3. ポート競合確認

```powershell
netstat -ano | findstr ":8000"
```
`LISTENING` 行が **1 行だけ** (新ブリッジの PID) なら OK。**旧 N225Trader.exe (PID 2680 等) が 8000 を占有していたら停止が必要**。

> 旧 webhook_server.py (port 8001) は Cloudflare 経路に乗っていないため、停止する/しないは新ブリッジテストには無関係。

### 1-4. kabu Station

- ケース 1, 2, 6, 7: 不要 (発注しない)
- ケース 3, 4, 5: **必須** (実弾発注、本番 18080 または検証 18081 のどちらかが起動していること)

検証ポート (18081) で動かしておくと安全 (kabu Station 側がモック応答を返し、実発注されない)。

---

## 2. テストケース一覧

| # | ファイル | 期待 HTTP | 期待 body | 発注の有無 |
|---|---|---|---|---|
| 1 | `payloads/01_auth_failed.json` | 200 | `Authenticated_Failed` | なし |
| 2 | `payloads/02_bad_json.txt` | 400 | `Bad Request: Invalid JSON.` | なし |
| 3 | `payloads/03_new_buy.json` | 200 | `NewOrderDispatched_` | **新規買い 1 枚** |
| 4 | `payloads/04_exit_long.json` | 200 | `ExitOrderDispatched_` | **返済 (建玉が必要)** |
| 5 | `payloads/05_doten_short_to_long.json` | 200 | `DotenDispatched_` | **ドテン (short 建玉が必要)** |
| 6 | `payloads/06_ignored_flat_to_flat.json` | 200 | `Ignored_` | なし |
| 7 | `payloads/07_not_registered.json` | 200 | `Ignored_` | なし (alert_name 未登録) |

> ケース 4, 5 は前提状態 (long/short 建玉) が必要。先にケース 3 を実行して long を作っておくか、手動で建玉を仕込む。

---

## 3. 実行方法 (3 通り)

### 方法 A: PowerShell 一括スクリプト (推奨)

```powershell
cd c:\Users\takao2\N225TradingSystem\docs\webhook_test
pwsh -File test_all.ps1                  # 1, 2, 6, 7 のみ (発注なし)
pwsh -File test_all.ps1 -IncludeOrder    # 3, 4, 5 も含む (発注あり)
```

各ケースの結果が `PASS/FAIL` 付きで色付きで表示される。

### 方法 B: 個別 curl

```powershell
# ケース 1 (認証失敗)
curl.exe -X POST http://localhost:8000/webhook/ `
    -H "Content-Type: application/json" `
    --data "@payloads/01_auth_failed.json" -i

# ケース 3 (新規買い、発注される)
curl.exe -X POST http://localhost:8000/webhook/ `
    -H "Content-Type: application/json" `
    --data "@payloads/03_new_buy.json" -i
```

### 方法 C: Insomnia

1. 新規 Request 作成
2. POST `http://localhost:8000/webhook/`
3. Body → JSON → `payloads/*.json` の中身を貼り付け
4. Send

---

## 4. 確認ポイント

### 4-1. ブリッジ UI ログパネル

- ケース 1: `Signal rejected: passphrase mismatch alert=TestStrategy`
- ケース 2: `Webhook parse failed ...`
- ケース 3-5: `Signal → NewOrder/ExitOrder/Doten ...`
- ケース 6-7: `Signal ignored ...` または `Signal skipped: strategy not enabled ...`

### 4-2. ブリッジ UI 戦略一覧 (最終受信シグナル)

ケース 3〜7 を実行後、`TestStrategy` 行の以下が更新されているか:
- `LastSignalAt` (受信時刻)
- `LastTradeType` (新規/返済/ドテン/—)
- `LastSide` (買/売)
- `LastPrice`

### 4-3. ファイル出力 (永続化)

- 戦略の `lastSignalAt` 更新 → `%LOCALAPPDATA%\N225BrokerBridge\strategies.json`
- 注文発注 → `%LOCALAPPDATA%\N225BrokerBridge\orders-metadata.json` に新規エントリ
- Serilog ログ → `%LOCALAPPDATA%\N225BrokerBridge\logs\n225brokerbridge-<日付>.log`

### 4-4. ブリッジ UI 注文一覧

ケース 3, 4, 5 でのみ新しい注文行が追加される (kabu API レスポンス次第)。

---

## 5. Stage 2: Cloudflare Tunnel + TV テスト

Stage 1 完了後:

### 5-1. Cloudflare 経路の構成 (前提知識)

旧構成も新構成も Cloudflare → localhost:**8000** を指している:

```
旧: TV → Cloudflare Tunnel → localhost:8000 (N225Trader.exe)
新: TV → Cloudflare Tunnel → localhost:8000 (N225BrokerBridge.UI.exe)
                              ↑ Cloudflare 側の設定は変更不要
```

→ **Cloudflare ingress 変更も、webhook_server.py 停止も不要**。旧 N225Trader.exe を停止して新ブリッジを 8000 で起動するだけで本番経路が新ブリッジへ切替わる。

### 5-2. Cloudflare Bypass ルール確認

[reference_cloudflare_webhook_bypass.md](../../memory/reference_cloudflare_webhook_bypass.md) のカスタムルール (`/webhook` パスを全セキュリティ機能から Bypass) が有効か確認。**これがないと TV の Go-http-client/2.0 が 403 で弾かれる**。

### 5-3. TV テスト発火 — ⚠ 実環境では実施不能 (2026-05-21 改訂)

TradingView でアラートを手動テスト発火する操作 (Only Once 強制発射等) は提供されておらず、ユーザー環境では実施不能。

→ **市場時間中の自然発火を待って実機確認** に置き換える。Stage 2 (5-4 のリモート POST) 通過時点で経路の構築は完結とみなしてよい。

(関連: `memory/feedback_tv_alert_test_unavailable.md`)

### 5-4. リモート POST 確認 (Cloudflare 経由)

ブリッジ UI とは別ターミナルで TV を真似た UA で送る:

```powershell
curl.exe -X POST https://webhook.n225trade.com/webhook `
    -H "Content-Type: application/json" `
    -H "User-Agent: Go-http-client/2.0" `
    --data "@payloads/06_ignored_flat_to_flat.json" -i
```

期待: `HTTP/2 200` + `Ignored_`。403 が返るなら Bypass ルールが効いていない。

---

## 6. Stage 3: 実弾稼働

問題なければ:
1. `strategies.json` を編集 (または UI から) 実運用戦略を有効化
2. TV のアラート Frequency を `Once Per Bar Close` 等の運用設定へ
3. 通常運用開始

---

## 付録: TV からの素 JSON の例 (パース可否確認用)

TV の {{ }} プレースホルダ展開後の典型例:
```json
{
  "passphrase": "abcdefg",
  "alert_name": "V7-7-fixed",
  "interval": "5",
  "ticker": "OSE:NK225M1!",
  "strategy": {
    "order_action": "buy",
    "market_position": "long",
    "prev_market_position": "flat",
    "order_contracts": 1,
    "market_position_size": 1,
    "prev_market_position_size": 0,
    "order_price": 60500
  }
}
```

`interval` は文字列で来る (TV 仕様)。Parser はこれを int 化する。
