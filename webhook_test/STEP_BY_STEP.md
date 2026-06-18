# Webhook デバッグ ステップバイステップ手順

**対象**: 新 N225BrokerBridge の Webhook 受信機能をローカルでテスト
**前提**: トレード中 → ブリッジは **検証モード (kabu port 18081)** で安全運転 (実発注されない)
**所要時間**: 約 30 分 (準備 15 分 + 7 ケース実行 15 分)

> **2026-05-21 改訂注記**:
> - 受信ポートは **8001** に統一 (本文中の「8000」記述は読み替え)
> - **自動売買トグル**: ブリッジ UI 上のトグルが ON でないと全シグナルが `AutoTradeDisabled_` で弾かれる (デフォルト OFF)
> - cloudflared 設定実体は `C:\SPB_DATA\.cloudflared\config.yml` (locally-managed、`originRequest.httpHostHeader: localhost` 必須)
> - Stage 3 (TV テスト発火) は実環境では実施不能 → 市場時間中の自然発火待ち

> 各ステップの □ を順番に消化してください。前のステップが完了してから次へ進むこと。

---

## STEP 0: 前提確認 (5 分)

- [ ] 0-1. **kabu Station 起動済み** ─ タスクトレイのアイコンが緑。`http://localhost:18080/kabusapi/` または `http://localhost:18081/kabusapi/` がブラウザで開ける
- [ ] 0-2. **port 8000 が空いている** ─ PowerShell で `netstat -ano | findstr ":8000"` を実行 → 何も出なければ OK
- [ ] 0-3. **Insomnia インストール済み** ─ 起動して空のワークスペースが見える
- [ ] 0-4. **テストファイル一式の存在** ─ `c:\Users\takao2\N225TradingSystem\docs\webhook_test\payloads\` に 7 ファイル

---

## STEP 1: ブリッジを検証モードで起動 (3 分)

- [ ] 1-1. デスクトップの **「N225 Bridge Dashboard」** をダブルクリック (またはダッシュボード B を起動)
- [ ] 1-2. ダッシュボード B の **「2. 本番起動」→「🚀 起動」** ボタンを押す
  - cloudflared と ブリッジが連続起動する
  - ヘッダー LED で `Bridge` が緑になるのを待つ (10 秒くらい)
- [ ] 1-3. ブリッジウィンドウが立ち上がったか確認 (`N225BrokerBridge.UI.exe`)

> ダッシュボードを使わず直接起動する場合: `N225BrokerBridge\src\N225BrokerBridge.UI\bin\Debug\net8.0-windows\N225BrokerBridge.UI.exe`

---

## STEP 2: 設定ダイアログで検証モード + passphrase (5 分)

- [ ] 2-1. ブリッジ UI の **「設定」ボタン** を押す
- [ ] 2-2. **kabu ステーション → 接続環境** で「**検証 (port 18081、モック応答・発注されない)**」のラジオを選択
  - 接続先プレビューが `http://localhost:18081/kabusapi` に変わる
- [ ] 2-3. **API パスワード（検証用）** に検証ポート用のパスワードを入力
  - kabu Station の「API パスワード（検証用）」と同じ値
  - すでに保存済みなら未入力で OK (空のままだと前回値を維持)
- [ ] 2-4. **取引暗証番号** が入っていることを確認 (本番と共通、空ならエラーになる)
- [ ] 2-5. **Webhook → パスフレーズ** に `abcdefg` を入力（TV 実テンプレートと同じ値）
- [ ] 2-6. **動作** セクションで「すべての注文発注の前に確認ダイアログを表示する」を **オフ** にする (テスト中はオフ推奨、いちいち OK 押さなくていい)
- [ ] 2-7. **「保存」** を押す → ステータスに `保存しました...` が出る
- [ ] 2-8. **ブリッジを再起動** (一旦閉じて、ダッシュボード B の「2. 起動」をもう一度押す)
  - パスフレーズ/環境は Options 注入のため、再起動が必須
