# 永続化データ仕様 (Data Spec)

**バージョン**: 1.0.0
**作成日**: 2026-05-26
**最終更新**: 2026-05-26
**対象**: `%LOCALAPPDATA%/N225BrokerBridge/` 配下の全ファイル

---

## 1. このドキュメントの目的

N225BrokerBridge が**ローカル PC に書き込むすべてのファイル**のスキーマ・タイミング・関係を網羅する。
バックアップ / 移行 / トラブルシュート / DB 移行検討の根拠資料とする。

関連:
- [class-design.md](./class-design.md) §4.2 (Persistence) — 書き込むクラス
- [functional-spec.md](./functional-spec.md) §5 (設定保存フロー)
- [operations.md](./operations.md) — バックアップ運用

---

## 2. ファイル一覧 (Overview)

| パス (相対 `%LOCALAPPDATA%/N225BrokerBridge/`) | 内容 | 暗号化 | 書込頻度 |
|---|---|---|---|
| `appsettings.Local.json` | 接続資格情報・ユーザー設定 | DPAPI | 設定保存時のみ |
| `strategies.json` | 戦略レジストリ (CRUD + 最終シグナル履歴) | なし | 戦略 CRUD / シグナル受信時 |
| `orders-metadata.json` | 注文の Strategy/Interval/TradeMode メタ | なし | 注文 Accept ごと |
| `auto-positions.json` | 自動取引建玉メタ | なし | 約定 / Position close ごと |
| `position-history.json` | 決済済み実現損益の履歴 (追記・恒久) | なし | 返済約定ごと ([position-history-spec.md](./position-history-spec.md)) |
| `ui-layout.json` | UI レイアウト (ウィンドウ・カラム幅) | なし | 5 秒間隔 (Dirty 時のみ) |
| `logs/n225brokerbridge-YYYY-MM-DD.log` | 構造化ログ | なし | 連続 (7 日保持) |

`%LOCALAPPDATA%` は通常 `C:\Users\<user>\AppData\Local`。

---

## 3. appsettings.Local.json

### 3.1 ファイル位置

```
%LOCALAPPDATA%/N225BrokerBridge/appsettings.Local.json
```

### 3.2 書込クラス / 読込クラス

- 書込: `LocalSettingsStore.Save(LocalSettingsValues)`
- 読込: `LocalSettingsStore.Load()` → `LocalSettingsValues`
- 起動時に `App.BootstrapAsync` が読み込んで DI Options に PostConfigure

### 3.3 スキーマ

```json
{
  "Webhook": {
    "Passphrase": "enc:Base64エンコードDPAPI暗号化テキスト",
    "Port": 8001
  },
  "Kabu": {
    "Environment": "Production",
    "ApiPassword": "enc:...",
    "ApiPasswordTest": "enc:...",
    "OrderPassword": "enc:..."
  },
  "Behavior": {
    "RequireConfirmBeforeOrder": true
  }
}
```

### 3.4 各フィールド詳細

#### Webhook.Passphrase

| 属性 | 値 |
|---|---|
| 型 | string |
| 暗号化 | ✅ DPAPI (`enc:` プレフィックス) |
| 用途 | `SignalHandler` の Authenticate 比較 |
| 空時挙動 | 警告 + 全シグナル受け入れ (検証用) |

#### Webhook.Port

| 属性 | 値 |
|---|---|
| 型 | int |
| 暗号化 | なし |
| デフォルト | 8001 |

#### Kabu.Environment

| 属性 | 値 |
|---|---|
| 型 | string |
| 値域 | `"Production"` / `"Verification"` |
| 暗号化 | なし |
| 用途 | `KabuOptions.BaseUrl` の切替 (将来。現在は固定で localhost:18080) |

#### Kabu.ApiPassword / ApiPasswordTest / OrderPassword

