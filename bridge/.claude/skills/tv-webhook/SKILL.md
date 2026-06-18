---
name: tv-webhook
description: TradingView の戦略アラート Webhook 設定を支援。プラン確認 (Pro 以上必須)、Webhook URL 構築、JSON ペイロード生成、アラート作成手順案内。/setup の Step 7 から呼ばれる。
---

# tv-webhook — TradingView Webhook 設定支援スキル

> ⚠️ **このスキルはドラフト (v0.1.0) です。テスター環境での動作確認が必要です。**

## 目的

TradingView の戦略 (Strategy) アラートからブリッジへ Webhook を飛ばすための設定を、利用者と対話しながら確立する。

## 利用シーン

- `/setup` の Step 7 (TradingView アラート設定案内) から呼ばれる
- 利用者が新しい戦略を追加して Webhook を増設するとき
- アラートが届かないトラブル調査時 (URL / JSON 形式の照合)

---

## 前提条件

### TradingView プラン

**Webhook 機能は有料プラン (Pro 以上) が必須**。Free プランでは利用不可。
利用者に確認:
```
Claude Code 発話:
「TradingView のプランを確認してください:
  1. Free (無料) — Webhook 不可、Pro 以上にアップグレード必須
  2. Pro / Pro+ / Premium — Webhook 利用可

現在のプラン番号を回答してください。」
```

`1` (Free) の場合: 「アップグレードしてから戻ってきてください」で中断。

### Cloudflare Tunnel 経由 URL の準備

事前に `cloudflare-tunnel` skill で `https://webhook.<your-domain>.com/webhook/` 形式の URL が用意されていること。

---

## 実行フロー

### Step 1: Webhook URL の確認

```
Claude Code 発話:
「TradingView から ブリッジへ届く Webhook URL を確認します。
あなたの Cloudflare で設定したドメインは何ですか? (例: webhook.example.com)

入力後、完成系の URL を表示します:
  https://<your-domain>/webhook/
※ 末尾のスラッシュ必須」
```

### Step 2: 戦略の `alert_name` を決める

```
Claude Code 発話:
「TradingView 側で発火する戦略の名前を決めてください (英数字推奨、例: V7_7_StrictA)。
この名前を:
  - TradingView のアラート Name に入れる
  - JSON ペイロードの alert_name に入れる
  - ブリッジ側の「戦略管理」画面に同じ名前で登録する

3 箇所すべてが完全一致する必要があります。
入力してください。」
```

### Step 3: JSON ペイロードを生成して提示

```json
{
  "alert_name": "<利用者入力>",
  "interval": <利用者選択: 1, 5, 15, 60 等>,
  "trade_type": "{{strategy.order.action == 'buy' ? 'new' : 'exit'}}",
  "side": "{{strategy.order.action}}",
  "price": {{close}},
  "passphrase": "<utilities/CLAUDE_PROMPT_TO_LOAD_PASSPHRASE>"
}
```

- `interval`: 戦略の時間足分数 (1m → 1, 5m → 5, 15m → 15, 1h → 60)
- `trade_type`: `new` (新規) or `exit` (返済) — 戦略ロジックに応じて
- `side`: `buy` or `sell` (TV プレースホルダ `{{strategy.order.action}}` で動的に挿入)
- `price`: 発火時の終値 (TV プレースホルダ `{{close}}`)
- `passphrase`: ブリッジの `appsettings.Local.json` の `Webhook.Passphrase` と完全一致が必須

利用者には passphrase を伏字入力させて、最終 JSON を整形して提示。

### Step 4: TradingView 側のアラート作成手順案内

```
Claude Code 発話:
「TradingView でアラートを作成します:
  1. 該当戦略のチャートを開く
  2. 右上の「アラート」(時計アイコン) → 「新しいアラート」
  3. 「条件」タブ: 戦略名を選択 (Strategy: alert() function calls only)
  4. 「通知」タブ:
     - Webhook URL に ON
     - URL: https://<your-domain>/webhook/
  5. 「メッセージ」タブ: 上で生成した JSON を貼り付け
  6. 「アラート名」: <入力した alert_name> と一致させる (任意だが推奨)
  7. 「作成」ボタンで保存

完了したら yes と回答してください。」
```

### Step 5: 発火テスト案内