- [ ] 2-9. 再起動後、ログパネルで以下を確認:
  - `kabu 環境=検証 (18081)`
  - `Webhook リスナー起動完了 (受信 URL=http://localhost:8000/webhook/)`

> ここで何かおかしければ STEP 1 からやり直し。先に進まないこと。

---

## STEP 3: TestStrategy を登録 (2 分)

- [ ] 3-1. ブリッジ UI の **「戦略管理」ボタン** を押す
- [ ] 3-2. **「追加」**:
  - AlertName: `TestStrategy`
  - Interval: `5`
  - IsEnabled: ✅
  - Description: (空でも `Webhook テスト用` でも可)
- [ ] 3-3. **「保存」**
- [ ] 3-4. ブリッジを閉じて再度開く必要は **ない** (戦略レジストリは動的に反映)
- [ ] 3-5. メインウィンドウの **戦略一覧** に `TestStrategy / 5 / ✓` が表示されることを確認

---

## STEP 4: Insomnia セットアップ (10 分)

> Insomnia の UI はバージョン (8.x / 9.x / 10.x / 11.x) で多少違います。「Use Local Vault」「Scratch Pad」が出るのは 9.x 以降。出なければ既存ワークスペースで進めてください。

### 4-A. Insomnia 起動と初回モード選択

- [ ] 4-A-1. Insomnia をデスクトップ / スタートメニューから起動
- [ ] 4-A-2. （**初回起動時のみ**）モード選択ダイアログが出たら **「Use Local Vault」** を選択
  - 「Sign Up」「Login」は不要 (ローカルテスト目的のため)
  - 「Scratch Pad」でも動くが永続化しないので Local Vault 推奨
- [ ] 4-A-3. メイン画面が開き、左ペインにプロジェクト or ワークスペース一覧が見える

### 4-B. プロジェクト作成

> Insomnia 9.x 以降は **Project → Collection → Request** の 3 階層。先にプロジェクトという箱を作ります。
> （既存の「Personal Workspace」プロジェクトをそのまま使う場合はこの 4-B はスキップ可）

- [ ] 4-B-1. 左ペイン上部の **「Projects」セクション** または Insomnia ロゴ横のドロップダウン
- [ ] 4-B-2. **「+ New Project」または「Create Project」** をクリック
- [ ] 4-B-3. 名前: **`N225 Trading System`**
  - トップレベル管理用なので、N225 関連の全テスト（Stage 2、他モジュール）も収まる広めの名前で
- [ ] 4-B-4. ストレージタイプ: **「Local Vault」**（Cloud Sync 不要なら）
- [ ] 4-B-5. **「Create」** をクリック
- [ ] 4-B-6. 左ペインがこのプロジェクト内に切り替わる (タイトル部に `N225 Trading System` 表示)

### 4-C. コレクション作成

- [ ] 4-C-1. プロジェクト内で **「Create」ボタンまたは「+」アイコン** をクリック
  - 配置: バージョンによって「右ペイン中央のカード」「上部 Create ボタン」「左ペイン上部の +」
- [ ] 4-C-2. メニューから **「Request Collection」** を選択
  - 隣に「Design Document」「Environment」がある場合あり、真ん中の Request Collection
- [ ] 4-C-3. 名前: **`N225BrokerBridge Webhook Test`**
- [ ] 4-C-4. **「Create」** をクリック
- [ ] 4-C-5. コレクションが開き、左側に空のリクエストツリー、右側に「No request selected」表示

### 4-D. Environment (環境変数) 設定

> ここで `base_url` を変数化すると、Stage 2 で Cloudflare URL に切替えるだけで全リクエスト使い回せます。

- [ ] 4-D-1. 左ペイン **上部** の「No Environment」または環境名表示の **右側にある歯車 (Cog) アイコン** をクリック
  - バージョンによっては「Manage Environments」テキストリンク
- [ ] 4-D-2. ダイアログ「Manage Environments」が開く
- [ ] 4-D-3. 左側の **「Base Environment」** をクリック (デフォルトで選ばれているはず)
- [ ] 4-D-4. 右側の JSON エディタに以下を **完全置換** で貼り付け:
  ```json
  {
    "base_url": "http://localhost:8000"
  }
  ```
