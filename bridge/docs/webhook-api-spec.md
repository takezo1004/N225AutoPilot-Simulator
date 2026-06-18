# Webhook API 仕様 (Webhook API Spec)

**バージョン**: 1.0.0
**作成日**: 2026-05-26
**最終更新**: 2026-05-26
**対象**: HttpWebhookListener (`src/N225BrokerBridge.Infrastructure/Webhooks/`)

---

## 1. このドキュメントの目的

N225BrokerBridge が公開する HTTP Webhook エンドポイントの**完全仕様**を定義する。
TradingView Pine Strategy のアラート設定者、検証用 cURL / Insomnia / Postman のリクエスト組立者、Cloudflare Tunnel の経路設計者が参照する。

関連:
- [functional-spec.md](./functional-spec.md) §7 — 受信から発注までの機能仕様
- [sequence-diagrams.md](./sequence-diagrams.md) S-02〜S-05 — 各シナリオ
- `../../docs/webhook_full_setup_manual.md` — Cloudflare Tunnel 構築マニュアル (リポジトリ root)

---

## 2. エンドポイント

### 2.1 基本情報

| 項目 | 値 |
|---|---|
| プロトコル | HTTP (HTTPS は Cloudflare Tunnel が終端) |
| ホスト | `localhost` (外部直接アクセス不可) |
| ポート | `8001` (デフォルト, `appsettings.json` の `Webhook.Port` で変更可) |
| パス | `/webhook` (`Webhook.Path` で変更可) |
| メソッド | `POST` のみ |
| Content-Type | `application/json` |

### 2.2 完全 URL (内部)

```
http://localhost:8001/webhook
```

### 2.3 外部公開 URL (Cloudflare Tunnel 経由)

```
https://<your-domain>/webhook
```

cloudflared 経路で `localhost:8001` にプロキシ。詳細は `webhook_full_setup_manual.md`。

---

## 3. リクエスト仕様

### 3.1 ヘッダー

| ヘッダー | 必須 | 値 / 用途 |
|---|---|---|
| Content-Type | ✅ | `application/json` (`charset=utf-8` 推奨) |
| Content-Length | (HttpListener が処理) | bytes |

⚠️ TradingView の Webhook 設定では Content-Type ヘッダを明示しないと自動付与されないことがある。アラート画面の Webhook URL 欄にヘッダ指定する。

### 3.2 ボディ (JSON ペイロード)

JSON スキーマ:

```json
{
  "passphrase": "string (任意、設定で必須化される)",
  "alert_name": "string (必須)",
  "interval": "integer (必須、分単位、>0)",
  "ticker": "string (必須、TV ティッカー、※本ブリッジでは無視)",
  "strategy": {
    "order_action": "string (必須、'buy' / 'sell')",
    "market_position": "string (必須、'flat' / 'long' / 'short')",
    "prev_market_position": "string (必須、'flat' / 'long' / 'short')",
    "order_contracts": "number (必須、>0)",
    "market_position_size": "number (必須、>=0)",
    "prev_market_position_size": "number (任意、>=0)",
    "order_price": "number (任意、>=0、負値は 0 に正規化)"
  }
}
```

### 3.3 各フィールド詳細

#### passphrase

| 属性 | 値 |
|---|---|
| 型 | string |
| 必須 | 設定による (空 = 認証スキップ + 警告ログ) |
| 用途 | `WebhookListenerOptions.Passphrase` と完全一致比較 |
| ログ | 平文記録なし (`***` でマスク) |

#### alert_name

| 属性 | 値 |
|---|---|
| 型 | string (非空) |
| 必須 | ✅ |
| 用途 | `IStrategyRegistry.IsEnabled(alertName, interval)` での主キー |
| 例 | "V7_7_Long_5min", "S28_v5_alpha" |

#### interval

| 属性 | 値 |
|---|---|
| 型 | integer (>0) |
| 必須 | ✅ |
| 用途 | 戦略主キーの一部 / 建玉・注文の `Interval` フィールド |
| 例 | `1`, `5`, `15`, `60`, `240`, `1440` (分単位) |

#### ticker

| 属性 | 値 |
|---|---|
| 型 | string |
| 必須 | ✅ (バリデーション対象) |
| 用途 | **本ブリッジでは無視**。発注銘柄は `IAutoTradeInstrumentProvider.ResolvedSymbolCode` が決定 |
| 例 | `"OSE:NK225M1!"`, `"OSE:NK225MC1!"` |

⚠️ ticker は受信ログには残るが、発注時の銘柄選択には使われない。これにより「TV では Mini を監視 → kabu では Micro を発注」といった運用を許容。

#### strategy.order_action

