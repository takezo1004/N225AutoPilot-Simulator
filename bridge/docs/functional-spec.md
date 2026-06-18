# 機能仕様書 (Functional Specification)

**バージョン**: 1.0.0
**作成日**: 2026-05-26
**最終更新**: 2026-05-26
**対象**: N225BrokerBridge v0.x

---

## 1. このドキュメントの目的

本書は [requirements.md](./requirements.md) で定義された機能要件を、**画面 / 入出力 / 操作フロー / 例外** の粒度に落として具体化する。
コード変更時 / バグ修正時 / 機能追加時に「期待動作とは何か」を判定する参照源。

関連ドキュメント:
- [要件定義書](./requirements.md) — 何を実現したいか
- [シーケンス図集](./sequence-diagrams.md) — フローの可視化
- [Webhook API 仕様](./webhook-api-spec.md) — 受信ペイロード仕様
- [MainWindow レイアウト](./mainwindow-layout.md) — UI 物理構造

---

## 2. システム構成図 (機能視点)

```
┌──────────────────────────────────────────────────────────────┐
│                    外部入出力                                  │
└──────────────────────────────────────────────────────────────┘
   ┌─────────────────┐                  ┌──────────────────┐
   │ TradingView      │   HTTPS Webhook  │  kabu Station    │
   │ Pine Strategy    │ ───┐         ┌──▶│  (localhost:18080)│
   └─────────────────┘    │         │   └──────────────────┘
                          ▼         │             ▲
                  ┌────────────────────────────────┴─┐
                  │  Cloudflare Tunnel ── localhost:8001
                  └──────────────────────────────────┘
                                ▼
   ┌──────────────────────────────────────────────────────────┐
   │              N225BrokerBridge (WPF / .NET 8)               │
   │                                                            │
   │  ┌──── UI 層 ────────────────────────────────────────┐ │
   │  │ MainWindow | SettingsWindow | StrategyManagerWindow│ │
   │  └─────────────────────────────────────────────────────┘ │
   │  ┌──── Application 層 (Use Cases / Services) ──────────┐ │
   │  │ SignalHandler / PlaceNewOrder / ClosePosition       │ │
   │  │ Doten / ManualClose / ExecutionApplier              │ │
   │  └─────────────────────────────────────────────────────┘ │
   │  ┌──── Domain 層 (Aggregates / VO / Events) ──────────┐ │
   │  │ Order / Position / PositionMatcher / Domain Events  │ │
   │  └─────────────────────────────────────────────────────┘ │
   │  ┌──── Infrastructure 層 ─────────────────────────────┐ │
   │  │ KabuAdapter / KabuApiClient / WebhookListener       │ │
   │  │ JsonRepositories / WebSocketClient / PollingService │ │
   │  └─────────────────────────────────────────────────────┘ │
   └──────────────────────────────────────────────────────────┘
                                ▲
                  ┌─────────────┴──────────────┐
                  │ %LOCALAPPDATA%/N225BrokerBridge/ │
                  │ - appsettings.Local.json (DPAPI) │
                  │ - strategies.json                │
                  │ - orders-metadata.json           │
                  │ - auto-positions.json            │
                  │ - ui-layout.json                 │
                  │ - logs/n225brokerbridge-*.log    │
                  └────────────────────────────────┘
```

---

## 3. 画面一覧

| 画面名 | ファイル | 起動方法 | 主機能 |
|---|---|---|---|
| **メインウィンドウ** | `Views/MainWindow.xaml` | アプリ起動時 | 手動発注・建玉/注文/ログ表示・自動売買トグル |
| **設定ダイアログ** | `Views/SettingsWindow.xaml` | メニュー「ファイル → 設定」 | Webhook / kabu 接続設定・確認ダイアログ要否 |
| **戦略管理ダイアログ** | `Views/StrategyManagerWindow.xaml` | メニュー「戦略 → 戦略管理」 | 戦略 CRUD |

物理レイアウトは [`mainwindow-layout.md`](./mainwindow-layout.md) を参照。

---

## 4. メインウィンドウ機能詳細

### 4.1 ヘッダー (Row 0: タイトルバー)

| 要素 | 機能 |
|---|---|
| メニュー「ファイル」 | 設定 / 終了 |
| メニュー「戦略」 | 戦略管理を開く |
| メニュー「表示」 | 列幅を初期値に戻す |
| メニュー「ヘルプ」 | バージョン情報 |
| ウィンドウタイトル | "N225 Broker Bridge" |
| 右上ボタン | 最小化 / 最大化 / 終了 |

