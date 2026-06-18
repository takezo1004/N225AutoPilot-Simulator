# ドメインモデル詳細 (Domain Model)

**バージョン**: 1.0.0
**作成日**: 2026-05-26
**最終更新**: 2026-05-26
**対象**: `src/N225BrokerBridge.Domain/`

---

## 1. このドキュメントの目的

本書は Domain 層に存在する**集約・エンティティ・値オブジェクト・ドメインイベント・ドメインサービス・リポジトリ抽象**を網羅し、その**不変条件**と**振る舞い**を定義する。
コード変更時の正本となる。コードと本書がズレたら**コードが正でなく本書が正**となる方針 (本書を更新するためにコード修正したかを判定する)。

関連ドキュメント:
- [ユビキタス言語辞書](./ubiquitous-language.md) — 用語の定義
- [状態遷移図](./state-machines.md) — Order / Position のライフサイクル
- [シーケンス図集](./sequence-diagrams.md) — 集約間の協調

---

## 2. レイヤー構造と原則

Domain 層は **他のいかなる層にも依存しない**。

- ❌ `using N225BrokerBridge.Infrastructure.*` 不可
- ❌ `using N225BrokerBridge.Application.*` 不可
- ✅ `using Microsoft.Extensions.Logging.Abstractions` は OK (抽象のため)

許される依存:
- .NET BCL (`System.*`)
- `Microsoft.Extensions.Logging.Abstractions` (抽象のみ)

---

## 3. ファイル構成

```
src/N225BrokerBridge.Domain/
├── Common/
│   ├── AggregateRoot.cs        集約ルート基底
│   ├── Entity.cs                エンティティ基底
│   ├── DomainException.cs       ドメイン例外
│   └── IDomainEvent.cs          ドメインイベント マーカー
├── Orders/
│   ├── Order.cs                 Order 集約ルート
│   ├── Execution.cs             Execution エンティティ
│   ├── OrderState.cs            OrderState 列挙
│   ├── OrderType.cs             OrderType 列挙
│   ├── TimeInForce.cs           TimeInForce 列挙
│   ├── TradeType.cs             TradeType 列挙
│   ├── IOrderRepository.cs      Order リポジトリ抽象
│   └── Events/
│       ├── OrderSubmittedEvent.cs
│       ├── OrderExecutedEvent.cs
│       └── OrderTerminatedEvent.cs
├── Positions/
│   ├── Position.cs              Position 集約ルート
│   ├── PositionMatcher.cs       消化計画ドメインサービス
│   ├── IPositionRepository.cs   Position リポジトリ抽象
│   └── Events/
│       ├── PositionOpenedEvent.cs
│       ├── PositionUpdatedEvent.cs
│       └── PositionClosedEvent.cs
├── ValueObjects/
│   ├── OrderId.cs
│   ├── ExecutionId.cs
│   ├── Price.cs
│   ├── Quantity.cs
│   ├── Side.cs
│   ├── BrokerCode.cs
│   ├── SymbolCode.cs
│   ├── StrategyName.cs
│   └── TradeMode.cs
└── Brokers/
    ├── IBrokerAdapter.cs        ブローカー抽象
    ├── OrderRequest.cs
    ├── OrderResult.cs
    ├── ClosePositionRequest.cs
    ├── ExecutionEvent.cs
    ├── OrderSnapshot.cs
    ├── PositionSnapshot.cs
    ├── PriceTick.cs
    ├── QuoteSnapshot.cs
    └── ResolvedSymbol.cs
```

---

## 4. 集約 (Aggregates)

### 4.1 Order 集約

#### 集約ルート: `Orders/Order.cs`

責務: 1 注文 = 1 ブローカーへの 1 アクション。ライフサイクル管理と分割約定対応。

#### プロパティ