| 属性 | 値 |
|---|---|
| 型 | string |
| 暗号化 | ✅ DPAPI |
| 用途 | `KabuTokenService` (API)、`KabuApiClient.SendOrderAsync` (Order) |

#### Behavior.RequireConfirmBeforeOrder

| 属性 | 値 |
|---|---|
| 型 | bool |
| 暗号化 | なし |
| デフォルト | true |
| 用途 | 手動買/売/返済/キャンセル 4 操作の確認ダイアログ表示 |

### 3.5 DPAPI 暗号化フォーマット

`enc:` プレフィックスの後に Base64 エンコードされた `ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser)` の結果が続く。

```
enc:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAAxxxxxxxxxxx...
```

- スコープ: `CurrentUser` (別ユーザーは復号不可)
- 別 PC への移行不可 (PC・ユーザー固有鍵で暗号化)

### 3.6 ファイル例

```json
{
  "Webhook": {
    "Passphrase": "enc:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA...",
    "Port": 8001
  },
  "Kabu": {
    "Environment": "Production",
    "ApiPassword": "enc:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA...",
    "ApiPasswordTest": "enc:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA...",
    "OrderPassword": "enc:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA..."
  },
  "Behavior": {
    "RequireConfirmBeforeOrder": true
  }
}
```

---

## 4. strategies.json

### 4.1 ファイル位置

```
%LOCALAPPDATA%/N225BrokerBridge/strategies.json
```

### 4.2 書込 / 読込クラス

- 書込/読込: `JsonStrategyRegistry` (Singleton)
- `IStrategyRegistry.UpsertAsync` / `RemoveAsync` / `MarkSignalReceivedAsync` / `UpdateLastSignalAsync` で更新

### 4.3 スキーマ

```json
[
  {
    "alertName": "string (主キー1)",
    "interval": "integer (主キー2)",
    "isEnabled": "bool",
    "description": "string (任意)",
    "lastSignalAt": "ISO 8601 datetime (UTC)",
    "lastTradeType": "string ('NewOrder' / 'ExitOrder')",
    "lastSide": "string ('Buy' / 'Sell')",
    "lastPrice": "decimal"
  },
  ...
]
```

主キー: `(alertName, interval)` 複合キー

### 4.4 各フィールド

#### alertName / interval

戦略の主キー。Webhook の `alert_name` + `interval` と一致するものが「登録済」と判定される。

#### isEnabled

`false` の場合、シグナル受信しても発注しない (受信ログ + LastSignalAt 更新は実施)。

#### description

UI 表示用の説明文 (任意)。

#### lastSignalAt / lastTradeType / lastSide / lastPrice

直近受信したシグナルの情報。UI で表示。シグナル受信時に毎回更新。

### 4.5 ファイル例

```json
[
  {
    "alertName": "V7_7_Long_5min",
    "interval": 5,
    "isEnabled": true,
    "description": "MESA Stochastic V7-7 ロング戦略",
    "lastSignalAt": "2026-05-26T01:30:15.123Z",
    "lastTradeType": "NewOrder",
    "lastSide": "Buy",
    "lastPrice": 0
  },
  {
    "alertName": "V7_7_Long_5min",
    "interval": 5,
    "isEnabled": false,
    "description": "(無効化中)",
    "lastSignalAt": null,
    "lastTradeType": null,
    "lastSide": null,
    "lastPrice": null
  }
]
```

### 4.6 注意

- 同じ AlertName でも Interval が違えば別エントリ (例: 5 分 / 15 分 で別戦略管理)
- 編集時に主キー変更 → 旧削除 + 新追加で実装
- JSON は WriteIndented で人間にも読める形式

---

## 5. orders-metadata.json

### 5.1 ファイル位置

```
%LOCALAPPDATA%/N225BrokerBridge/orders-metadata.json
```

### 5.2 書込 / 読込クラス

