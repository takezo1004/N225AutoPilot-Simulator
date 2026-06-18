# 主要クラス設計 (Class Design)

**バージョン**: 1.0.0
**作成日**: 2026-05-26
**最終更新**: 2026-05-26
**対象**: Application / Infrastructure / UI の主要クラス

---

## 1. このドキュメントの目的

[domain-model.md](./domain-model.md) で Domain 層を整理したのに対し、本書は **Application / Infrastructure / UI** 各層の**クラス責務・依存関係・拡張ポイント**を網羅する。
新規機能追加時 / 既存コード理解時の地図として使う。

関連:
- [architecture.md](./architecture.md) — DDD 4 層の全体像
- [context-map.md](./context-map.md) — 境界づけられたコンテキスト
- [sequence-diagrams.md](./sequence-diagrams.md) — クラス協調

---

## 2. 層の依存方向

```
UI → Application → Domain ← Infrastructure
                       ↑
                  (DIP: Infrastructure が Domain の抽象を実装)
```

- UI は Application と Domain.ValueObjects を参照可
- Application は Domain のみ参照可
- Infrastructure は Domain と Application の抽象を実装
- Domain は他の層に依存しない

---

## 3. Application 層クラス

### 3.1 Signals: シグナル受信〜振り分け

#### 3.1.1 `SignalHandler` (Application Service)

ファイル: `Signals/SignalHandler.cs`

**責務**: シグナル受信のオーケストレーター。認証 → 戦略チェック → Intent 解釈 → UseCase 振り分け。

```csharp
public sealed class SignalHandler
{
    public SignalHandler(
        ISignalAuthenticator authenticator,
        IAutoTradeGate autoTradeGate,
        IAutoTradeInstrumentProvider instrumentProvider,
        IStrategyRegistry strategyRegistry,
        PlaceNewOrderUseCase placeNewOrderUseCase,
        ClosePositionUseCase closePositionUseCase,
        DotenUseCase dotenUseCase,
        ILogger<SignalHandler> logger
    );

    public Task<SignalHandleOutcome> HandleAsync(
        SignalPayload payload,
        TradeMode tradeMode,
        CancellationToken ct);
}
```

**判定順序** (短絡評価):

1. `_autoTradeGate.IsEnabled == false` → `AutoTradeDisabled`
2. `_authenticator.Authenticate(passphrase) == false` → `AuthFailed`
3. `_strategyRegistry.IsEnabled(alertName, interval) == false` → `Ignored`
4. `_instrumentProvider.ResolvedSymbolCode == null` → `Ignored`
5. `SignalInterpreter.Interpret(payload, tradeMode, symbol)` → 4 種の Intent
6. Intent に応じて UseCase 呼出

**戻り値**: 判別共用体 `SignalHandleOutcome`

| ケース | 説明 |
|---|---|
| `AutoTradeDisabled(alertName)` | グローバルゲート OFF |
| `AuthFailed(alertName)` | 認証失敗 |
| `Interpretation_Failed(alertName, error)` | Interpret 例外 |
| `Ignored(reason)` | 戦略未登録 / 銘柄未解決 / Intent=Ignore |
| `NewOrderDispatched(PlaceNewOrderResult)` | 新規発注実施 |
| `ExitOrderDispatched(ClosePositionResult)` | 返済実施 |
| `DotenDispatched(DotenResult)` | ドテン実施 |

#### 3.1.2 `SignalInterpreter` (純粋関数)

ファイル: `Signals/SignalInterpreter.cs`

**責務**: `SignalPayload` を `SignalIntent` に変換する純粋関数。副作用なし。

```csharp
public static class SignalInterpreter
{
    public static SignalIntent Interpret(
        SignalPayload payload,
        TradeMode tradeMode,
        SymbolCode symbol);
}
```

⚠️ `payload.SymbolTicker` (TradingView 側ティッカー) は**読まない**。`symbol` パラメータを採用する設計。
これにより TV で Mini を監視しながら kabu では Micro を発注、といった運用を許容。

**戻り値**: `SignalIntent` (sealed abstract record)

| 派生 | フィールド |
|---|---|
| `NewOrderIntent` | Side, Quantity, OrderType, TimeInForce, LimitPrice, StopPrice, ...共通 |
| `ExitOrderIntent` | OriginalSide, Quantity, ...共通 |
| `DotenIntent` | OldSide, NewSide, NewQuantity, ...共通 |
| `IgnoreIntent` | Reason |

#### 3.1.3 `ISignalAuthenticator` (Port)

```csharp
public interface ISignalAuthenticator
{
    bool Authenticate(string? receivedPassphrase);
}
```