### 4.2 ステータスバー (Row 1)

| 要素 | 表示内容 | 更新トリガー |
|---|---|---|
| `BrokerStatus` | ブローカー: 接続中 / 切断 / 未接続 | 5 秒間隔ポーリング + 例外時 |
| `WebhookStatus` | Webhook: 受信中 / 停止中 | `HttpWebhookListener.Started/Stopped` |
| `StateMessage` | 直近の操作結果 | 各 Command 実行後に上書き |
| `IsAutoTradeEnabled` トグル | 自動売買 ON/OFF | ユーザー操作 |
| 更新ボタン | `RefreshStatusCommand` | クリック |

#### 自動売買トグル仕様

- 初期値: `false` (起動直後は OFF)
- トグル ON → `IAutoTradeGate.IsEnabled = true`
- トグル OFF → `IAutoTradeGate.IsEnabled = false`
- OFF 状態で Webhook シグナル受信 → 即 `AutoTradeDisabled` 結果を返却し発注経路に入らない
- 手動発注は本トグルの影響を受けない

### 4.3 手動発注パネル (Row 2 左)

| フィールド | 入力種別 | 制約 |
|---|---|---|
| 銘柄 | ComboBox (`AvailableInstruments`) | 起動時に Mini / Micro 登録。`ResolvedSymbolCode = null` の項目は発注不可 |
| 注文タイプ | RadioButton | 成行 / 指値 / 逆指値 / 最良気配 |
| 時間条件 | ComboBox | 注文タイプにより選択肢が変動 (`AvailableTimeInForces`) |
| 注文数量 | NumberBox | 1 以上の整数 |
| 指値価格 | NumberBox | 注文タイプが指値の時のみ活性化 |
| 逆指値価格 | NumberBox | 注文タイプが逆指値の時のみ活性化 |
| 買 / 売 ボタン | Button | `PlaceBuyOrderCommand` / `PlaceSellOrderCommand` |
| 返済 / キャンセル ボタン | Button | `ExitPositionCommand` / `CancelOrderCommand` (グリッド選択行を対象) |

#### 注文タイプと時間条件の連動

| 注文タイプ | 指値価格 | 逆指値価格 | 選択可能な時間条件 |
|---|---|---|---|
| 成行 | 無効 (灰色) | 無効 | FAS / FAK |
| 指値 | 必須 | 無効 | FAS / FAK / FOK |
| 逆指値 | 必須 (指値部分) | 必須 (トリガー) | FAS / FAK / FOK |
| 最良気配 | 無効 | 無効 | FAK |

#### 手動買発注フロー

1. ユーザー: 銘柄選択 / 数量入力 / 「買 注 文」クリック
2. (RequireConfirmBeforeOrder=true なら) 確認ダイアログ表示
3. `PlaceNewOrderUseCase.ExecuteAsync(NewOrderIntent { Side=Buy, ... })`
4. 結果が `Accepted` → 注文一覧に行追加 / `StateMessage` に "発注成功"
5. 結果が `Rejected` → `StateMessage` に "発注拒否: ..."
6. 結果が `NetworkError` → `StateMessage` に "通信エラー (要再照会)"

### 4.4 現在値パネル (Row 2 中央)

| 要素 | 表示 | データ源 |
|---|---|---|
| 銘柄表示名 | 例: "日経225Mini 26/06" | `ManualOrderInstrument.DisplayName + ContractMonth` |
| 現在値 | `LastPrice` (整数表示) | `IPriceUpdateNotifier.PriceUpdated` |
| BID / 数量 | `BidPrice` / `BidQty` | 同上 |
| ASK / 数量 | `AskPrice` / `AskQty` | 同上 |

> ⚠️ kabu API の BID/ASK 命名は通常と逆 (`BidPrice = 売り板 = ASK`)。本 UI では「通常のトレーダー視点」で表示するため、kabu 値をそのまま受け取って表示している (詳細は [`adapters/kabu.md`](./adapters/kabu.md))。

### 4.5 戦略一覧 (Row 2 右上)

| カラム | 内容 |
|---|---|
| 有効 | Checkbox (`IsEnabled`) — 変更で `IStrategyRegistry.UpsertAsync` を hook |
| アラート名 | 戦略名 |
| 足 | Interval (分) |
| 最終シグナル時刻 | `LastSignalAt` |
| 直近 売買 | `LastTradeType` (New/Exit) + `LastSide` (Buy/Sell) |
| 直近 価格 | `LastPrice` (0 なら "成行") |
| 説明 | `Description` |