| 名前 | 型 | 説明 |
|---|---|---|
| `Id` | `Guid` | アプリ内一意 ID (ブローカー OrderId と別) |
| `BrokerCode` | `BrokerCode` | 発注先 |
| `Strategy` | `StrategyName` | 戦略名 (手動は "Manual") |
| `Interval` | `int` | 足 (分)。Auto なら > 0、Manual なら 0 |
| `TradeMode` | `TradeMode` | Auto / Manual |
| `Symbol` | `SymbolCode` | 銘柄 |
| `Side` | `Side` | Buy / Sell |
| `TradeType` | `TradeType` | NewOrder / ExitOrder |
| `OrderType` | `OrderType` | Market / Limit / Stop / BestMarket |
| `TimeInForce` | `TimeInForce` | FAK / FAS / FOK |
| `RequestedQuantity` | `Quantity` | 発注枚数 |
| `LimitPrice` | `Price` | 指値 (成行では 0) |
| `StopPrice` | `Price` | 逆指値トリガー (逆指値以外では 0) |
| `TargetExecutionId` | `ExecutionId?` | 返済対象建玉 (ExitOrder のみ必須) |
| `State` | `OrderState` | 現在状態 |
| `BrokerOrderId` | `OrderId?` | ブローカー採番 (Submitted 以降) |
| `CreatedAt` | `DateTime (UTC)` | 集約生成時刻 |
| `SubmittedAt` | `DateTime? (UTC)` | ブローカー送信完了時刻 |
| `TerminatedAt` | `DateTime? (UTC)` | 終端遷移時刻 |
| `Executions` | `IReadOnlyList<Execution>` | 取り込んだ約定 (0..*) |
| `CumulativeExecutedQuantity` | `Quantity` | 累計約定数量 |
| `RemainingQuantity` | `Quantity` | RequestedQuantity - 累計 |
| `IsTerminal` | `bool` | Filled / Cancelled / Expired / Rejected |

#### メソッド

| メソッド | 引数 | 不変条件 / 副作用 |
|---|---|---|
| `MarkSubmitted` | `OrderId brokerOrderId`, `DateTime utcNow` | `State == Created` の時のみ。`State → Submitted`、`BrokerOrderId`/`SubmittedAt` 設定、`OrderSubmittedEvent` 発火 |
| `ApplyExecution` | `Execution fill` | `State ∈ {Submitted, PartiallyFilled}` の時のみ。`Executions` に追加、`State` 更新 (`PartiallyFilled` or `Filled`)、`OrderExecutedEvent` 発火 |
| `MarkTerminated` | `OrderState terminalState`, `string? reason`, `DateTime utcNow` | `State.IsTerminal() == false` の時のみ。`terminalState ∈ {Cancelled, Expired, Rejected}` 必須。`OrderTerminatedEvent` 発火 |

#### 不変条件

- **INV-O1**: `RequestedQuantity = Σ Executions.Quantity + RemainingQuantity`
- **INV-O2**: 終端状態からは状態遷移不可
- **INV-O3**: `Auto` モードは `Interval > 0`、`Manual` モードは `Interval == 0`
- **INV-O4**: `TradeType == ExitOrder` なら `TargetExecutionId` 必須
- **INV-O5**: `OrderType == Stop` のみ `StopPrice > 0`、その他は `StopPrice == 0`

#### 状態遷移

```
Created ──MarkSubmitted──▶ Submitted ──ApplyExecution──▶ PartiallyFilled
                              │                                   │
                              │                                   ApplyExecution
                              │                                   ▼
                              │                                Filled
                              │                                  (terminal)
                              ▼
                         MarkTerminated
                              ▼
                       Cancelled / Expired / Rejected
                              (terminal)
```

詳細は [`state-machines.md`](./state-machines.md) §2 を参照。

---

### 4.2 Execution エンティティ

#### ファイル: `Orders/Execution.cs`

Order 集約内の子エンティティ。1 注文の 1 約定を表す。

#### プロパティ

| 名前 | 型 | 説明 |
|---|---|---|
| `Id` | `ExecutionId` | ブローカー採番。建玉識別子にもなる |
| `Quantity` | `Quantity` | 約定枚数 |
| `Price` | `Price` | 約定価格 |
| `ExecutedAt` | `DateTime (UTC)` | 約定時刻 |