実装: `PassphraseSignalAuthenticator` (`WebhookListenerOptions.Passphrase` と完全一致比較)。

#### 3.1.4 `IAutoTradeGate` (Port)

```csharp
public interface IAutoTradeGate
{
    bool IsEnabled { get; set; }
}
```

実装: `AutoTradeGate` (volatile bool)。MainViewModel から `IsEnabled = true/false` で操作。

#### 3.1.5 `IAutoTradeInstrumentProvider` (Port)

```csharp
public interface IAutoTradeInstrumentProvider
{
    SymbolCode? ResolvedSymbolCode { get; }
    string? DisplayName { get; }
    string? ContractMonth { get; }
    void SetInstrument(SymbolCode? symbol, string? displayName, string? contractMonth);
}
```

実装: `AutoTradeInstrumentProvider` (Singleton)。MainViewModel が起動時に `SetInstrument` する。

#### 3.1.6 `IStrategyRegistry` (Port)

```csharp
public interface IStrategyRegistry
{
    IReadOnlyList<StrategyEntry> GetAll();
    bool IsEnabled(string alertName, int interval);
    Task UpsertAsync(StrategyEntry entry, CancellationToken ct);
    Task MarkSignalReceivedAsync(string alertName, int interval, DateTime atUtc, CancellationToken ct);
    Task UpdateLastSignalAsync(...);
    Task RemoveAsync(string alertName, int interval, CancellationToken ct);
    event EventHandler Changed;
}
```

実装: `JsonStrategyRegistry` (Infrastructure)。

---

### 3.2 Orders: 新規発注

#### 3.2.1 `PlaceNewOrderUseCase`

ファイル: `Orders/PlaceNewOrderUseCase.cs`

```csharp
public sealed class PlaceNewOrderUseCase
{
    public PlaceNewOrderUseCase(
        IBrokerAdapter broker,
        IOrderRepository orderRepo,
        IOrderMetadataStore metaStore,
        IPendingOrderTracker tracker,
        IDateTimeProvider clock,
        ILogger<PlaceNewOrderUseCase> logger);

    public Task<PlaceNewOrderResult> ExecuteAsync(
        NewOrderIntent intent,
        CancellationToken ct);
}
```

**手順**:

1. `Order` 集約生成 (`State=Created`)
2. `_orderRepo.AddAsync(order)`
3. `_broker.PlaceOrderAsync(OrderRequest{...})`
4. レスポンスに応じて分岐:
   - `Accepted`: `order.MarkSubmitted(brokerOrderId)`, `_metaStore.UpsertAsync`, `_tracker.Track`
   - `Rejected`: `order.MarkTerminated(Rejected, errorMessage)`
   - `NetworkError`: `order.MarkTerminated(Rejected, "NetworkError: ...")`
5. `_orderRepo.UpdateAsync(order)`
6. `PlaceNewOrderResult(order, status, errorMessage)` 返却

#### 3.2.2 `ExecutionApplier` (Application Service)

ファイル: `Orders/ExecutionApplier.cs`

**責務**: 約定イベント / 注文終端イベントを Order/Position に反映するアダプタ。

```csharp
public sealed class ExecutionApplier
{
    public ExecutionApplier(
        IOrderRepository orderRepo,
        IPositionRepository positionRepo,
        IAutoPositionMetadataStore autoPositionMetaStore,
        IPendingOrderTracker tracker,
        IDateTimeProvider clock,
        ILogger<ExecutionApplier> logger);

    public Task ApplyAsync(ExecutionEvent execution, CancellationToken ct);
    public Task ApplyTerminationAsync(
        BrokerCode brokerCode,
        OrderId brokerOrderId,
        string? reason,
        CancellationToken ct);
}
```

**ApplyAsync (約定)**:

1. `_orderRepo.FindByBrokerOrderIdAsync(brokerCode, brokerOrderId)`
2. `order.ApplyExecution(fill)` で OrderState 更新 + イベント発火
3. `order.IsTerminal` なら `_tracker.Untrack(brokerOrderId)`
4. NewOrder 約定 → `Position` 生成 + `_positionRepo.AddAsync`、Auto なら `_autoPositionMetaStore.UpsertAsync`
5. ExitOrder 約定 → `_positionRepo.FindByIdAsync(execution.TargetPositionId)` → `position.ApplyClosure(qty)` → `IsClosed` なら `_positionRepo.RemoveAsync` + メタ削除、部分なら `_positionRepo.UpdateAsync`

**ApplyTerminationAsync (取消/失効/拒否)**:

1. `_orderRepo.FindByBrokerOrderIdAsync`
2. `order.IsTerminal` ならスキップ (冪等)
3. `order.RemainingQuantity > 0` なら `order.MarkTerminated(Cancelled, reason)`
4. ExitOrder で TargetExecutionId あれば対応 Position の `ReleaseReservation(unfilled)` → `Update`

---

### 3.3 Positions: 返済・ドテン・手動返済

#### 3.3.1 `ClosePositionUseCase`

ファイル: `Positions/ClosePositionUseCase.cs`

```csharp
public sealed class ClosePositionUseCase
{
    public ClosePositionUseCase(
        IBrokerAdapter broker,
        IOrderRepository orderRepo,
        IPositionRepository positionRepo,
        IOrderMetadataStore metaStore,
        IPendingOrderTracker tracker,
        IDateTimeProvider clock,
        ILogger<ClosePositionUseCase> logger);

    public Task<ClosePositionResult> ExecuteAsync(
        ExitOrderIntent intent,
        CancellationToken ct);
}
```

**手順**:

1. `_positionRepo.FindMatchingForCloseAsync(...)` で候補建玉取得
2. `PositionMatcher.BuildPlan(candidates, requestedQty)` で `ClosurePlan` 作成
3. 各 `ClosureAllocation` をループ:
   - `position.ReserveForClose(qty)` (拘束)
   - `_positionRepo.UpdateAsync(position)`
   - `Order` 集約生成 (`TradeType=ExitOrder`, `TargetExecutionId=position.Id`)
   - `_orderRepo.AddAsync(order)`
   - `_broker.ClosePositionAsync(ClosePositionRequest{...})`
   - レスポンス分岐:
     - `Accepted`: `order.MarkSubmitted`, メタ永続化, `_tracker.Track`
     - `Rejected`: `position.ReleaseReservation(qty)` で解放、`order.MarkTerminated`
     - `NetworkError`: 拘束は保持 (発注通った可能性)、`order.MarkTerminated`
4. `Shortfall > 0` なら警告ログ
5. `ClosePositionResult(intent, plan, exitOrders, shortfall)` 返却

#### 3.3.2 `DotenUseCase`

ファイル: `Positions/DotenUseCase.cs`

```csharp
public sealed class DotenUseCase
{
    public DotenUseCase(
        ClosePositionUseCase closePositionUseCase,
        PlaceNewOrderUseCase placeNewOrderUseCase);

    public Task<DotenResult> ExecuteAsync(
        DotenIntent intent,
        CancellationToken ct);
}
```

**手順**:

1. `DotenIntent → ExitOrderIntent` → `_closePositionUseCase.ExecuteAsync()` (旧建玉返済)
2. `DotenIntent → NewOrderIntent` → `_placeNewOrderUseCase.ExecuteAsync()` (新方向発注)

⚠️ 返済の約定通知を待たずに新規を発火。一時的両建てを許容 (旧仕様踏襲)。

#### 3.3.3 `ManualClosePositionUseCase`

ファイル: `Positions/ManualClosePositionUseCase.cs`

```csharp
public sealed class ManualClosePositionUseCase
{
    public ManualClosePositionUseCase(
        IBrokerAdapter broker,
        IOrderRepository orderRepo,
        IPositionRepository positionRepo,
        IOrderMetadataStore metaStore,
        IPendingOrderTracker tracker,
        IDateTimeProvider clock,
        ILogger<ManualClosePositionUseCase> logger);

    public Task<ManualCloseResult> ExecuteAsync(
        ExecutionId targetExecutionId,
        Quantity? quantity = null,
        OrderType orderType = OrderType.BestMarket,
        Price? limitPrice = null,
        Price? stopPrice = null,
        TimeInForce timeInForce = TimeInForce.FAS,
        CancellationToken ct = default);
}
```

**戻り値**: `ManualCloseResult` (status: Accepted/Rejected/NetworkError + 詳細)

静的ファクトリ:
- `PositionNotFound(id)`
- `NoAvailableQuantity(pos)`
- `QuantityExceedsAvailable(pos, qty)`
- `BrokerException(pos, order, ex)`

---

### 3.4 Sync: 起動初期化・ストリーム購読

#### 3.4.1 `BrokerSessionInitializerService` (HostedService)

ファイル: `Sync/BrokerSessionInitializerService.cs`

**責務**: 起動時に kabu トークン warm-up → 注文一覧取得 → 建玉再同期。

```csharp
public sealed class BrokerSessionInitializerService : IHostedService
{
    public Task StartAsync(CancellationToken ct);  // Step 1 → 2 → 3
    public Task StopAsync(CancellationToken ct);   // no-op
}
```

各 Step の例外は catch して LogWarning。起動継続。

#### 3.4.2 `ExecutionStreamSubscriberService` (HostedService)