| 属性 | 値 |
|---|---|
| 型 | string |
| 値域 | `"buy"` / `"sell"` |
| 必須 | ✅ |
| 用途 | 売買方向の決定 |

#### strategy.market_position

| 属性 | 値 |
|---|---|
| 型 | string |
| 値域 | `"flat"` / `"long"` / `"short"` |
| 必須 | ✅ |
| 用途 | アラート発火時点の戦略ポジション |

#### strategy.prev_market_position

| 属性 | 値 |
|---|---|
| 型 | string |
| 値域 | `"flat"` / `"long"` / `"short"` |
| 必須 | ✅ |
| 用途 | アラート発火直前の戦略ポジション。Intent 解釈の主軸 |

#### strategy.order_contracts

| 属性 | 値 |
|---|---|
| 型 | number (decimal) |
| 必須 | ✅ |
| 制約 | `> 0` (0 以下は例外) |
| 用途 | 注文枚数。double → int 変換は AwayFromZero 丸め |
| 例 | `1`, `2`, `1.0` (整数化) |

#### strategy.market_position_size

| 属性 | 値 |
|---|---|
| 型 | number |
| 必須 | ✅ |
| 制約 | `>= 0` |
| 用途 | 発火後の予想ポジションサイズ。部分返済判定に使う |

#### strategy.prev_market_position_size

| 属性 | 値 |
|---|---|
| 型 | number |
| 必須 | 任意 |
| 用途 | 発火前のポジションサイズ (将来の検証用) |

#### strategy.order_price

| 属性 | 値 |
|---|---|
| 型 | number (decimal) |
| 必須 | 任意 |
| 値域 | `>= 0` (負値は 0 に正規化 = 成行扱い) |
| 用途 | 指値発注時の価格。`0` は成行発注を意味する |

### 3.4 サンプルペイロード

#### サンプル 1: 新規買 (flat → long)

```json
{
  "passphrase": "your-strong-passphrase",
  "alert_name": "V7_7_Long_5min",
  "interval": 5,
  "ticker": "OSE:NK225M1!",
  "strategy": {
    "order_action": "buy",
    "market_position": "long",
    "prev_market_position": "flat",
    "order_contracts": 1,
    "market_position_size": 1,
    "prev_market_position_size": 0,
    "order_price": 0
  }
}
```

#### サンプル 2: 全量返済 (long → flat)

```json
{
  "passphrase": "your-strong-passphrase",
  "alert_name": "V7_7_Long_5min",
  "interval": 5,
  "ticker": "OSE:NK225M1!",
  "strategy": {
    "order_action": "sell",
    "market_position": "flat",
    "prev_market_position": "long",
    "order_contracts": 1,
    "market_position_size": 0,
    "prev_market_position_size": 1,
    "order_price": 0
  }
}
```

#### サンプル 3: 部分返済 (long → long、サイズ縮小)

```json
{
  "passphrase": "your-strong-passphrase",
  "alert_name": "V7_7_Long_5min",
  "interval": 5,
  "ticker": "OSE:NK225M1!",
  "strategy": {
    "order_action": "sell",
    "market_position": "long",
    "prev_market_position": "long",
    "order_contracts": 1,
    "market_position_size": 1,
    "prev_market_position_size": 2,
    "order_price": 0
  }
}
```

#### サンプル 4: ドテン (short → long)

```json
{
  "passphrase": "your-strong-passphrase",
  "alert_name": "V7_7_Long_5min",
  "interval": 5,
  "ticker": "OSE:NK225M1!",
  "strategy": {
    "order_action": "buy",
    "market_position": "long",
    "prev_market_position": "short",
    "order_contracts": 1,
    "market_position_size": 1,
    "prev_market_position_size": 1,
    "order_price": 0
  }
}
```

#### サンプル 5: 指値発注 (`order_price > 0`)

```json
{
  "passphrase": "your-strong-passphrase",
  "alert_name": "V7_7_Long_5min",
  "interval": 5,
  "ticker": "OSE:NK225M1!",
  "strategy": {
    "order_action": "buy",
    "market_position": "long",
    "prev_market_position": "flat",
    "order_contracts": 1,
    "market_position_size": 1,
    "prev_market_position_size": 0,
    "order_price": 38500
  }
}
```

---

## 4. レスポンス仕様

### 4.1 ステータスコード

| HTTP | 意味 | レスポンス Body |
|---|---|---|
| **200** | 正常受領 (発注実施 or Ignore) | outcome の class name (例: `"NewOrderDispatched"`) |
| **400** | JSON パース失敗 / 必須フィールド欠落 | エラーメッセージ (text/plain) |
| **405** | POST 以外の HTTP メソッド | `"Method Not Allowed"` |
| **500** | 内部例外 | 詳細は出さない (ログのみ) |