- [ ] 4-D-5. **「Done」** または **「Close」** ボタンで閉じる (Insomnia は自動保存)
- [ ] 4-D-6. 左上の環境セレクタに「Base Environment」と表示されていれば適用済み

---

## STEP 5: Insomnia リクエストを 7 個作成 (15 分)

> 共通設定:
> - Method: **POST**
> - URL: **`{{ _.base_url }}/webhook/`** (末尾の `/` 必須、変数は `{{ _.base_url }}` 形式)
> - Headers: **Content-Type: application/json** (Body を JSON にすると自動付与される)
> - Body 中身: 各 payload ファイルをコピペ
>
> 古い Insomnia (8.x 以前) では変数構文が `{{ base_url }}` (`_` なし) です。変数候補が出ない場合は試してみてください。

### 5-A. 最初のリクエスト `01 Auth Failed` を作成

- [ ] 5-A-1. コレクション内で **「New Request」または「+」** ボタンをクリック
  - 左ペインのコレクション名を右クリック → 「New Request」も可
- [ ] 5-A-2. 名前: **`01 Auth Failed`**
- [ ] 5-A-3. Method を **POST** に変更 (デフォルト GET)
- [ ] 5-A-4. URL 欄に **`{{ _.base_url }}/webhook/`** を入力
  - `{{` を打つと変数候補がポップアップで出るので、`base_url` を選択でも OK
  - 入力後、URL がハイライトされていれば変数解決成功
- [ ] 5-A-5. **「Body」タブ** を開く
- [ ] 5-A-6. Body タイプを **「JSON」** に変更 (ドロップダウンから選択)
  - 自動的に Content-Type: application/json ヘッダが付く
- [ ] 5-A-7. Body エディタに [`payloads/01_auth_failed.json`](payloads/01_auth_failed.json) の中身を **完全コピペ** (TV 同形フル構造):
  ```json
  {
    "passphrase": "WRONG_PASS_xxxxx",
    "alert_name": "TestStrategy",
    "time": "2026-05-21T10:00:00Z",
    "exchange": "OSE",
    "ticker": "OSE:NK225M1!",
    "interval": "5",
    "bar": {
      "time": "2026-05-21T10:00:00Z",
      "open": 60500,
      "high": 60680,
      "low": 60305,
      "close": 60565,
      "volume": 600000
    },
    "strategy": {
      "position_size": 1,
      "order_action": "buy",
      "order_contracts": 1,
      "order_price": 60500,
      "order_id": "test-01-auth-failed",
      "market_position": "long",
      "market_position_size": 1,
      "prev_market_position": "flat",
      "prev_market_position_size": 0
    }
  }
  ```
- [ ] 5-A-8. **送信しない** (まだブリッジが準備未完了の可能性、STEP 6 で順次送る)

### 5-B. リクエスト 2-7 を作成 (テンプレ複製方式)

> 1 つずつ「New Request」で作っても良いですが、`01 Auth Failed` を **Duplicate (右クリック)** して名前と Body だけ差し替える方が早いです。

各リクエストとも:
- Method: **POST** (同じ)
- URL: **`{{ _.base_url }}/webhook/`** (同じ)
- Body タイプ: **JSON** (同じ)
- Body 中身のみ差し替え

#### 5-B-1. `02 Bad JSON`
- [ ] 作成 (`01 Auth Failed` を右クリック → Duplicate)
- [ ] 名前変更: **`02 Bad JSON`**
- [ ] Body: 以下に置き換え (壊れた JSON、Insomnia が黄色警告を出すが無視で OK)
  ```
  { "passphrase": "abcdefg", "alert_name": broken json,,,,
  ```

#### 5-B-2. `03 New Buy (flat→long)`
- [ ] 作成
- [ ] Body: [`payloads/03_new_buy.json`](payloads/03_new_buy.json) の中身

#### 5-B-3. `04 Exit Long (long→flat)`
- [ ] 作成
- [ ] Body: [`payloads/04_exit_long.json`](payloads/04_exit_long.json) の中身