ファイル: `Sync/ExecutionStreamSubscriberService.cs`

**責務**: `IBrokerAdapter.ExecutionStream` を購読し、各 `ExecutionEvent` を `ExecutionApplier.ApplyAsync` に渡す。

DI scope を切ってからハンドラを呼ぶ (Transient な ExecutionApplier を解決するため)。

#### 3.4.3 `OrderTerminationSubscriberService` (HostedService)

ファイル: `Sync/OrderTerminationSubscriberService.cs`

**責務**: `IOrderSnapshotNotifier.SnapshotsUpdated` (ポーリング結果) を購読し、終端状態の注文に対し `ExecutionApplier.ApplyTerminationAsync` を呼ぶ。

→ Filled は `ExecutionStreamSubscriberService` で既処理。本サービスは Cancelled / Expired / Rejected 用。

#### 3.4.4 `OrderMetadata` / `IOrderMetadataStore`

ファイル: `Sync/OrderMetadata.cs`

```csharp
public sealed record OrderMetadata(
    string BrokerOrderId,
    string Strategy,
    int Interval,
    TradeMode TradeMode);

public interface IOrderMetadataStore
{
    Task<IReadOnlyList<OrderMetadata>> LoadAllAsync(CancellationToken ct);
    Task UpsertAsync(OrderMetadata meta, CancellationToken ct);
    Task RemoveAsync(string brokerOrderId, CancellationToken ct);
    OrderMetadata? TryGet(string brokerOrderId);  // ホットパス同期取得
}
```

実装: `JsonOrderMetadataStore` → `orders-metadata.json`

#### 3.4.5 `AutoPositionMetadata` / `IAutoPositionMetadataStore`

ファイル: `Sync/AutoPositionMetadata.cs`

```csharp
public sealed record AutoPositionMetadata(
    string ExecutionId,
    string Strategy,
    int Interval);

public interface IAutoPositionMetadataStore
{
    Task<IReadOnlyList<AutoPositionMetadata>> LoadAllAsync(CancellationToken ct);
    Task UpsertAsync(AutoPositionMetadata meta, CancellationToken ct);
    Task RemoveAsync(string executionId, CancellationToken ct);
    Task SyncToActiveSetAsync(IEnumerable<string> activeExecutionIds, CancellationToken ct);
}
```

実装: `JsonAutoPositionMetadataStore` → `auto-positions.json`

#### 3.4.6 `IPendingOrderTracker`

ファイル: `Sync/IPendingOrderTracker.cs`

```csharp
public interface IPendingOrderTracker
{
    void Track(string brokerOrderId);
    void Untrack(string brokerOrderId);
    IReadOnlyList<string> GetAll();
    bool IsEmpty { get; }
}
```

実装: `InMemoryPendingOrderTracker` (Infrastructure)。

#### 3.4.7 `IOrderInitialFetcher`

```csharp
public interface IOrderInitialFetcher
{
    Task<int> InitialFetchOrdersAsync(CancellationToken ct);
}
```

実装: `KabuOrderPollingService` が兼任 (`/orders` 全件取得 → UI push)。

---

### 3.5 Brokers: 限月計算

#### 3.5.1 `DerivMonthCalculator`

ファイル: `Brokers/DerivMonthCalculator.cs`

**責務**: 日経 225 先物の現月コード判定。SQ 日 (第 2 金曜日の前日) を境に翌限月に切替。

純粋関数:

```csharp
public static int Compute(DateTime utcNow);
```

---

### 3.6 Common: 共通ポート

| インターフェース | 責務 |
|---|---|
| `IDateTimeProvider` | `DateTime UtcNow` (テスト差替用) |
| `IOrderChangeNotifier` | `event OrderChangedEventArgs Changed` (リポジトリ → UI) |
| `IOrderSnapshotNotifier` | `event OrderSnapshotsEventArgs SnapshotsUpdated` + `LatestSnapshots` (ポーリング → UI) |
| `IPositionChangeNotifier` | `event PositionChangedEventArgs Changed` |
| `IPriceUpdateNotifier` | `event PriceTick PriceUpdated` |

---

### 3.7 DI: 登録拡張

#### `ApplicationServiceCollectionExtensions`

ファイル: `DI/ApplicationServiceCollectionExtensions.cs`