### 4.6 建玉一覧 (Row 2 右中)

| カラム | 内容 |
|---|---|
| 銘柄 | 表示名 |
| 銘柄コード | 数値コード (kabu) |
| 取引区分 | Auto / Manual |
| 約定日 | `OpenedAt` |
| 戦略 | `Strategy` |
| 足 | Interval |
| サイド | 買建 / 売建 |
| 残数量 | `LeaveQuantity` |
| 拘束 | `HoldQuantity` |
| 建値 | `EntryPrice` |
| 含み損益 | `(現在値 - 建値) × LeaveQty × Multiplier × Side符号` |
| ExecutionID | 内部識別 |

#### 行選択 → 「返済」 → フロー

1. グリッド選択 → `SelectedPosition` 更新
2. 「返済」ボタンクリック → `ExitPositionCommand`
3. (確認ダイアログ表示)
4. `ManualClosePositionUseCase.ExecuteAsync(targetExecutionId, qty=null, orderType=BestMarket)`
5. 結果反映 (注文一覧に行追加 + ステータス更新)

### 4.7 注文一覧 (Row 2 右下)

| カラム | 内容 |
|---|---|
| 銘柄 | 表示名 |
| 取引区分 | Auto / Manual |
| 受信時刻 | `RecvTime` |
| 戦略 | `Strategy` |
| 足 | Interval |
| 新規/返済 | `CashMargin` |
| サイド | Buy / Sell |
| 状態 | Created / Submitted / PartiallyFilled / Filled / Cancelled / Expired / Rejected |
| 数量 | `OrderQty` |
| 約定 | `CumQty` |
| 価格 | 約定価格 (約定済) または指値 (未約定)。成行は "成行" |
| OrderID | ブローカー OrderId |

#### 行選択 → 「キャンセル」 → フロー

1. グリッド選択 → `SelectedOrder` 更新
2. 「キャンセル」ボタン → `CancelOrderCommand`
3. (確認ダイアログ)
4. `IBrokerAdapter.CancelOrderAsync(brokerOrderId)`
5. 結果反映 (ポーリングが終端状態を検出 → `Cancelled` 表示)

### 4.8 ログパネル (Row 4)

- `UiLogSink` が Serilog の LogEvent を ObservableCollection に流す (最新 1000 件)
- レベル (Info/Warn/Error/Debug) ごとに色分け
- 自動スクロール (最新行を表示)
- 「クリア」「コピー」ボタン (実装が無ければ将来課題)

### 4.9 GridSplitter (Row 3)

- ログ領域とメイン領域の高さを動的調整
- 位置は `ui-layout.json` に永続化

---

## 5. 設定ダイアログ機能詳細

### 5.1 Webhook セクション

| フィールド | 種別 | 暗号化 |
|---|---|---|
| Webhook パスフレーズ | PasswordBox | DPAPI |
| Webhook ポート | NumberBox | 平文 (8001 デフォルト) |

### 5.2 kabu 接続セクション

| フィールド | 種別 | 暗号化 |
|---|---|---|
| 環境 | RadioButton (Production / Verification) | 平文 |
| API パスワード (本番) | PasswordBox | DPAPI |
| API パスワード (検証) | PasswordBox | DPAPI |
| 取引暗証番号 | PasswordBox | DPAPI |
| 平文表示トグル | CheckBox | 一時的のみ (ダイアログ閉じるとリセット) |
| 接続先 URL プレビュー | TextBlock (read-only) | - |

### 5.3 動作セクション

| フィールド | 種別 | 影響範囲 |
|---|---|---|
| 手動操作前に確認 | CheckBox | 買/売/返済/キャンセル の 4 操作のみ |

### 5.4 保存フロー

1. 「保存」クリック → `SaveCommand`
2. `LocalSettingsStore.Save(LocalSettingsValues)`
3. 機密項目を DPAPI Encrypt
4. JSON 化 → `%LOCALAPPDATA%\N225BrokerBridge\appsettings.Local.json` へ書き込み
5. `StatusMessage = "保存しました (HH:mm:ss)"`
6. ユーザーが「閉じる」 → `DialogResult = true` 返却

---

## 6. 戦略管理ダイアログ機能詳細

### 6.1 グリッドカラム