#### 5-B-4. `05 Doten (short→long)`
- [ ] 作成
- [ ] Body: [`payloads/05_doten_short_to_long.json`](payloads/05_doten_short_to_long.json) の中身

#### 5-B-5. `06 Ignored (flat→flat)`
- [ ] 作成
- [ ] Body: [`payloads/06_ignored_flat_to_flat.json`](payloads/06_ignored_flat_to_flat.json) の中身

#### 5-B-6. `07 Not Registered`
- [ ] 作成
- [ ] Body: [`payloads/07_not_registered.json`](payloads/07_not_registered.json) の中身

### 5-C. 作成後の確認

- [ ] 5-C-1. 左ペインのコレクション内に **7 つのリクエスト** が並んでいる
- [ ] 5-C-2. リクエスト名の左に **POST** バッジが付いている (青系の色)
- [ ] 5-C-3. 各リクエストを 1 個ずつ選んで:
  - URL に `{{ _.base_url }}/webhook/` が入っている
  - Body タブを開くと JSON が表示される
  - Headers タブで `Content-Type: application/json` が見える

---

## STEP 6: テスト送信 (順番厳守) (10 分)

> 推奨順序: **安全なケース (1, 2, 6, 7) → 発注ケース (3, 4, 5)** の順。検証モードなので 3〜5 でも実発注はされないが、念のため。
>
> ### Insomnia 送信と結果確認の流れ
> 1. 左ペインで対象リクエストを **クリック** (例: `01 Auth Failed`)
> 2. 右ペインに URL / Body が表示される → URL の右にある **「Send」ボタン** を押す
> 3. 下のレスポンスペインに以下が表示される:
>    - **Status**: 右上に色付きで `200 OK` / `400 Bad Request` 等
>    - **Time**: 応答時間 (通常 50ms 以下)
>    - **Body タブ**: レスポンス本文 (テキスト)
>    - **Headers タブ**: レスポンスヘッダ (Content-Type: text/plain 等)
> 4. 期待値と一致するか目視チェック → 本書の □ にチェック
> 5. ブリッジ UI のログパネルを Alt+Tab で確認 → 期待ログが出ているかチェック
> 6. 次のリクエストへ

### Insomnia のショートカット
- `Ctrl + Enter`: 現在のリクエストを送信
- `Ctrl + L`: URL 欄にフォーカス
- `Ctrl + ,`: Preferences (設定)

### 6-1. ▶ `01 Auth Failed` を送信
- [ ] レスポンス: **HTTP 200**
- [ ] レスポンス本文: `Authenticated_Failed`
- [ ] ブリッジログ: `Signal rejected: passphrase mismatch alert=TestStrategy`
- [ ] 戦略一覧の `TestStrategy` 最終受信時刻が **更新されない** (認証失敗なので)

### 6-2. ▶ `02 Bad JSON` を送信
- [ ] レスポンス: **HTTP 400**
- [ ] レスポンス本文: `Bad Request: Invalid JSON.`
- [ ] ブリッジログ: `Webhook parse failed`

### 6-3. ▶ `06 Ignored (flat→flat)` を送信
- [ ] レスポンス: **HTTP 200**
- [ ] レスポンス本文: `Ignored_`
- [ ] ブリッジログ: `Signal ignored strategy=TestStrategy reason=Unhandled transition...`
- [ ] 戦略一覧の `TestStrategy` 最終受信時刻が **更新される** (認証通過 + 戦略登録済なので)

### 6-4. ▶ `07 Not Registered` を送信
- [ ] レスポンス: **HTTP 200**
- [ ] レスポンス本文: `Ignored_`
- [ ] ブリッジログ: `Signal skipped: strategy not enabled alert=UnknownStrategy_NotRegistered`

ここまでで「**経路 + 認証 + Parser + 戦略チェック**」が動作確認できた。

---