```csharp
public static IServiceCollection AddBrokerBridgeApplication(this IServiceCollection s)
{
    // UseCase (Transient)
    s.AddTransient<PlaceNewOrderUseCase>();
    s.AddTransient<ClosePositionUseCase>();
    s.AddTransient<DotenUseCase>();
    s.AddTransient<ManualClosePositionUseCase>();
    s.AddTransient<ExecutionApplier>();
    s.AddTransient<SignalHandler>();

    // Hosted (起動順)
    s.AddHostedService<BrokerSessionInitializerService>();   // 最先
    s.AddHostedService<ExecutionStreamSubscriberService>();
    s.AddHostedService<OrderTerminationSubscriberService>();

    // Ports (Singleton)
    s.AddSingleton<IAutoTradeGate, AutoTradeGate>();
    s.AddSingleton<IAutoTradeInstrumentProvider, AutoTradeInstrumentProvider>();

    return s;
}
```

---

## 4. Infrastructure 層クラス

### 4.1 Brokers/Kabu

#### 4.1.1 `KabuAdapter` (IBrokerAdapter 実装)

ファイル: `Brokers/Kabu/KabuAdapter.cs`

**責務**: Domain の発注リクエストを kabu REST API に橋渡し。

**内部 Subject**:
- `_executionSubject : Subject<ExecutionEvent>` — `ExecutionStream` の実体
- `_priceSubject : Subject<PriceTick>` — `PriceStream` の実体

**メソッド対応**:

| Domain メソッド | kabu HTTP |
|---|---|
| `PlaceOrderAsync` | POST `/sendorder/future` |
| `ClosePositionAsync` | POST `/sendorder/future` (`TradeType=2`) |
| `CancelOrderAsync` | PUT `/cancelorder` |
| `GetPositionsAsync` | GET `/positions?product=3` |
| `GetOrdersAsync` | GET `/orders?product=3` |
| `GetQuoteAsync` | GET `/board/{symbol}@{exchange}` |
| `SubscribePriceAsync` | PUT `/register` |
| `UnsubscribePriceAsync` | PUT `/unregister` |
| `ResolveFutureSymbolAsync` | GET `/symbolname/future` + `/symbol/{}@{}?info=true` |

**Exchange 動的判定**:

| 用途 | 規則 |
|---|---|
| 発注 (`PlaceOrderAsync` / `ClosePositionAsync`) | 06:00–15:45 JST → 23 (日中), その他 → 24 (夜間) |
| 板登録 / 気配取得 | 2 (日通し) 固定 |

**Push メソッド** (KabuOrderPollingService / KabuBoardWebSocketService から呼ばれる):

```csharp
internal void PushExecution(ExecutionEvent ev) => _executionSubject.OnNext(ev);
internal void PushPriceTick(PriceTick tick) => _priceSubject.OnNext(tick);
```

#### 4.1.2 `KabuApiClient` (HTTP Client)

ファイル: `Brokers/Kabu/KabuApiClient.cs`

**責務**: kabu API 全エンドポイント呼出を一元管理。トークン自動 Refresh。

**主要メソッド**:

| メソッド | エンドポイント |
|---|---|
| `SendOrderAsync(req)` | POST /sendorder/future (401 時自動 Refresh + 1 リトライ) |
| `CancelOrderAsync(id, password)` | PUT /cancelorder |
| `GetPositionsAsync()` | GET /positions?product=3 |
| `GetOrdersAsync()` | GET /orders?product=3 |
| `GetOrderByIdAsync(id)` | GET /orders?product=3&id={id} |
| `GetBoardAsync(symbol)` | GET /board/{symbol}@{exchange} |
| `GetSymbolNameAsync(code, month)` | GET /symbolname/future |
| `GetSymbolFutureAsync(symbol)` | GET /symbol/{symbol}@{exchange}?info=true |
| `RegisterSymbolAsync(symbol)` | PUT /register |
| `UnregisterSymbolAsync(symbol)` | PUT /unregister |

**タイムアウト**:

| 種別 | デフォルト | 設定キー |
|---|---|---|
| 発注系 | 5 秒 | `Kabu.OrderTimeoutSeconds` |
| 照会系 | 10 秒 | `Kabu.QueryTimeoutSeconds` |

**ログマスク**: `SendOrderAsync` の Request ログでは `Password` を `***` に置換。

#### 4.1.3 `KabuTokenService`

ファイル: `Brokers/Kabu/KabuTokenService.cs`

**責務**: API トークン取得・キャッシュ管理 (排他制御付き)。

```csharp
public Task<string> GetTokenAsync(CancellationToken ct);    // キャッシュ有効ならそのまま
public Task<string> RefreshAsync(CancellationToken ct);     // 強制再取得
```

- キャッシュ寿命: 12 時間
- `SemaphoreSlim` でマルチスレッド競合防止
- ApiPassword 未設定時 `InvalidOperationException`

#### 4.1.4 `KabuOptions`

`appsettings.json` の `Kabu` セクションと `LocalSettingsValues` をマージ。