### 4.2 outcome 種別 (200 OK のとき返る class name)

| outcome | 意味 | 発注された? |
|---|---|---|
| `AutoTradeDisabled_` | 自動売買グローバル OFF | ❌ |
| `Authenticated_Failed` | パスフレーズ不一致 | ❌ |
| `Interpretation_Failed` | Interpret で例外 | ❌ |
| `Ignored_` | 戦略未登録 / 銘柄未解決 / Intent=Ignore | ❌ |
| `NewOrderDispatched_` | 新規発注実施 | ✅ |
| `ExitOrderDispatched_` | 返済発注実施 | ✅ |
| `DotenDispatched_` | ドテン実施 | ✅ |

⚠️ 200 OK = 「ブリッジが正常に受信した」だけで「発注された」を保証しない。発注の成否は outcome を確認すること。

### 4.3 セキュリティ判断

`Authenticated_Failed` も 200 OK で返すのは、HTTP ステータスからパスフレーズ正否を推測できないようにするため。攻撃者は body の class name で初めて失敗を知る。

---

## 5. 認証

### 5.1 パスフレーズ照合

`SignalHandler` で:
```csharp
var passOk = _authenticator.Authenticate(payload.Passphrase);
```

実装は `WebhookListenerOptions.Passphrase` との完全一致比較。

### 5.2 設定値の安全な保存

`appsettings.Local.json` に DPAPI 暗号化された値で保存される (`enc:` プレフィックス)。詳細は [data-spec.md](./data-spec.md) §3。

### 5.3 パスフレーズが空の場合

`Passphrase = ""` または未設定の場合:
- 警告ログ "passphrase not configured — all signals will be accepted" を起動時に 1 回出す
- 全シグナル受け入れ (検証目的)

本番では必ず非空のパスフレーズを設定する。

---

## 6. バリデーション詳細

### 6.1 JSON パース失敗

以下の場合は 400 を返す:

- Content-Type が application/json でない
- Body が JSON として無効
- 必須フィールド (`alert_name`, `interval`, `ticker`, `strategy.*`) が欠落
- `interval` が 0 以下 / 整数外
- `order_action` が `"buy"` / `"sell"` 以外
- `market_position` / `prev_market_position` が `"flat"` / `"long"` / `"short"` 以外

### 6.2 正規化

- `order_contracts` / `market_position_size`: double → int (AwayFromZero 丸め)
- `order_price`: 負値 → 0 (成行扱い)
- 文字列フィールドのトリム (前後空白除去)

### 6.3 JSON 設定

`SignalPayloadParser` の JSON 解析設定:

```csharp
var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,  // "Passphrase" / "passphrase" 両対応
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip
};
```

---

## 7. TradingView 設定例

TradingView アラートの Webhook URL と Message を以下のように設定する。

### 7.1 Webhook URL

```
https://your-domain.com/webhook
```

### 7.2 メッセージ (Pine Strategy `alert()` 用テンプレート)

```json
{
  "passphrase": "your-strong-passphrase",
  "alert_name": "{{strategy.order.alert_message}}",
  "interval": {{interval}},
  "ticker": "{{ticker}}",
  "strategy": {
    "order_action": "{{strategy.order.action}}",
    "market_position": "{{strategy.market_position}}",
    "prev_market_position": "{{strategy.prev_market_position}}",
    "order_contracts": {{strategy.order.contracts}},
    "market_position_size": {{strategy.market_position_size}},
    "prev_market_position_size": {{strategy.prev_market_position_size}},
    "order_price": {{strategy.order.price}}
  }
}
```

> `{{...}}` は TradingView の placeholder。alert 発火時に動的に展開される。

### 7.3 Pine 側の `alert()` 呼出

```pine
//@version=6
strategy("MyStrategy", overlay=true, ...)

if longCondition
    strategy.entry("Long", strategy.long, alert_message="V7_7_Long_5min")

if exitLongCondition
    strategy.close("Long", alert_message="V7_7_Long_5min")
```

---

## 8. cURL での検証

### 8.1 ローカル直接 (Cloudflare Tunnel なし)

```bash
curl -X POST http://localhost:8001/webhook \
  -H "Content-Type: application/json" \
  -d '{
    "passphrase": "your-strong-passphrase",
    "alert_name": "Test",
    "interval": 5,
    "ticker": "OSE:NK225M1!",
    "strategy": {
      "order_action": "buy",
      "market_position": "long",
      "prev_market_position": "flat",
      "order_contracts": 1,
      "market_position_size": 1,
      "prev_market_position_size": 0,
      "order_price": 0
    }
  }'
```

### 8.2 Cloudflare Tunnel 経由