### 6-5. ▶ `03 New Buy` を送信 (検証モード = モック応答)
- [ ] レスポンス: **HTTP 200**
- [ ] レスポンス本文: `NewOrderDispatched_`
- [ ] ブリッジログ: `Signal → NewOrder strategy=TestStrategy side=Buy qty=1`
- [ ] **注文一覧** に新しい注文行が追加される (kabu Station 18081 からのモック応答)
- [ ] 戦略一覧の `TestStrategy`: 最終種別=新規 / サイド=買 / 価格=60500

### 6-6. ▶ `04 Exit Long` を送信
- [ ] レスポンス: **HTTP 200**
- [ ] レスポンス本文: `ExitOrderDispatched_`
  - 検証ポートはモックなので、建玉がなくても OK 応答が返る可能性大
  - 建玉なしのエラーが返るならログに `Manual close failed ...`
- [ ] ブリッジログ: `Signal → ExitOrder ...` または `... not found` 系
- [ ] 戦略一覧の `TestStrategy`: 最終種別=返済 / サイド=売

### 6-7. ▶ `05 Doten` を送信
- [ ] レスポンス: **HTTP 200**
- [ ] レスポンス本文: `DotenDispatched_`
- [ ] ブリッジログ: `Signal → Doten ...`
- [ ] 戦略一覧の `TestStrategy`: 最終種別=ドテン / サイド=買

---

## STEP 7: 結果集計 (3 分)

すべて期待どおりだったか確認:

| # | ケース | 期待 | 結果 |
|---|---|---|---|
| 1 | Auth Failed | 200 `Authenticated_Failed` | □ |
| 2 | Bad JSON | 400 `Bad Request` | □ |
| 3 | New Buy | 200 `NewOrderDispatched_` | □ |
| 4 | Exit Long | 200 `ExitOrderDispatched_` | □ |
| 5 | Doten | 200 `DotenDispatched_` | □ |
| 6 | Ignored flat→flat | 200 `Ignored_` | □ |
| 7 | Not Registered | 200 `Ignored_` | □ |

7 ケース中 **6 個以上 PASS** で Stage 1 合格。

ファイル出力もチェック:
- [ ] `%LOCALAPPDATA%\N225BrokerBridge\strategies.json` の `TestStrategy` 行に `lastSignalAt` 更新
- [ ] `%LOCALAPPDATA%\N225BrokerBridge\orders-metadata.json` に新しいエントリ (ケース 3〜5)
- [ ] `%LOCALAPPDATA%\N225BrokerBridge\logs\n225brokerbridge-2026-05-21.log` にすべてのリクエストログ

---

## STEP 8: トラブルシュート

| 症状 | 原因と対処 |
|---|---|
| 404 Not Found | URL の末尾 `/` が抜けている → `/webhook/` にする |
| 405 Method Not Allowed | GET になっている → POST に直す |
| Connection refused | ブリッジが起動していない or port 違い → STEP 1 から |
| 全部 `Authenticated_Failed` | パスフレーズ不一致 → STEP 2-5 の `abcdefg` 入力 + 再起動を確認 |
| 6 番が `Authenticated_Failed` | 同上、パスフレーズ問題 |
| 3〜5 番が `Authenticated_Failed` | 同上 |
| すべて `Ignored_` で `not enabled` | 戦略未登録 → STEP 3 やり直し |
| ブリッジが起動しない | kabu Station 未起動 or 検証ポート用パスワード未設定 → STEP 0 / STEP 2 |
| Internal Server Error 500 | ログ確認 (logs フォルダ) |

---

## STEP 9: Stage 2 (Cloudflare + TV) への進行条件

Stage 1 が 6/7 以上 PASS なら次に進める。

### 9-1. Cloudflare 経路の構成 (前提知識)

旧構成も新構成も Cloudflare → localhost:**8000** を指している:

```
旧: TV → Cloudflare Tunnel → localhost:8000 (N225Trader.exe)
新: TV → Cloudflare Tunnel → localhost:8000 (N225BrokerBridge.UI.exe)
                              ↑ Cloudflare 側の設定は変更不要
```

→ **Cloudflare ingress 変更は不要**。旧 N225Trader.exe を止めて新ブリッジを 8000 で起動すれば、Cloudflare はそのまま新ブリッジへ POST を流す。