| キー | デフォルト |
|---|---|
| BaseUrl | http://localhost:18080/kabusapi |
| WebSocketUrl | ws://localhost:18080/kabusapi/websocket |
| ApiPassword | (LocalSettings) |
| OrderPassword | (LocalSettings) |
| OrderTimeoutSeconds | 5 |
| QueryTimeoutSeconds | 10 |
| Product | 3 (先物固定) |
| Exchange | 2 (日通し、登録/気配で使用) |

#### 4.1.5 `KabuMappers` (Domain ↔ DTO 変換)

ファイル: `Brokers/Kabu/KabuMappers.cs`

主要メソッド:

| メソッド | 変換 |
|---|---|
| `ToKabuRequest(OrderRequest)` | Domain 新規 → KabuSendOrderRequest |
| `ToKabuRequest(ClosePositionRequest)` | Domain 返済 → KabuSendOrderRequest (CashMargin=3 + HoldID 同梱) |
| `ToOrderResult(KabuSendOrderResponse)` | kabu 応答 → Domain OrderResult |
| `ToPositionSnapshot(KabuPositionDto)` | kabu → Domain |
| `ToOrderSnapshot(KabuOrderDto)` | kabu → Domain (加重平均約定価格計算含む) |
| `ToQuoteSnapshot(KabuBoardDto)` | kabu → Domain |

**注文タイプマッピング**:

| Domain | kabu FrontOrderType |
|---|---|
| Market | 120 |
| BestMarket | 20 |
| Limit | 20 |
| Stop | 30 |

**TimeInForce マッピング**:

| Domain | kabu |
|---|---|
| FAS | 1 |
| FAK | 2 |
| FOK | 3 |

**Side マッピング** (kabu の逆順仕様):

| Domain Side | kabu |
|---|---|
| Buy (1) | 2 |
| Sell (2) | 1 |

#### 4.1.6 `KabuOrderPollingService` (HostedService)

ファイル: `Brokers/Kabu/KabuOrderPollingService.cs`

**責務**: 1 秒間隔で kabu `/orders` を pending 注文に絞ってポーリング。約定検出 → `ExecutionEvent` 発火。

**インターフェース実装**:
- `IHostedService`
- `IOrderInitialFetcher` (起動時全件取得)
- `IOrderSnapshotNotifier` (UI 通知)

**主要メソッド**:

| メソッド | 用途 |
|---|---|
| `StartAsync(ct)` | ポーリングループ開始 |
| `StopAsync(ct)` | 終了 |
| `InitialFetchOrdersAsync(ct)` | 起動時 `/orders` 全件取得 + UI push |
| `PollLoopAsync(ct)` | 1 秒間隔。`_tracker.IsEmpty` ならスキップ |
| `PollOnceAsync(ct)` | 各 pending OrderId を `/orders?id={id}` で照会 |
| `BuildExecutionEventAsync(order, detail, ct)` | kabu 約定明細 → ExecutionEvent (TargetPositionId 復元含む) |

**重複ガード**: `ConcurrentDictionary<string, byte> _seenExecutionIds` で同一 ExecutionID の再発火を防止。

#### 4.1.7 `KabuBoardWebSocketService` (HostedService)

ファイル: `Brokers/Kabu/KabuBoardWebSocketService.cs`

**責務**: kabu WebSocket 接続 → board push 受信 → PriceTick 発火。

**主要メソッド**:

| メソッド | 用途 |
|---|---|
| `StartAsync(ct)` | ConnectionLoopAsync 開始 |
| `StopAsync(ct)` | 切断 + ループ終了 (3 秒タイムアウト) |
| `ConnectionLoopAsync(ct)` | 失敗時 5 秒待機 → 再接続 |
| `ReceiveLoopAsync(ws, ct)` | UTF-8 デコード → KabuBoardPushDto → PriceTick |

**イベント**: `event Action<PriceTick> PriceUpdated` (Rx 非依存の購読者向け)。

---

### 4.2 Persistence (リポジトリ実装)

| クラス | ポート | 永続化 |
|---|---|---|
| `InMemoryOrderRepository` | `IOrderRepository` + `IOrderChangeNotifier` | メモリ |
| `InMemoryPositionRepository` | `IPositionRepository` + `IPositionChangeNotifier` | メモリ |
| `InMemoryPendingOrderTracker` | `IPendingOrderTracker` | メモリ |
| `JsonOrderMetadataStore` | `IOrderMetadataStore` | `orders-metadata.json` |
| `JsonAutoPositionMetadataStore` | `IAutoPositionMetadataStore` | `auto-positions.json` |

メモリリポジトリは将来 SQLite/EF Core 実装に差替予定。Repository 抽象が Domain にあるため UI/Application を変えずに済む。