#### 不変条件

- **INV-E1**: `Quantity.Value > 0`
- **INV-E2**: `Price.Value >= 0`
- **INV-E3**: `Id.Value` は非空

---

### 4.3 Position 集約

#### 集約ルート: `Positions/Position.cs`

責務: 1 建玉 = 1 約定単位で保持。返済フロー (拘束・返済・解放) の厳密管理。

#### プロパティ

| 名前 | 型 | 説明 |
|---|---|---|
| `Id` | `ExecutionId` | 建玉識別子 (= 新規約定の ExecutionId) |
| `BrokerCode` | `BrokerCode` | 保有ブローカー |
| `Strategy` | `StrategyName` | 建玉を生んだ戦略 |
| `Interval` | `int` | 足 (分) |
| `TradeMode` | `TradeMode` | Auto / Manual |
| `Symbol` | `SymbolCode` | 銘柄 |
| `Side` | `Side` | 買建 / 売建 |
| `EntryPrice` | `Price` | 建値 |
| `OpenedAt` | `DateTime (UTC)` | 建玉成立時刻 |
| `LeaveQuantity` | `Quantity` | 残保有枚数 |
| `HoldQuantity` | `Quantity` | 返済中拘束枚数 |
| `IsClosed` | `bool` | `LeaveQuantity == 0` |
| `AvailableForClose` | `Quantity` | `LeaveQuantity - HoldQuantity` |

#### メソッド

| メソッド | 引数 | 不変条件 / 副作用 |
|---|---|---|
| `ReserveForClose` | `Quantity qty` | `qty <= AvailableForClose` 必須。`HoldQuantity += qty`、`PositionUpdatedEvent` 発火 |
| `ApplyClosure` | `Quantity qty`, `DateTime utcNow` | `qty <= HoldQuantity` 必須。`LeaveQuantity -= qty`、`HoldQuantity -= qty`、`LeaveQuantity == 0` なら `PositionClosedEvent` 発火、それ以外は `PositionUpdatedEvent` 発火 |
| `ReleaseReservation` | `Quantity qty` | `qty <= HoldQuantity` 必須。`HoldQuantity -= qty`、`PositionUpdatedEvent` 発火 |

#### 不変条件

- **INV-P1**: `LeaveQuantity.Value >= 0`
- **INV-P2**: `HoldQuantity.Value >= 0`
- **INV-P3**: `HoldQuantity <= LeaveQuantity`
- **INV-P4**: 終了後 (`LeaveQuantity == 0`) は状態変更不可
- **INV-P5**: `Auto` モードは `Interval > 0`、`Manual` モードは `Interval == 0`

#### 返済フロー

```
新規約定 → Position 生成 (LeaveQty=N, HoldQty=0)
   │
   │ 返済発注
   ▼
ReserveForClose(q1) (LeaveQty=N, HoldQty=q1)
   │
   │ 返済約定
   ▼
ApplyClosure(q1) (LeaveQty=N-q1, HoldQty=0)
   │ (LeaveQty != 0 なら継続、== 0 なら閉鎖)
   ▼
(全量返済) PositionClosedEvent → Repository.RemoveAsync
(部分返済) PositionUpdatedEvent
```

詳細は [`state-machines.md`](./state-machines.md) §3 を参照。

---

## 5. 値オブジェクト (Value Objects)

すべて record / readonly record struct でイミュータブル。

### 5.1 OrderId

```csharp
public sealed record OrderId(string Value);
```

| 不変条件 | INV: `Value` は非空 |
|---|---|

### 5.2 ExecutionId

```csharp
public sealed record ExecutionId(string Value);
```

| 不変条件 | INV: `Value` は非空 |
|---|---|