> 旧 webhook_server.py (port 8001) は Cloudflare の経路には乗っていないので、停止する/しないは無関係。動いていても新ブリッジのテストには影響しない。

### 9-2. Stage 2 への準備

- [ ] `/webhook` パスの Bypass カスタムルール ([reference_cloudflare_webhook_bypass.md](../../memory/reference_cloudflare_webhook_bypass.md)) が **有効** か確認 (これが無いと TV の Go-http-client/2.0 UA が 403 で弾かれる)
- [ ] 旧 N225Trader.exe (port 8000) を停止
- [ ] 新ブリッジを起動 (port 8000 を引き継ぐ)
- [ ] 新ブリッジのログに `Webhook リスナー起動完了 (受信 URL=http://localhost:8000/webhook/)` を確認

### 9-3. Insomnia で Cloudflare 経路を確認 (TV 発火の前段)

Insomnia の Environment を切替えるだけで、同じ 7 リクエスト群を Cloudflare 経由でテストできる:

- [ ] Insomnia の Environment に新規追加:
  ```json
  { "base_url": "https://webhook.n225trade.com" }
  ```
- [ ] 左上ドロップダウンで切替 → 全 7 リクエストを再送
- [ ] 全部 Stage 1 と同じレスポンスが返れば、Cloudflare 経路 OK

オプション: TV の UA を再現したい場合、各リクエストの Headers タブで `User-Agent: Go-http-client/2.0` を追加。

### 9-4. TV テスト発火 (最終確認) — ⚠ 実環境では実施不能

> **2026-05-21 追記**: TradingView では「アラートを手動でテスト発火する」操作 (Only Once 強制発射等) は提供されていない / ユーザー環境では不可能。
> このステップは **市場時間中の自然発火を待って実機確認** に置き換える。
> Stage 1 + Stage 2 通過時点で Webhook 経路の構築は完結とみなしてよい。

- [ ] 市場時間中、運用戦略のアラート条件が自然に成立した瞬間にシグナルが届く
- [ ] ブリッジログに `Signal →` が出れば本番経路 OK
- [ ] (注: ブリッジの **自動売買トグルを ON** にしていないと `AutoTradeDisabled_` で弾かれる)

---

## 付録: 各ケースの Body JSON (コピペ用)

> すべて TradingView の実アラートテンプレートと同形のフル構造 (time/exchange/bar/strategy.position_size/strategy.order_id 付き)。ブリッジは未使用フィールドを無視するので動作上は問題なく、Stage 2 へそのまま流用可能。

### 01 Auth Failed
```json
{
  "passphrase": "WRONG_PASS_xxxxx",
  "alert_name": "TestStrategy",
  "time": "2026-05-21T10:00:00Z",
  "exchange": "OSE",
  "ticker": "OSE:NK225M1!",
  "interval": "5",
  "bar": {
    "time": "2026-05-21T10:00:00Z",
    "open": 60500,
    "high": 60680,
    "low": 60305,
    "close": 60565,
    "volume": 600000
  },
  "strategy": {
    "position_size": 1,
    "order_action": "buy",
    "order_contracts": 1,
    "order_price": 60500,
    "order_id": "test-01-auth-failed",
    "market_position": "long",
    "market_position_size": 1,
    "prev_market_position": "flat",
    "prev_market_position_size": 0
  }
}
```

### 02 Bad JSON
```
{ "passphrase": "abcdefg", "alert_name": broken json,,,,
```

### 03 New Buy (flat→long buy)
```json
{
  "passphrase": "abcdefg",
  "alert_name": "TestStrategy",
  "time": "2026-05-21T10:00:00Z",
  "exchange": "OSE",
  "ticker": "OSE:NK225M1!",
  "interval": "5",
  "bar": {
    "time": "2026-05-21T10:00:00Z",
    "open": 60500,
    "high": 60680,
    "low": 60305,
    "close": 60565,
    "volume": 600000
  },
  "strategy": {
    "position_size": 1,
    "order_action": "buy",
    "order_contracts": 1,
    "order_price": 60500,
    "order_id": "test-03-new-buy",
    "market_position": "long",
    "market_position_size": 1,
    "prev_market_position": "flat",
    "prev_market_position_size": 0
  }
}
```