### 4.3 Strategies

#### `JsonStrategyRegistry`

ファイル: `Strategies/JsonStrategyRegistry.cs`

**ポート**: `IStrategyRegistry`
**永続化**: `strategies.json`

### 4.4 Webhooks

#### 4.4.1 `HttpWebhookListener`

ファイル: `Webhooks/HttpWebhookListener.cs`

**責務**: `localhost:8001/webhook` で HttpListener。POST 受信 → `SignalPayloadParser.Parse` → `SignalHandler.HandleAsync`。

**応答**:

| 状況 | HTTP |
|---|---|
| 成功 | 200 + outcome class name |
| JSON パース失敗 | 400 |
| POST 以外 | 405 |
| 内部例外 | 500 |

#### 4.4.2 `WebhookHostedService`

ファイル: `Webhooks/WebhookHostedService.cs`

**責務**: 起動・停止時に `HttpWebhookListener.StartAsync/StopAsync` を呼ぶ。

#### 4.4.3 `SignalPayloadParser`

ファイル: `Webhooks/SignalPayloadParser.cs`

**責務**: TradingView 形式 JSON → `SignalPayload` への構造化。

バリデーション:
- `alert_name` 必須
- `interval` 正の整数
- `ticker` 必須
- `strategy.order_action` / `market_position` / `prev_market_position` 必須

正規化:
- `OrderContracts` / `MarketPositionSize`: double → int (AwayFromZero 丸め)
- `OrderPrice`: double → decimal (負値は 0 に正規化)

#### 4.4.4 `RawWebhookPayload` / `WebhookListenerOptions`

データクラス。詳細は [`webhook-api-spec.md`](./webhook-api-spec.md) 参照。

### 4.5 Logging

#### `SerilogConfiguration`

ファイル: `Logging/SerilogConfiguration.cs`

3 sink (Console / File / UI) を設定。

### 4.6 DI

#### `InfrastructureServiceCollectionExtensions`

ファイル: `DI/InfrastructureServiceCollectionExtensions.cs`

| メソッド | 登録内容 |
|---|---|
| `AddBrokerBridgeInfrastructure()` | リポジトリ / メタストア / Tracker |
| `AddBrokerBridgeWebhook()` | HttpWebhookListener, WebhookHostedService, SignalPayloadParser |
| `AddBrokerBridgeKabu()` | KabuTokenService(Singleton), KabuApiClient, KabuAdapter, KabuOrderPollingService(HostedService), KabuBoardWebSocketService(HostedService), 名前付き HttpClient "Kabu" |

---

## 5. UI 層クラス

### 5.1 ViewModels

#### 5.1.1 `MainViewModel`

ファイル: `ViewModels/MainViewModel.cs`

**主要 ObservableProperty**:

- 状態: `IsAutoTradeEnabled`, `BrokerStatus`, `WebhookStatus`, `StateMessage`
- 現在値: `CurrentPrice`, `BidPrice`, `BidQty`, `AskPrice`, `AskQty`
- 発注フォーム: `ManualOrderInstrument`, `OrderQty`, `LimitPrice`, `StopPrice`, `OrderType`, `SelectedTimeInForce`
- リスト: `Strategies`, `Positions`, `Orders`, `LogEntries`
- 選択: `SelectedPosition`, `SelectedOrder`

**Command**:

| Command | 動作 |
|---|---|
| `PlaceBuyOrderCommand` | 手動買発注 |
| `PlaceSellOrderCommand` | 手動売発注 |
| `ExitPositionCommand` | 選択建玉返済 |
| `CancelOrderCommand` | 選択注文取消 |
| `RefreshStatusCommand` | ステータス再取得 |

**コンストラクタ依存** (16 個):

```
IBrokerAdapter, HttpWebhookListener, PlaceNewOrderUseCase, ManualClosePositionUseCase,
IPositionRepository, IPositionChangeNotifier, IOrderSnapshotNotifier, IPriceUpdateNotifier,
IStrategyRegistry, IOrderMetadataStore, LocalSettingsStore, IAutoTradeGate,
IAutoTradeInstrumentProvider, ILogger
```

#### 5.1.2 `SettingsViewModel`

ファイル: `ViewModels/SettingsViewModel.cs`

設定ダイアログのバインド先。`LocalSettingsStore` を読み書きする。

#### 5.1.3 `StrategyManagerViewModel`

ファイル: `ViewModels/StrategyManagerViewModel.cs`

戦略 CRUD。`IStrategyRegistry` を介して `strategies.json` 操作。

#### 5.1.4 `InstrumentDefinition`