```bash
curl -X POST https://your-domain.com/webhook \
  -H "Content-Type: application/json" \
  -d '@payload.json'
```

`payload.json` は §3.4 のサンプルから選択。

### 8.3 PowerShell での検証

```powershell
$body = @{
  passphrase = "your-strong-passphrase"
  alert_name = "Test"
  interval = 5
  ticker = "OSE:NK225M1!"
  strategy = @{
    order_action = "buy"
    market_position = "long"
    prev_market_position = "flat"
    order_contracts = 1
    market_position_size = 1
    prev_market_position_size = 0
    order_price = 0
  }
} | ConvertTo-Json -Depth 3

Invoke-RestMethod -Uri "http://localhost:8001/webhook" -Method POST `
  -ContentType "application/json" -Body $body
```

---

## 9. 検証ペイロードと期待 outcome

下表は実機検証で使う組合せの代表例。

| シナリオ | prev | curr | action | 期待 Intent | 期待 outcome |
|---|---|---|---|---|---|
| 新規買 | flat | long | buy | NewOrderIntent(Buy) | `NewOrderDispatched_` |
| 新規売 | flat | short | sell | NewOrderIntent(Sell) | `NewOrderDispatched_` |
| 全量返済 (買→平) | long | flat | sell | ExitOrderIntent(全量) | `ExitOrderDispatched_` |
| 全量返済 (売→平) | short | flat | buy | ExitOrderIntent(全量) | `ExitOrderDispatched_` |
| 部分返済 (買縮小) | long | long | sell | ExitOrderIntent(部分) | `ExitOrderDispatched_` |
| 部分返済 (売縮小) | short | short | buy | ExitOrderIntent(部分) | `ExitOrderDispatched_` |
| ドテン (売→買) | short | long | buy | DotenIntent | `DotenDispatched_` |
| ドテン (買→売) | long | short | sell | DotenIntent | `DotenDispatched_` |
| 無効遷移 (flat→flat) | flat | flat | * | IgnoreIntent | `Ignored_` |
| パスフレーズ違 | * | * | * | - | `Authenticated_Failed` |
| 自動売買 OFF | * | * | * | - | `AutoTradeDisabled_` |

---

## 10. エラー処理ポリシー

| 状況 | 動作 |
|---|---|
| JSON パース失敗 | 400 / 詳細はサーバログのみ / クライアントには簡素なメッセージ |
| バリデーション失敗 | 400 |
| 認証失敗 | 200 + `Authenticated_Failed` (情報漏洩防止) |
| 戦略未登録 / 無効 | 200 + `Ignored_` |
| 銘柄未解決 | 200 + `Ignored_` |
| UseCase 内で kabu Rejected | 200 + `NewOrderDispatched_` / `ExitOrderDispatched_` (outcome に詳細) |
| UseCase 内で NetworkError | 同上 (Order の terminal state が Rejected) |
| 予期しない例外 | 500 / サーバログに完全な stack trace |

---

## 11. レート制限・スパイク対策 (将来課題)

現状: レート制限なし。

将来検討: 1 秒以内の重複アラートを同一視するための idempotency-key ヘッダ対応。

---

## 12. 監査ログ

各リクエストは以下の形式でログ記録される (`HttpWebhookListener`):

```
[2026-05-26T07:00:00.123+09:00 INF Webhook] Received POST /webhook from 127.0.0.1
[2026-05-26T07:00:00.124+09:00 INF SignalHandler] HandleAsync alert=V7_7_Long_5min interval=5
[2026-05-26T07:00:00.234+09:00 INF SignalHandler] Result outcome=NewOrderDispatched
```

詳細はログレベル `Information` 以上。`SourceContext` で発生元クラス追跡可能。

---

## 13. 既知の制約

| 制約 | 内容 | 回避策 |
|---|---|---|
| ticker の無視 | TV ティッカーは無視され、kabu の銘柄選択は UI で決定 | UI の銘柄選択を起動時に確認 |
| Pine `strategy.market_position_size` の double 型 | TV は整数の場合も `1.0` で送る | int 変換は AwayFromZero |
| Pine 戦略の `alert_message` を `alert_name` に流用 | 同じ戦略の複数 alert を区別したい場合は alert_message を変える | 戦略レジストリで (AlertName, Interval) で識別 |
| 認証失敗の 200 OK 応答 | 攻撃者には正否がわからない代わりに、誤入力ユーザーも気付きにくい | クライアントは outcome を確認 |
| Cloudflare Tunnel WAF | 一部 JSON 構造が WAF にブロックされる | `webhook_full_setup_manual.md` の Bypass ルール参照 |

---

## 14. 変更履歴

| バージョン | 日付 | 変更内容 |
|---|---|---|
| 1.0.0 | 2026-05-26 | 初版作成 |