### 04 Exit Long (long→flat sell)
```json
{
  "passphrase": "abcdefg",
  "alert_name": "TestStrategy",
  "time": "2026-05-21T10:05:00Z",
  "exchange": "OSE",
  "ticker": "OSE:NK225M1!",
  "interval": "5",
  "bar": {
    "time": "2026-05-21T10:05:00Z",
    "open": 60565,
    "high": 60820,
    "low": 60540,
    "close": 60800,
    "volume": 580000
  },
  "strategy": {
    "position_size": 0,
    "order_action": "sell",
    "order_contracts": 1,
    "order_price": 60800,
    "order_id": "test-04-exit-long",
    "market_position": "flat",
    "market_position_size": 0,
    "prev_market_position": "long",
    "prev_market_position_size": 1
  }
}
```

### 05 Doten Short→Long (short→long buy)
```json
{
  "passphrase": "abcdefg",
  "alert_name": "TestStrategy",
  "time": "2026-05-21T10:10:00Z",
  "exchange": "OSE",
  "ticker": "OSE:NK225M1!",
  "interval": "5",
  "bar": {
    "time": "2026-05-21T10:10:00Z",
    "open": 60800,
    "high": 60900,
    "low": 60450,
    "close": 60600,
    "volume": 620000
  },
  "strategy": {
    "position_size": 1,
    "order_action": "buy",
    "order_contracts": 2,
    "order_price": 60600,
    "order_id": "test-05-doten",
    "market_position": "long",
    "market_position_size": 1,
    "prev_market_position": "short",
    "prev_market_position_size": 1
  }
}
```

### 06 Ignored (flat→flat)
```json
{
  "passphrase": "abcdefg",
  "alert_name": "TestStrategy",
  "time": "2026-05-21T10:15:00Z",
  "exchange": "OSE",
  "ticker": "OSE:NK225M1!",
  "interval": "5",
  "bar": {
    "time": "2026-05-21T10:15:00Z",
    "open": 60600,
    "high": 60620,
    "low": 60560,
    "close": 60580,
    "volume": 540000
  },
  "strategy": {
    "position_size": 0,
    "order_action": "buy",
    "order_contracts": 1,
    "order_price": 60500,
    "order_id": "test-06-ignored",
    "market_position": "flat",
    "market_position_size": 0,
    "prev_market_position": "flat",
    "prev_market_position_size": 0
  }
}
```

### 07 Not Registered
```json
{
  "passphrase": "abcdefg",
  "alert_name": "UnknownStrategy_NotRegistered",
  "time": "2026-05-21T10:20:00Z",
  "exchange": "OSE",
  "ticker": "OSE:NK225M1!",
  "interval": "5",
  "bar": {
    "time": "2026-05-21T10:20:00Z",
    "open": 60580,
    "high": 60620,
    "low": 60530,
    "close": 60560,
    "volume": 510000
  },
  "strategy": {
    "position_size": 1,
    "order_action": "buy",
    "order_contracts": 1,
    "order_price": 60500,
    "order_id": "test-07-notregistered",
    "market_position": "long",
    "market_position_size": 1,
    "prev_market_position": "flat",
    "prev_market_position_size": 0
  }
}
```

---

## 進捗チェックボード

完了したステップに ✅ を付けてください:

- [ ] STEP 0: 前提確認
- [ ] STEP 1: ブリッジ起動 (検証モード)
- [ ] STEP 2: passphrase 設定 + 再起動
- [ ] STEP 3: TestStrategy 登録
- [ ] STEP 4: Insomnia 環境設定
- [ ] STEP 5: 7 リクエスト作成
- [ ] STEP 6: 順次送信 (1, 2, 6, 7, 3, 4, 5 の順)
- [ ] STEP 7: 結果集計
- [ ] STEP 9: Stage 2 への準備 (Stage 1 合格後のみ)