| カラム | 内容 |
|---|---|
| アラート名 | StrategyEntry.AlertName |
| 足 | Interval |
| 有効 | IsEnabled |
| 説明 | Description |
| 最終シグナル時刻 | LastSignalAt |
| 直近 売買 | LastTradeType / LastSide / LastPrice |

### 6.2 編集フォーム (グリッド下部)

統合編集フォーム (`IsEditing` フラグで挙動切替):

| ボタン | `IsEditing=false` 時 | `IsEditing=true` 時 |
|---|---|---|
| 追加 | 入力フィールドの値を新規エントリで保存 | (無効) |
| 編集 | SelectedEntry の値をフォームにコピーして `IsEditing=true` | (無効) |
| 更新 | (無効) | 編集中フィールドで上書き保存。主キー変更時は旧削除 + 新追加 |
| キャンセル | (無効) | `IsEditing=false` でフォームクリア |
| 削除 | SelectedEntry を削除 | (無効) |

### 6.3 永続化

- 全 CUD 操作は `IStrategyRegistry.UpsertAsync` / `RemoveAsync` → `strategies.json` 自動更新
- `Changed` イベントが他コンポーネント (MainViewModel) に伝播
- ダイアログ閉じた時 MainViewModel が `ReloadStrategiesFromRegistry()` で同期

---

## 7. 自動売買経路 (Webhook 受信フロー)

### 7.1 シグナル受信から判定まで

```
[1] HttpWebhookListener
      ↓ HTTP POST /webhook (JSON body)
[2] SignalPayloadParser.Parse(json)
      ↓ SignalPayload
[3] SignalHandler.HandleAsync(payload, TradeMode.Auto, ct)
      ├─ [3-1] IAutoTradeGate.IsEnabled = false → AutoTradeDisabled (終了)
      ├─ [3-2] ISignalAuthenticator.Authenticate(passphrase) = false → AuthFailed (終了)
      ├─ [3-3] IStrategyRegistry.IsEnabled(alertName, interval) = false → Ignored (終了)
      ├─ [3-4] IAutoTradeInstrumentProvider.ResolvedSymbolCode = null → Ignored (終了)
      └─ [3-5] SignalInterpreter.Interpret(payload, tradeMode, symbol)
              ↓ SignalIntent
```

### 7.2 SignalIntent → UseCase マッピング

| Intent | UseCase | 説明 |
|---|---|---|
| `NewOrderIntent` | `PlaceNewOrderUseCase` | 新規建玉 |
| `ExitOrderIntent` | `ClosePositionUseCase` | 返済 (部分含む) |
| `DotenIntent` | `DotenUseCase` | 返済 + 新規 (反対方向) |
| `IgnoreIntent` | (実行なし) | 未定義遷移 / NoOp |

### 7.3 SignalInterpreter 解釈テーブル

| prev_position | market_position | order_action | 結果 Intent |
|---|---|---|---|
| flat | long | buy | NewOrderIntent (Buy) |
| flat | short | sell | NewOrderIntent (Sell) |
| long | flat | sell | ExitOrderIntent (OriginalSide=Buy, 全量) |
| short | flat | buy | ExitOrderIntent (OriginalSide=Sell, 全量) |
| long | long | sell | ExitOrderIntent (部分返済) |
| short | short | buy | ExitOrderIntent (部分返済) |
| short | long | buy | DotenIntent (Short → Long) |
| long | short | sell | DotenIntent (Long → Short) |
| その他 | - | - | IgnoreIntent (Reason 付与) |

---

## 8. 手動操作経路

### 8.1 手動新規 (買/売)

```
[UI] 銘柄 / 数量 / 注文タイプ / 時間条件 入力
   ↓ 「買 注 文」/「売 注 文」 クリック
[ViewModel] PlaceBuyOrderCommand / PlaceSellOrderCommand
   ↓ (確認ダイアログ)
[Application] PlaceNewOrderUseCase.ExecuteAsync(NewOrderIntent {
     Strategy = "Manual",
     Interval = 0,
     TradeMode = Manual,
     Symbol = ManualOrderInstrument.ResolvedSymbolCode,
     Side, OrderType, TimeInForce, Quantity, LimitPrice, StopPrice
   })
```

### 8.2 手動返済

```
[UI] 建玉行を選択 → 「返 済」 クリック
   ↓ (確認ダイアログ)
[ViewModel] ExitPositionCommand
   ↓
[Application] ManualClosePositionUseCase.ExecuteAsync(
     SelectedPosition.ExecutionId,
     quantity: null,         // = AvailableForClose 全量
     orderType: BestMarket,
     timeInForce: FAS
   )
```