⚠️ **重要な設計注釈**: 新規約定の ExecutionId は建玉識別子として使えるが、返済約定の ExecutionId は「約定自体の新規 ID」であり、返済対象建玉の元 ID とは別。建玉の元 ID は呼び出し元が `TargetPositionId` で別途保持する。

### 5.3 Price

```csharp
public readonly record struct Price(decimal Value);
```

| 不変条件 | INV: `Value >= 0` |
|---|---|

#### 静的 / プロパティ

- `Price.Zero` — ゼロ価格 (成行)
- `IsZero` — ゼロ判定

#### 演算子

- `+`, `-` (負にならない検証)
- `<`, `>`, `<=`, `>=`

### 5.4 Quantity

```csharp
public readonly record struct Quantity(int Value);
```

| 不変条件 | INV: `Value >= 0` (整数) |
|---|---|

#### 静的 / プロパティ

- `Quantity.Zero`
- `IsZero`, `IsPositive`
- `Quantity.Min(a, b)` — 2 つのうち小さい方 (跨ぎ消化計算で多用)

### 5.5 Side

```csharp
public enum Side { Buy = 1, Sell = 2 }
```

⚠️ enum int 値は kabu API の値と一致**しない**。kabu には `ToKabuCode()` を使う。

#### 拡張メソッド

| メソッド | 機能 |
|---|---|
| `Opposite()` | 反対サイド (返済注文のサイド決定) |
| `ToDisplay()` | "買" / "売" 日本語表示 |
| `ToKabuCode()` | kabu API 値 (1=売 / 2=買) — Side 列挙とは**逆順** |

### 5.6 BrokerCode

```csharp
public sealed record BrokerCode(string Value);
```

#### 定数

- `BrokerCode.Kabu` — "kabu"
- `BrokerCode.Rakuten` — "rakuten" (将来)

#### 静的ファクトリ

- `BrokerCode.Of(string)`

### 5.7 SymbolCode

```csharp
public sealed record SymbolCode(string Value);
```

| 不変条件 | INV: `Value` は非空 |
|---|---|

ブローカー固有形式。kabu では数値コード ("167060019")。

### 5.8 StrategyName

```csharp
public sealed record StrategyName(string Value);
```

| 不変条件 | INV: `Value` は非空 |
|---|---|

手動操作時は `"Manual"`。

### 5.9 TradeMode

```csharp
public enum TradeMode { Manual = 0, Auto = 1 }
```

---

## 6. ドメインイベント (Domain Events)

### 6.1 IDomainEvent

```csharp
public interface IDomainEvent
{
    DateTime OccurredAt { get; }   // UTC
}
```

### 6.2 OrderSubmittedEvent

| フィールド | 型 |
|---|---|
| AggregateId | Guid |
| BrokerCode | BrokerCode |
| BrokerOrderId | OrderId |
| OccurredAt | DateTime |

発火: `Order.MarkSubmitted`

### 6.3 OrderExecutedEvent

| フィールド | 型 |
|---|---|
| AggregateId | Guid |
| ExecutionId | ExecutionId |
| ExecutedQuantity | Quantity |
| ExecutedPrice | Price |
| IsFullyFilled | bool |
| OccurredAt | DateTime |

発火: `Order.ApplyExecution` (部分/全量どちらでも)

### 6.4 OrderTerminatedEvent

| フィールド | 型 |
|---|---|
| AggregateId | Guid |
| TerminalState | OrderState |
| Reason | string? |
| OccurredAt | DateTime |

発火: `Order.MarkTerminated`

### 6.5 PositionOpenedEvent

| フィールド | 型 |
|---|---|
| PositionId | ExecutionId |
| BrokerCode | BrokerCode |
| Strategy | StrategyName |
| Side | Side |
| OpenedQuantity | Quantity |
| EntryPrice | Price |
| OccurredAt | DateTime |

発火: Position コンストラクタ (新規生成時)

### 6.6 PositionUpdatedEvent

| フィールド | 型 |
|---|---|
| PositionId | ExecutionId |
| LeaveQuantity | Quantity |
| HoldQuantity | Quantity |
| OccurredAt | DateTime |