- 書込/読込: `JsonOrderMetadataStore` (Singleton)
- `PlaceNewOrderUseCase` / `ClosePositionUseCase` / `ManualClosePositionUseCase` で UpsertAsync
- `ExecutionApplier` 経由で RemoveAsync (注文 terminal 時)
- `KabuOrderPollingService` の UI 突合では同期 `TryGet` を多用 (ホットパス)

### 5.3 スキーマ

```json
[
  {
    "brokerOrderId": "string (kabu OrderID、主キー)",
    "strategy": "string",
    "interval": "integer",
    "tradeMode": "string ('Auto' / 'Manual')"
  },
  ...
]
```

### 5.4 用途

注文一覧 UI で「この注文を発したのは何戦略か / 何分足か / Auto か Manual か」を表示するためのメタ情報。kabu API には Strategy/Interval/TradeMode の概念がないため、ブリッジが独自に紐付ける。

### 5.5 ライフサイクル

| イベント | 動作 |
|---|---|
| 注文 Accepted | UpsertAsync |
| 注文 約定完了 (Filled) | (保持) |
| 注文 Cancelled / Rejected / Expired | (保持) |
| アプリ起動時の Step 2 で kabu /orders 取得 | UI 表示で TryGet |
| アプリ終了時 | 何もしない (永続化されたまま) |

⚠️ 現状、終端状態の注文メタは削除されない。ロードがどんどん溜まるが、TryGet は O(1) なので問題なし。将来課題: 30 日経過したエントリの自動削除。

### 5.6 ファイル例

```json
[
  {
    "brokerOrderId": "ORD-20260526-001",
    "strategy": "V7_7_Long_5min",
    "interval": 5,
    "tradeMode": "Auto"
  },
  {
    "brokerOrderId": "ORD-20260526-002",
    "strategy": "Manual",
    "interval": 0,
    "tradeMode": "Manual"
  }
]
```

---

## 6. auto-positions.json

### 6.1 ファイル位置

```
%LOCALAPPDATA%/N225BrokerBridge/auto-positions.json
```

### 6.2 書込 / 読込クラス

- 書込/読込: `JsonAutoPositionMetadataStore` (Singleton)
- `ExecutionApplier.ApplyAsync` で UpsertAsync (Auto モードの新規約定時)
- `ExecutionApplier.ApplyAsync` で RemoveAsync (Position close 時)
- `BrokerSessionInitializerService` の Step 3 で SyncToActiveSetAsync (起動時整合)

### 6.3 スキーマ

```json
[
  {
    "executionId": "string (主キー = kabu ExecutionID)",
    "strategy": "string",
    "interval": "integer"
  },
  ...
]
```

### 6.4 用途

kabu API の /positions では「この建玉が自動取引由来か手動か」「どの戦略から生まれたか」が分からない。本ファイルは Auto 建玉だけにそのメタを紐付ける。

起動時の Position 復元 (`BrokerSessionInitializerService` Step 3):

```
kabu /positions の各エントリに対して
  meta = auto-positions.json から ExecutionID で検索
  if (meta found):
      Position(TradeMode=Auto, Strategy=meta.Strategy, Interval=meta.Interval)
  else:
      Position(TradeMode=Manual, Strategy="Manual", Interval=0)
```

### 6.5 SyncToActiveSetAsync

起動時に「kabu に存在しない ExecutionID」のメタを削除する。これにより**永続化ファイルが kabu の実状から乖離しないようにする**。

### 6.6 ファイル例

```json
[
  {
    "executionId": "EXEC-20260526-A001",
    "strategy": "V7_7_Long_5min",
    "interval": 5
  },
  {
    "executionId": "EXEC-20260526-A002",
    "strategy": "S28_v5_alpha",
    "interval": 15
  }
]
```

---

## 7. ui-layout.json

### 7.1 ファイル位置

```
%LOCALAPPDATA%/N225BrokerBridge/ui-layout.json
```

### 7.2 書込 / 読込クラス