```
Claude Code 発話:
「TradingView は直接テスト発火する機能がありません (アラート手動発火不可)。
代わりに以下の方法でテストできます:

  方法 A: ブリッジ側でローカル POST テスト (推奨)
    → /verify の Step 5 でローカル発火し、ブリッジが受信するか確認

  方法 B: TradingView でわざと発火する戦略を一時設定
    → 「ストラテジー: 即時 buy → 5 秒後 sell」のような条件で発火確認
    → 確認後は元の戦略に戻す

  方法 C: ブリッジ側の戦略管理画面でモック発注を試す
    → ブリッジ UI から手動で同じ alert_name でテスト

どれを試しますか? (A / B / C / skip)」
```

### Step 6: ブリッジ側の戦略登録確認

```
Claude Code 発話:
「ブリッジ側の戦略管理画面でも同じ <alert_name> を登録してください:
  1. ブリッジ UI のメニュー: 戦略 → 戦略管理
  2. 「追加」ボタン
  3. 戦略名: <入力した alert_name> (完全一致)
  4. Interval (分): <選択した時間足>
  5. 銘柄: NK225M (日経 225 ミニ)
  6. 自動売買: ON
  7. 保存

登録完了したら yes と回答してください。」
```

---

## TradingView の Webhook プレースホルダ集

参考: TradingView が JSON 内で展開できる主なプレースホルダ。

| プレースホルダ | 展開内容 | 例 |
|---|---|---|
| `{{ticker}}` | シンボル名 | `OSE:NK225M2026K` |
| `{{exchange}}` | 取引所 | `OSE` |
| `{{close}}` | 発火時の終値 | `38450.5` |
| `{{open}}` | 始値 | — |
| `{{high}}` `{{low}}` | 高値・安値 | — |
| `{{volume}}` | 出来高 | — |
| `{{time}}` | 発火時刻 (UTC ISO) | `2026-05-22T05:00:00Z` |
| `{{timenow}}` | 現在時刻 (UTC ISO) | — |
| `{{strategy.order.action}}` | 戦略の売買方向 | `buy` / `sell` |
| `{{strategy.order.contracts}}` | 約定枚数 | `1` |
| `{{strategy.order.price}}` | 約定価格 | `38450.5` |
| `{{strategy.position_size}}` | 現在ポジション | `1` / `0` / `-1` |

詳細は TradingView 公式ドキュメント: https://www.tradingview.com/support/solutions/43000529348-webhooks

---

## よくあるミス

### 1. URL 末尾のスラッシュ忘れ

❌ `https://webhook.your-domain.com/webhook`
✅ `https://webhook.your-domain.com/webhook/`

ブリッジは `POST /webhook/` の Path に厳格にマッチさせている。スラッシュ無しだと 404。

### 2. alert_name のスペース・特殊文字

`alert_name` は英数字 + アンダースコアのみ推奨。スペースや日本語は URL エンコード問題を起こす可能性。

### 3. JSON 内の TV プレースホルダを引用符で囲み忘れ

❌ `"side": {{strategy.order.action}}` → `"side": buy` (構文エラー)
✅ `"side": "{{strategy.order.action}}"` → `"side": "buy"` (正常)

### 4. passphrase の直書きをコミット

`alert_name` だけ TV に書いて、`passphrase` を appsettings.Local.json 経由でブリッジ側に保存する設計は安全。
**TV のアラート設定にパスフレーズが直書きされる**ので、TV アカウントの 2FA を必ず有効化。

### 5. アラート上限

TradingView Pro: 同時アクティブアラート 20 個まで。Pro+ で 100 個、Premium で 400 個。
N225 戦略が複数ある場合、上限に注意。

### 6. アラート発火の遅延

TradingView の Webhook は数秒の遅延が発生し得る。スキャルピング戦略では発火→約定までの時間ロスを考慮。

---

## 設計メモ (開発者向け)

### このスキルの設計原則

1. **3 箇所完全一致を強調**: alert_name (TV) ↔ JSON payload ↔ ブリッジ戦略管理 の完全一致を念入りに確認
2. **passphrase の取扱に注意**: TV 側に必ず書く必要があるので、漏洩対策 (TV アカウント 2FA、定期 ローテーション) を案内
3. **テスト発火の限界**: TV から直接テスト発火できないため、ローカル POST テストを案内

### TBD

- [ ] TV プレースホルダの自動補完テンプレートを複数用意 (スキャルプ / スイング / 利確/損切)
- [ ] passphrase ローテーション機能 (定期更新と TV 側案内)
- [ ] アラート設定の TV API による自動化 (TV 公式 API は限定的)

### バージョン

- v0.1.0 (2026-05-22、初版ドラフト)