### 8.3 手動キャンセル

```
[UI] 注文行を選択 → 「キャンセル」 クリック
   ↓ (確認ダイアログ)
[ViewModel] CancelOrderCommand
   ↓
[Application/Adapter] IBrokerAdapter.CancelOrderAsync(SelectedOrder.OrderId)
   ↓
[kabu PUT /cancelorder]
   ↓ 終端状態応答
[Polling] KabuOrderPollingService が終端を検出 → OrderTerminationSubscriberService
   ↓
[ExecutionApplier] ApplyTerminationAsync(brokerCode, orderId, reason)
   - Order.MarkTerminated(Cancelled)
   - 返済注文だった場合: Position.ReleaseReservation(unfilled)
```

---

## 9. 起動シーケンス (App ライフサイクル)

### 9.1 OnStartup

```
1. DispatcherUnhandledException 等のグローバル例外ハンドラ設定
2. 起動引数 --demo を判定し App.IsDemoMode を設定
3. BootstrapAsync() 実行
   ├─ Serilog 設定 (Console / File / UI Sink)
   ├─ LocalSettingsStore.Load() で DPAPI 復号
   ├─ DI コンテナ構築 (Host.CreateDefaultBuilder + AddBrokerBridge*)
   ├─ (IsDemoMode == false の場合)
   │   └─ _host.StartAsync()
   │       ├─ BrokerSessionInitializerService.StartAsync (Step 1-3)
   │       ├─ ExecutionStreamSubscriberService.StartAsync
   │       ├─ OrderTerminationSubscriberService.StartAsync
   │       ├─ KabuOrderPollingService.StartAsync (1秒ポーリング開始)
   │       ├─ KabuBoardWebSocketService.StartAsync (WebSocket 接続)
   │       └─ HttpWebhookListener.StartAsync (port 8001 LISTEN 開始)
   └─ MainWindow.Show()
4. MainViewModel コンストラクタ:
   ├─ 通知購読 (Position/Order/Price/Strategy)
   ├─ LoadInstruments (Mini/Micro)
   ├─ LoadStrategies (registry → ObservableCollection)
   ├─ RefreshStatus
   └─ (IsDemoMode == true) SeedDemoData / InsertDemoLogs
       (IsDemoMode == false) LoadInitialStateAsync / TryResolveInstrumentsAsync / Register
```

### 9.2 OnExit

```
1. _host.StopAsync(timeout: 5s)
   ├─ HttpWebhookListener.StopAsync (LISTEN 停止)
   ├─ KabuBoardWebSocketService.StopAsync (WebSocket 切断)
   ├─ KabuOrderPollingService.StopAsync (ポーリング停止)
   └─ その他 HostedService の StopAsync
2. UILayoutStore.Save() (現在のレイアウト)
3. Serilog FlushAndCloseAsync
4. Application.Shutdown
```

---

## 10. デモモード (`--demo`)

### 10.1 起動条件

```
N225BrokerBridge.UI.exe --demo
```

または `--Demo` (大文字小文字無視)。

### 10.2 動作差分

| 項目 | 通常 | `--demo` |
|---|---|---|
| kabu API 接続 | する | **しない** |
| Webhook listener | 起動 | **起動しない** |
| 注文ポーリング | 起動 | **起動しない** |
| Position reconciliation | 起動 | **起動しない** |
| WebSocket 板情報 | 接続 | **接続しない** |
| strategies.json 読み書き | する | **しない** |
| auto-positions.json 読み書き | する | **しない** |
| MainWindow 表示 | 通常 | **決め打ちデータ + ログ 5 件で表示** |

### 10.3 用途

- ブログ / マニュアル用スクリーンショット
- UI レイアウト調整 (本番口座と切り離して)
- 新人レビュー時の画面共有

詳細は [`demo-mode.md`](./demo-mode.md) を参照。

---

## 11. ログ仕様

### 11.1 出力先