- 書込/読込: `UILayoutStore`
- 書込タイミング: `MainWindow` の DispatcherTimer (5 秒間隔、IsDirty 時のみ)
- 強制書込: OnClosing で 1 回
- 読込タイミング: `MainWindow.OnContentRendered`

### 7.3 スキーマ

```json
{
  "windowWidth": "double",
  "windowHeight": "double",
  "windowLeft": "double",
  "windowTop": "double",
  "isMaximized": "bool",
  "leftPanelWidth": "double",
  "logRowHeight": "double",
  "strategyGridColumnWidths": [ "double", "double", ... ],
  "positionGridColumnWidths": [ "double", "double", ... ],
  "orderGridColumnWidths": [ "double", "double", ... ]
}
```

### 7.4 リセット

- メニュー「表示 → 列幅を初期値に戻す」で各 `*ColumnWidths` を削除
- 次回起動時に XAML 初期値が採用される

### 7.5 ファイル例

```json
{
  "windowWidth": 1600,
  "windowHeight": 900,
  "windowLeft": 100,
  "windowTop": 50,
  "isMaximized": false,
  "leftPanelWidth": 280,
  "logRowHeight": 200,
  "strategyGridColumnWidths": [60, 220, 50, 130, 60, 60, 80, 200],
  "positionGridColumnWidths": [120, 100, 80, 120, 130, 50, 60, 70, 70, 80, 90, 180],
  "orderGridColumnWidths": [120, 80, 130, 130, 60, 70, 60, 90, 70, 70, 80, 180, 180]
}
```

---

## 8. logs/n225brokerbridge-YYYY-MM-DD.log

### 8.1 ファイル位置

```
%LOCALAPPDATA%/N225BrokerBridge/logs/n225brokerbridge-YYYY-MM-DD.log
```

### 8.2 書込クラス

- `SerilogConfiguration` → File Sink
- 日次ローテーション、最大 7 日保持

### 8.3 フォーマット

```
[2026-05-26T07:00:00.123+09:00 INF SignalHandler] HandleAsync alert=V7_7_Long_5min interval=5 {Properties}
```

- ISO 8601 タイムスタンプ (タイムゾーン付き)
- レベル: VRB / DBG / INF / WRN / ERR / FTL (Serilog 3 文字略号)
- SourceContext (クラス名)
- メッセージ + 構造化 properties

### 8.4 構造化フィールド (代表)

| キー | 内容 |
|---|---|
| BrokerOrderId | kabu OrderID |
| ExecutionId | 約定 ID |
| Strategy | 戦略名 |
| Interval | 足 |
| AlertName | TV アラート名 |
| Symbol | 銘柄コード |
| Quantity | 数量 |
| Price | 価格 |

### 8.5 マスキング

`KabuApiClient.SendOrderAsync` の Request ログでは `"Password": "***"` に置換される。

---

## 9. ファイル間の関係図

```
appsettings.Local.json
    └─ Webhook.Passphrase ─→ SignalAuthenticator
    └─ Kabu.ApiPassword ───→ KabuTokenService
    └─ Kabu.OrderPassword ─→ KabuApiClient.SendOrderAsync

strategies.json
    └─ (alertName, interval) ──→ SignalHandler の戦略チェック
                              ←─ UI 戦略管理ダイアログ
                              ←─ シグナル受信時の MarkSignalReceived

orders-metadata.json
    └─ brokerOrderId ──→ UI 注文一覧の Strategy/Interval/TradeMode 表示
                       ←─ 注文 Accepted 時に Upsert

auto-positions.json
    └─ executionId ──→ 起動時 Step 3 で Position(TradeMode=Auto) 復元
                     ←─ Auto 新規約定時に Upsert
                     ←─ Position close 時に Remove
                     ←─ 起動時 Step 3 で SyncToActiveSet (dead 削除)

ui-layout.json
    └─ MainWindow ロード時に適用
                  ←─ 5 秒間隔 DispatcherTimer で書込

logs/*.log
    └─ Serilog → File Sink (常時書込、7 日保持)
```