発火: `Position.ReserveForClose` / `ApplyClosure` (部分返済) / `ReleaseReservation`

### 6.7 PositionClosedEvent

| フィールド | 型 |
|---|---|
| PositionId | ExecutionId |
| OccurredAt | DateTime |

発火: `Position.ApplyClosure` で `LeaveQuantity == 0` に到達

---

## 7. 列挙型 (Enums)

### 7.1 OrderState

| 値 | 説明 | 終端? |
|---|---|---|
| Created | 集約生成直後 | × |
| Submitted | ブローカー送信完了 | × |
| PartiallyFilled | 一部約定 | × |
| Filled | 全量約定 | ✅ |
| Cancelled | 取消完了 | ✅ |
| Expired | 期限切れ | ✅ |
| Rejected | ブローカー拒否 | ✅ |

#### 拡張

- `IsTerminal()` — Filled / Cancelled / Expired / Rejected で true

### 7.2 OrderType

| 値 | 説明 |
|---|---|
| Market | 成行 |
| Limit | 指値 |
| Stop | 逆指値 |
| BestMarket | 最良気配 (kabu SelectedOrder=1 相当) |

### 7.3 TimeInForce

| 値 | 説明 |
|---|---|
| FAK | Fill And Kill |
| FAS | Fill And Store |
| FOK | Fill Or Kill |

### 7.4 TradeType

| 値 | 説明 |
|---|---|
| NewOrder | 新規建て |
| ExitOrder | 返済 |

---

## 8. ドメインサービス (Domain Services)

### 8.1 PositionMatcher

ファイル: `Positions/PositionMatcher.cs`

責務: **部分返済の建玉選択**。候補建玉群に対し要求枚数を消化計画として割り当てる純粋関数。

#### 設計判断 (2026-05-17 ユーザー合意)

1. **選択順序**: `ExecutionId` 順 (決定性確保)
2. **跨ぎ消化**: 許可。建玉 A 全消化 + 建玉 B 部分消化のような組合せを返す
3. **要求 > 残合計**: 残合計まで消化し、`Shortfall` で不足を通知

#### メソッド

```csharp
public static ClosurePlan BuildPlan(
    IEnumerable<Position> candidates,
    Quantity requestedQuantity)
```

#### 戻り値: `ClosurePlan`

| フィールド | 型 | 説明 |
|---|---|---|
| Requested | Quantity | 元の要求枚数 |
| Allocations | IReadOnlyList<ClosureAllocation> | 各建玉への割当 |
| Shortfall | Quantity | 不足枚数 |

#### `ClosureAllocation`

| フィールド | 型 |
|---|---|
| Position | Position |
| Quantity | Quantity |

#### サンプル

要求 = 5、候補 = [P1: AvailableForClose=2, P2: AvailableForClose=4, P3: AvailableForClose=10]

→ 計画:
- Alloc(P1, 2)
- Alloc(P2, 3)
- Shortfall = 0

要求 = 20 で同じ候補なら:
- Alloc(P1, 2), Alloc(P2, 4), Alloc(P3, 10)
- Shortfall = 4

---

## 9. リポジトリ抽象 (Repository Interfaces)

リポジトリはドメイン層に置き、Infrastructure 層が実装する (DIP)。

### 9.1 IOrderRepository

```csharp
public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken ct);
    Task UpdateAsync(Order order, CancellationToken ct);
    Task<Order?> FindByIdAsync(Guid id, CancellationToken ct);
    Task<Order?> FindByBrokerOrderIdAsync(BrokerCode brokerCode, OrderId brokerOrderId, CancellationToken ct);
    Task<IReadOnlyList<Order>> FindActiveAsync(CancellationToken ct);
}
```

### 9.2 IPositionRepository