| Sink | 出力先 | レベル |
|---|---|---|
| Console | 標準出力 | Information+ (Microsoft/* は Warning+) |
| File | `%LOCALAPPDATA%/N225BrokerBridge/logs/n225brokerbridge-YYYY-MM-DD.log` | 同上 / 7 日保持 / 日次ローテーション |
| UI | `UiLogSink` → `ObservableCollection<UiLogEntry>` (最大 1000 件) | 同上 |

### 11.2 構造化フィールド

代表的なフィールド:

- `SourceContext` — クラス名
- `BrokerOrderId` — kabu OrderId
- `ExecutionId` — 約定 ID
- `Strategy` — 戦略名
- `Interval` — 足
- `AlertName` — シグナル名

### 11.3 マスキング

- `KabuApiClient.SendOrderAsync` の Request Body ログでは `Password` フィールドを `***` に置換
- DPAPI 暗号化された設定値はログに出さない

---

## 12. エラーハンドリングポリシー

| 場面 | 動作 |
|---|---|
| Webhook JSON パース失敗 | 400 Bad Request + 詳細はログのみ |
| 認証失敗 | 200 OK + `AuthFailed` (攻撃者にパスフレーズ正否を漏らさない) |
| kabu API 接続失敗 | OrderResult.NetworkError → Order.MarkTerminated(Rejected, "NetworkError...") |
| kabu API 401 応答 | KabuTokenService.RefreshAsync → 1 度自動リトライ |
| 起動時 kabu 未接続 | 各 Step は警告ログのみで継続。UI は立ち上がる |
| WebSocket 切断 | 5 秒待機 → 自動再接続 |
| 設定保存失敗 | StatusMessage にエラー表示 / 元の値は保持 |
| 不正な数量入力 | NumberBox の Validation で発注ボタン無効化 |
| 確認ダイアログ「キャンセル」 | コマンド実行を中止 |

---

## 13. 確認ダイアログ仕様

`RequireConfirmBeforeOrder = true` 時、以下の操作で表示:

| 操作 | ダイアログタイトル | メッセージ |
|---|---|---|
| 買 注文 | 確認 | "{銘柄} を {数量} 枚で {注文タイプ} 買付発注します。よろしいですか?" |
| 売 注文 | 確認 | 同上 (売付) |
| 返済 | 確認 | "{銘柄} {サイド} 建玉 {数量} 枚を {注文タイプ} で返済します。よろしいですか?" |
| キャンセル | 確認 | "{銘柄} {サイド} 注文 #{OrderId} をキャンセルします。よろしいですか?" |

Webhook 自動発注には表示しない (自動経路で確認は意味がない)。

---

## 14. UI レイアウト永続化

### 14.1 保存対象

- メインウィンドウのサイズ・位置
- メインウィンドウの最大化状態
- 手動発注パネル幅 (左 GridColumn)
- ログパネル高さ (Row 4)
- 各 DataGrid のカラム幅

### 14.2 保存タイミング

- 5 秒間隔の DispatcherTimer で `IsDirty == true` なら保存
- ウィンドウクローズ時に強制保存

### 14.3 リセット

- メニュー「表示 → 列幅を初期値に戻す」で各 DataGrid のカラム幅を XAML 初期値に戻す
- `ui-layout.json` から該当キーを削除

---

## 15. テーマ / スタイリング

- Wpf.Ui 4.x Dark テーマ
- ウィンドウ背景: Mica (Windows 11)
- フォント: タイトル = Cascadia Code、数値 = Consolas
- 数値カラムは右寄せ、文字列カラムは左寄せ
- DataGrid ヘッダ: 濃紺背景 + 白字 + 中央寄せ

詳細スタイル定義は `App.xaml` / `MainWindow.xaml` を参照。

---

## 16. 機能要件トレーサビリティ

[requirements.md](./requirements.md) §5 の F-NN と、本書のセクション対応:

| 要件 ID | 仕様セクション |
|---|---|
| F-1 (Webhook 受信) | §7.1, §11 |
| F-2 (シグナル解釈) | §7.3 |
| F-3 (自動売買ゲート) | §4.2 |
| F-4 (戦略レジストリ) | §6 |
| F-5 (新規発注) | §4.3, §7.2, §8.1 |
| F-6 (返済自動) | §7.2 |
| F-7 (返済手動) | §8.2 |
| F-8 (ドテン) | §7.2 |
| F-9 (キャンセル) | §4.7, §8.3 |
| F-10 (約定反映) | §4.6, §4.7 |
| F-11 (起動時復元) | §9.1 |
| F-12 (ポーリング) | §9.1 |
| F-13 (価格ティック) | §4.4 |
| F-14 (限月解決) | §9.1 |
| F-15 (UI) | §3-§4 |
| F-16 (デモモード) | §10 |
| F-17 (設定永続化) | §5 |

---

## 変更履歴

| バージョン | 日付 | 変更内容 |
|---|---|---|
| 1.0.0 | 2026-05-26 | 初版作成 (現行コードから逆引きで構築) |