```csharp
public partial class InstrumentDefinition : ObservableObject
{
    public string DisplayName { get; set; }
    public string FutureCode { get; set; }          // kabu API コード ("NK225mini")
    public string? ResolvedSymbolCode { get; set; }  // 起動時に解決
    public string? ContractMonth { get; set; }       // "2026年6月限"
    public decimal ProfitMultiplier { get; set; }    // Mini=100, Micro=10
    public decimal LastPrice { get; set; }
    public decimal BidPrice { get; set; }
    public decimal AskPrice { get; set; }
    public int BidQty { get; set; }
    public int AskQty { get; set; }
}
```

### 5.2 Views (XAML)

| ファイル | 説明 |
|---|---|
| `App.xaml` | アプリケーションリソース (Wpf.Ui Dark, Converters) |
| `Views/MainWindow.xaml` | メインウィンドウ |
| `Views/SettingsWindow.xaml` | 設定ダイアログ |
| `Views/StrategyManagerWindow.xaml` | 戦略管理ダイアログ |

### 5.3 Services / Helpers

| クラス | 用途 |
|---|---|
| `LocalSettingsStore` | DPAPI 暗号化された appsettings.Local.json の Load/Save |
| `UILayoutStore` | ui-layout.json (ウィンドウサイズ / カラム幅) の Load/Save |
| `UiLogSink` | Serilog → UI 用 ObservableCollection に流す Sink |
| `OrderTypeChoiceConverter` | enum ↔ RadioButton.IsChecked |
| `GroupAggregateConverter` | (将来: 建玉グループ集計) |

### 5.4 App.xaml.cs

**責務**: アプリケーション起動。グローバル例外ハンドラ設定 → `BootstrapAsync` → DI 構築 → HostedService 起動 → MainWindow 表示。

**フィールド**:

```csharp
public static bool IsDemoMode { get; private set; }
private IHost? _host;
```

---

## 6. クラス依存関係 (主要)

```
┌────────────────────────────────────────────────────────┐
│ MainWindow ─bind─ MainViewModel ─uses─ UseCases       │
│                       │            ─uses─ IBrokerAdapter│
│                       │            ─uses─ IRepository  │
│                       └──notify── IPositionChangeNotifier│
└────────────────────────────────────────────────────────┘
            ↓                              ↑
┌────────────────────────────────────┐    │
│ HttpWebhookListener                 │    │
│  ↓                                  │    │
│ SignalHandler ─uses─ UseCases ──┐  │    │
│                                   │  │    │
└───────────────────────────────────┘  │    │
                                       ▼    │
┌────────────────────────────────────────────┐
│ UseCases (PlaceNewOrder / ClosePosition /   │
│ Doten / ManualClose / ExecutionApplier)     │
│  - Order/Position 集約 (Domain)              │
│  - IBrokerAdapter                            │
│  - IOrderRepository / IPositionRepository    │
└────────────────────────────────────────────┘
                       ↓
┌────────────────────────────────────────────┐
│ KabuAdapter ─uses─ KabuApiClient ─uses─    │
│   KabuTokenService                          │
│ KabuOrderPollingService ─push─▶ KabuAdapter│
│ KabuBoardWebSocketService ─push─▶ KabuAdapter│
└────────────────────────────────────────────┘
```

---

## 7. 拡張ポイント

新規ブローカー追加 (例: 楽天証券) のチェックリスト:

1. `Infrastructure/Brokers/Rakuten/` に新規ディレクトリ
2. `RakutenAdapter : IBrokerAdapter` 実装
3. `RakutenApiClient` 実装
4. DTO / Mapper 実装
5. `BrokerCode.Rakuten` を定義 (既存) し、`DI/InfrastructureServiceCollectionExtensions` に `AddBrokerBridgeRakuten()` 追加
6. UI で発注先選択 UI を追加 (現在は kabu hardcode)
7. Domain 層は**変更不要**

新規 UseCase 追加:

1. `Application/{Domain}/{NewUseCase}.cs` に `ExecuteAsync` を持つクラス
2. DI に `AddTransient<NewUseCase>()`
3. 呼び出し元 (SignalHandler / ViewModel) に DI

---

## 8. テスト容易性のためのポイント

- 全 UseCase は依存を Constructor Injection
- `IDateTimeProvider` 差替で時刻固定テスト可
- `FakeBrokerAdapter` で kabu 接続なしに UseCase テスト可
- `Stub{IPort}` で UI / インフラ単独テスト可

詳細は [`test-spec.md`](./test-spec.md) 参照。

---

## 変更履歴

| バージョン | 日付 | 変更内容 |
|---|---|---|
| 1.0.0 | 2026-05-26 | 初版作成 |