---

## 10. バックアップ・移行

### 10.1 バックアップ対象

| ファイル | バックアップ要 | 理由 |
|---|---|---|
| `appsettings.Local.json` | ⚠️ 注意 | DPAPI 暗号化のため別 PC では復号不可。同一 PC のみ意味あり |
| `strategies.json` | ✅ | 戦略構成は手動で再構築すると時間がかかる |
| `orders-metadata.json` | ❌ | 終端注文のメタなので失っても致命的でない |
| `auto-positions.json` | ✅ | 失うと既存建玉が全部 Manual 扱いになる |
| `ui-layout.json` | ❌ | リセットしても困らない |
| `logs/*.log` | ⚠️ | 監査用に長期保存したい場合のみ |

### 10.2 別 PC への移行

DPAPI 暗号化のため `appsettings.Local.json` は移行不可。新 PC で**再入力が必須**。

それ以外のファイルは単純コピーで OK。

### 10.3 バックアップ推奨タイミング

- 戦略を CRUD した後
- 大きなトレード日の終わり
- N225BrokerBridge のバージョンアップ前

PowerShell でフォルダごとコピー:

```powershell
$src = "$env:LOCALAPPDATA\N225BrokerBridge"
$dst = "D:\Backups\N225BrokerBridge\$(Get-Date -Format 'yyyyMMdd_HHmmss')"
Copy-Item -Path $src -Destination $dst -Recurse
```

---

## 11. 並行アクセス制御

### 11.1 ファイル書込のロック

各 `Json*Store` 内に `SemaphoreSlim _saveLock` を持ち、書込中の競合を防ぐ:

```csharp
await _saveLock.WaitAsync();
try
{
    await File.WriteAllTextAsync(path, json, ct);
}
finally
{
    _saveLock.Release();
}
```

### 11.2 同一プロセス内マルチスレッドからの呼出

問題なし (上記ロックで保護)。

### 11.3 複数プロセス並行起動

⚠️ 想定外。ファイル破損の可能性あり。

対策: アプリ起動時に Mutex で多重起動防止 (実装が無ければ将来課題)。

---

## 12. JSON フォーマット規約

| 項目 | 値 |
|---|---|
| `JsonSerializerOptions.WriteIndented` | true (人間にも読める) |
| `PropertyNamingPolicy` | camelCase |
| Date 形式 | ISO 8601 with TZ (`yyyy-MM-ddTHH:mm:ss.fffZ`) |
| null 表現 | JSON null |
| エンコーディング | UTF-8 (BOM なし) |

---

## 13. データサイズ目安

| ファイル | サイズ目安 |
|---|---|
| `appsettings.Local.json` | < 2 KB |
| `strategies.json` | 戦略 10 件で ~3 KB |
| `orders-metadata.json` | 1 注文 ~120 B、1 年 1000 注文で ~120 KB |
| `auto-positions.json` | 1 建玉 ~80 B、最大 10 同時保有想定 ~1 KB |
| `ui-layout.json` | ~500 B |
| `logs/*.log` (1 日) | 通常運用で ~5–20 MB |

---

## 14. 将来課題 (SQLite 移行)

JSON ファイル群を SQLite に集約する案。

| 利点 | リスク |
|---|---|
| トランザクション保証 | スキーマ変更コスト |
| 履歴データの効率的な保存 | DPAPI 暗号化の継続が必要 (列ごと暗号化) |
| インデックスによる検索高速化 | 既存 JSON との同期コスト |

着手条件: 同時更新の競合が発生したら / 1 万件以上の注文履歴が必要になったら。

---

## 15. 変更履歴

| バージョン | 日付 | 変更内容 |
|---|---|---|
| 1.0.0 | 2026-05-26 | 初版作成 |