```csharp
public interface IPositionRepository
{
    Task AddAsync(Position position, CancellationToken ct);
    Task UpdateAsync(Position position, CancellationToken ct);
    Task RemoveAsync(ExecutionId id, CancellationToken ct);
    Task<Position?> FindByIdAsync(ExecutionId id, CancellationToken ct);
    Task<IReadOnlyList<Position>> FindMatchingForCloseAsync(
        BrokerCode brokerCode,
        StrategyName strategy,
        int interval,
        TradeMode tradeMode,
        Side originalSide,
        CancellationToken ct);
    Task<IReadOnlyList<Position>> FindActiveAsync(CancellationToken ct);
}
```

---

## 10. ブローカー抽象 (Brokers / IBrokerAdapter)

### 10.1 IBrokerAdapter

責務: 証券会社固有 API (REST / WebSocket / COM 等) を統一インターフェースに隠蔽。

```csharp
public interface IBrokerAdapter
{
    BrokerCode BrokerCode { get; }
    bool IsConnected { get; }
    IObservable<ExecutionEvent> ExecutionStream { get; }
    IObservable<PriceTick> PriceStream { get; }

    Task<OrderResult> PlaceOrderAsync(OrderRequest req, CancellationToken ct);
    Task<OrderResult> ClosePositionAsync(ClosePositionRequest req, CancellationToken ct);
    Task<OrderResult> CancelOrderAsync(OrderId brokerOrderId, CancellationToken ct);
    Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(CancellationToken ct);
    Task<IReadOnlyList<OrderSnapshot>> GetOrdersAsync(CancellationToken ct);
    Task<QuoteSnapshot?> GetQuoteAsync(SymbolCode symbol, CancellationToken ct);
    Task SubscribePriceAsync(SymbolCode symbol, CancellationToken ct);
    Task SubscribePricesAsync(IEnumerable<SymbolCode> symbols, CancellationToken ct);
    Task UnsubscribePriceAsync(SymbolCode symbol, CancellationToken ct);
    Task<ResolvedSymbol?> ResolveFutureSymbolAsync(string futureCode, int derivMonth, CancellationToken ct);
}
```

### 10.2 リクエスト / レスポンス型

#### OrderRequest

新規発注リクエスト。Order 集約からアダプタ呼出時に組立。

| フィールド | 型 |
|---|---|
| CorrelationId | Guid (= Order.Id) |
| Strategy | StrategyName |
| Interval | int |
| TradeMode | TradeMode |
| Symbol | SymbolCode |
| Side | Side |
| OrderType | OrderType |
| TimeInForce | TimeInForce |
| Quantity | Quantity |
| LimitPrice | Price |
| StopPrice | Price |

#### OrderResult

| フィールド | 型 |
|---|---|
| CorrelationId | Guid |
| Status | OrderResultStatus (Accepted / Rejected / NetworkError) |
| BrokerOrderId | OrderId? |
| ErrorCode | string? |
| ErrorMessage | string? |
| ReceivedAt | DateTime (UTC) |

#### ClosePositionRequest

| フィールド | 型 |
|---|---|
| CorrelationId | Guid |
| Strategy | StrategyName |
| Interval | int |
| TradeMode | TradeMode |
| Symbol | SymbolCode |
| OriginalSide | Side (= 建玉サイド。発注時はアダプタが反対側に変換) |
| TargetExecutionId | ExecutionId |
| Quantity | Quantity |
| OrderType | OrderType |
| TimeInForce | TimeInForce |
| LimitPrice | Price |
| StopPrice | Price |

#### ExecutionEvent

| フィールド | 型 |
|---|---|
| BrokerCode | BrokerCode |
| BrokerOrderId | OrderId |
| ExecutionId | ExecutionId |
| TradeType | TradeType |
| Side | Side |
| Symbol | SymbolCode |
| Quantity | Quantity |
| Price | Price |
| ExecutedAt | DateTime (UTC) |
| TargetPositionId | ExecutionId? (返済約定のみ) |

#### OrderSnapshot / PositionSnapshot / PriceTick / QuoteSnapshot / ResolvedSymbol

[Application 層マッピング](#) または対応するソースファイルを参照。

---

## 11. 共通基盤 (Common)

### 11.1 AggregateRoot\<TId\>

| メンバー | 説明 |
|---|---|
| `DomainEvents` | 未配信イベント (IReadOnlyList) |
| `RaiseEvent(IDomainEvent)` | 内部キューに追加 |
| `ClearDomainEvents()` | キューをクリア (ディスパッチ完了後) |

### 11.2 Entity\<TId\>

ID で同一性を判定する基底。

| メンバー | 説明 |
|---|---|
| `Id` | 識別子 |
| `Equals`, `GetHashCode`, `==`, `!=` | ID ベース同一性 |

### 11.3 DomainException

ドメインルール違反専用例外。インフラ例外 (HTTP/IO) と区別。

派生:
- `InvalidValueObjectException` — VO コンストラクタの不変条件違反

### 11.4 IDomainEvent

マーカーインターフェース。`OccurredAt` プロパティのみ。

---

## 12. 集約境界 (Aggregate Boundary) と整合性

### 12.1 Order 集約の境界

- 中: Order + Execution
- 外: Position (別集約)

Order と Position の関係:
- 新規約定 → ExecutionApplier (Application 層) が Order と Position の両方を更新
- 1 トランザクション内で 1 集約のみ書き換える原則は守られていない (両方更新する)
  - これは "Sagas" や "Domain Events に分割" するほどの複雑度ではないため、シンプルさを優先

### 12.2 Position 集約の境界

- 中: Position 単体 (Execution は持たない)
- 外: Order (別集約)

返済時に必要な情報:
- 建玉 Side / Strategy / Interval (絞り込み用)
- 元 ExecutionId (TargetExecutionId として注文に同梱)

### 12.3 ドメインイベントによる協調

イベントは「**他コンテキストに伝える事実**」。本プロジェクトでは UI 通知に活用しているが、別集約への副作用伝播には使っていない (DI 直接呼び出し)。

---

## 13. テスト観点 (Domain.Tests)

### 13.1 Order テスト

- コンストラクタの初期状態
- ExitOrder で TargetExecutionId 必須
- Created → Submitted の遷移成功
- Filled からの再遷移は拒否
- 部分約定の累計計算
- ApplyExecution が OrderExecutedEvent を発火

### 13.2 Position テスト

- 初期 LeaveQuantity / HoldQuantity
- ReserveForClose で AvailableForClose を超えると例外
- ApplyClosure で部分返済時の状態
- ApplyClosure で全量返済時 PositionClosedEvent 発火
- ReleaseReservation で HoldQuantity 減算
- Closed 後の操作は拒否

### 13.3 PositionMatcher テスト

- 単一建玉での全量消化
- 複数建玉跨ぎ消化
- 要求 > 残合計の Shortfall 算出
- 要求 0 の境界
- 候補なしの境界

### 13.4 値オブジェクトテスト

- Price / Quantity 負値で例外
- OrderId / ExecutionId 空で例外
- Side.Opposite / ToKabuCode

---

## 14. 注釈・要注意箇所

| 場所 | 注意点 |
|---|---|
| `ExecutionId` | 新規約定 ID と返済約定 ID は別物。建玉の元 ID は `TargetPositionId` で別途保持 |
| `Side` enum | kabu API 値と enum int 値が逆順。必ず `ToKabuCode()` を使う |
| `Price`, `QuoteSnapshot.BidPrice/AskPrice` | kabu の BID/ASK 命名は通常と逆。`adapters/kabu.md` 参照 |
| `Position.TradeMode` × `Interval` | Auto は Interval > 0、Manual は Interval == 0 で固定 |
| `PositionMatcher.BuildPlan` | 集約を**変更しない** (純粋関数)。`ReserveForClose` を呼ぶのは Application 層 |

---

## 変更履歴

| バージョン | 日付 | 変更内容 |
|---|---|---|
| 1.0.0 | 2026-05-26 | 初版作成 (現行コードのコード読みに基づく完全マッピング) |
