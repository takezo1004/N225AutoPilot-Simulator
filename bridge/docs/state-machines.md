# 状態遷移図 (State Machines)

**バージョン**: 1.0.0
**作成日**: 2026-05-26
**最終更新**: 2026-05-26

---

## 1. このドキュメントの目的

`Order` / `Position` 集約および外部接続コンポーネントのライフサイクルを**状態遷移表**として記録する。
状態 / 遷移 / トリガー / ガード / 副作用 を 1 表にまとめ、不正遷移を起こさないコード保守の基準にする。

関連:
- [domain-model.md](./domain-model.md) — Order / Position の不変条件
- [sequence-diagrams.md](./sequence-diagrams.md) — 状態遷移に至るフロー

---

## 2. Order の状態遷移

### 2.1 全体図

```
           ┌──────────────────────────────────────┐
           │                                       │
       [Created]                                   │
           │                                       │
           │ MarkSubmitted (BrokerOrderId)         │
           ▼                                       │
       [Submitted] ──────┐                         │
           │              │                         │
           │ ApplyExec    │ MarkTerminated         │
           │ (partial)    │                         │
           ▼              ▼                         │
   [PartiallyFilled] ─[Cancelled / Expired /       │
           │             Rejected] (terminal)      │
           │              ▲                         │
           │ ApplyExec   │                         │
           │ (last fill) │ MarkTerminated          │
           ▼              │                         │
       [Filled] (terminal)│                         │
                          │                         │
                          └─────────────────────────┘
```

### 2.2 状態遷移表

| 現状態 | トリガー | ガード | 次状態 | 副作用 / イベント |
|---|---|---|---|---|
| Created | `MarkSubmitted(brokerOrderId, utcNow)` | なし | Submitted | `BrokerOrderId` 設定 / `SubmittedAt = utcNow` / `OrderSubmittedEvent` 発火 |
| Created | `MarkTerminated(Rejected/Expired)` | terminal でない | Rejected / Expired | `TerminatedAt` 設定 / `OrderTerminatedEvent` 発火 |
| Submitted | `ApplyExecution(fill)` | `fill.Quantity <= RemainingQuantity` | PartiallyFilled (まだ残あり) / Filled (残ゼロ) | `Executions` に追加 / `OrderExecutedEvent(IsFullyFilled=?)` 発火 |
| Submitted | `MarkTerminated(Cancelled/Expired/Rejected)` | terminal でない | Cancelled / Expired / Rejected | `TerminatedAt` 設定 / `OrderTerminatedEvent` 発火 |
| PartiallyFilled | `ApplyExecution(fill)` | `fill.Quantity <= RemainingQuantity` | PartiallyFilled (まだ残あり) / Filled (残ゼロ) | 同上 |
| PartiallyFilled | `MarkTerminated(Cancelled/Expired/Rejected)` | terminal でない | Cancelled / Expired / Rejected | 残数量分の Order は Cancelled 等で終わる。返済注文ならアプリ層が Position.ReleaseReservation を別途呼ぶ |
| Filled | * | terminal (再遷移不可) | - | InvalidOperationException |
| Cancelled | * | terminal | - | InvalidOperationException |
| Expired | * | terminal | - | InvalidOperationException |
| Rejected | * | terminal | - | InvalidOperationException |

### 2.3 状態の意味

| 状態 | 意味 | 残数量 |
|---|---|---|
| Created | 集約生成済。ブローカー未送信。 | RequestedQuantity (= 全量) |
| Submitted | ブローカー受付完了。約定待ち。 | RequestedQuantity (= 全量) |
| PartiallyFilled | 一部約定。残あり。 | RequestedQuantity - Σ ExecutedQty |
| Filled | 全量約定。終端。 | 0 |
| Cancelled | ユーザー or システム取消。終端。 | RequestedQuantity - 約定済 (残あり) |
| Expired | 時間条件 (FAK/FOK 等) で期限切れ。終端。 | 同上 |
| Rejected | ブローカーが受付拒否。終端。 | RequestedQuantity (約定なし) |

### 2.4 約定数量と状態判定の関係

`Order.ApplyExecution` 内の判定 (擬似コード):

```csharp
_executions.Add(fill);
var totalFilled = Σ _executions.Quantity;

if (totalFilled == RequestedQuantity)
{
    State = OrderState.Filled;
    isFullyFilled = true;
}
else if (totalFilled < RequestedQuantity)
{
    State = OrderState.PartiallyFilled;
    isFullyFilled = false;
}
else  // totalFilled > RequestedQuantity
{
    throw new DomainException("...");  // 不変条件違反
}

RaiseEvent(new OrderExecutedEvent(..., isFullyFilled));
```

---

## 3. Position の状態遷移

### 3.1 全体図

Position の "状態" は明示的 enum ではなく、`LeaveQuantity` / `HoldQuantity` から導出する。

```
                      [生成]
                  (LeaveQty=N, HoldQty=0)
                         │
                         │ ReserveForClose(q)
                         ▼
                  [Reserved]
              (LeaveQty=N, HoldQty=q)
                ▲           │
                │           │ ApplyClosure(q)
                │           ▼
   ReserveForClose       [Partially Closed]
   (追加返済発注)    (LeaveQty=N-q, HoldQty=0)
                            │
                            │ ReserveForClose / ApplyClosure 繰り返し
                            ▼
                    [LeaveQty == 0]
                  PositionClosedEvent 発火
                      Repository.RemoveAsync
                            │
                            ▼
                        (消滅)
```

```
                      [生成]
                         │
                         │ ReserveForClose(q)
                         ▼
                  [Reserved]
                         │
                         │ ReleaseReservation(q)
                         │   (注文 Cancelled / Rejected 時)
                         ▼
                  [LeaveQty=N, HoldQty=0]
                  (元の生成直後と同じ)
```

### 3.2 状態遷移表

| 現状態 (LeaveQty, HoldQty) | トリガー | ガード | 次状態 | 副作用 / イベント |
|---|---|---|---|---|
| (N, 0) — 生成直後 / 解放後 | `ReserveForClose(q)` | `q <= AvailableForClose = N - 0` | (N, q) | `HoldQuantity += q` / `PositionUpdatedEvent` 発火 |
| (N, h) — 拘束あり | `ReserveForClose(q)` | `q <= AvailableForClose = N - h` | (N, h+q) | 同上 |
| (N, h) — 拘束あり | `ApplyClosure(q, utcNow)` | `q <= h` | (N-q, h-q) | `LeaveQty -= q`, `HoldQty -= q` / `PositionUpdatedEvent` 発火 |
| (q, q) — 残全量拘束 | `ApplyClosure(q, utcNow)` | `q <= h` | (0, 0) | `LeaveQty = 0`, `HoldQty = 0` / `PositionClosedEvent` 発火 / Repository.RemoveAsync 対象 |
| (N, h) — 拘束あり | `ReleaseReservation(q)` | `q <= h` | (N, h-q) | `HoldQty -= q` / `PositionUpdatedEvent` 発火 |
| (0, 0) — Closed | * | closed (操作不可) | - | DomainException |

### 3.3 不変条件 (常時保証)

- `LeaveQuantity.Value >= 0`
- `HoldQuantity.Value >= 0`
- `HoldQuantity <= LeaveQuantity`
- `LeaveQuantity == 0 ⇒ HoldQuantity == 0` (closed なら拘束もゼロ)

### 3.4 AvailableForClose の計算

```
AvailableForClose = LeaveQuantity - HoldQuantity
```

意味: 「これから新たに返済発注に投入できる残り枚数」

### 3.5 シナリオ例

#### シナリオ A: 単純な全量返済

```
初期        : (10, 0)
ReserveForClose(10) → (10, 10)
ApplyClosure(10)    → (0, 0)  ← PositionClosedEvent
                                 Repository.RemoveAsync
```

#### シナリオ B: 部分返済

```
初期        : (10, 0)
ReserveForClose(3) → (10, 3)
ApplyClosure(3)    → (7, 0)   ← PositionUpdatedEvent
ReserveForClose(7) → (7, 7)
ApplyClosure(7)    → (0, 0)   ← PositionClosedEvent
```

#### シナリオ C: 注文キャンセル時の解放

```
初期        : (10, 0)
ReserveForClose(5) → (10, 5)   ← 返済注文を発注
(注文 Cancelled 通知)
ReleaseReservation(5) → (10, 0)  ← 拘束を解放
```

#### シナリオ D: 部分約定後のキャンセル

```
初期        : (10, 0)
ReserveForClose(5) → (10, 5)
ApplyClosure(2)    → (8, 3)    ← 2 枚だけ約定
(注文 Cancelled 通知, 残 3 枚分)
ReleaseReservation(3) → (8, 0)
```

---

## 4. アプリケーション ライフサイクル

### 4.1 状態定義

| 状態 | 説明 |
|---|---|
| NotStarted | プロセス起動前 |
| Bootstrapping | DI 構築・HostedService 起動中 |
| Running | 全 HostedService 稼働中。UI 表示中 |
| ShuttingDown | OnClosing 中 (HostedService 停止待ち) |
| Stopped | プロセス終了 |

### 4.2 遷移

```
NotStarted ──(Launch)──▶ Bootstrapping ──(OK)──▶ Running ──(WM_CLOSE / Exit)──▶ ShuttingDown ──(OK)──▶ Stopped
                              │
                              └──(Fatal)──▶ Stopped (例外ダイアログ表示後)
```

### 4.3 起動失敗時

| 失敗箇所 | 動作 |
|---|---|
| Serilog 初期化 | Console に書き出し、ダイアログ表示後 Stopped |
| LocalSettings Load | 暗号化破損 → 警告ダイアログ、空設定で続行 |
| DI Build | 必須実装欠落 → ダイアログ表示後 Stopped |
| HostedService.StartAsync (kabu 系) | 警告ログのみ、Running 継続 (kabu 未接続でも UI 起動) |

---

## 5. kabu WebSocket 接続の状態遷移

### 5.1 状態定義

| 状態 | 説明 |
|---|---|
| Disconnected | 接続なし |
| Connecting | ConnectAsync 中 |
| Connected | 接続中。ReceiveLoop 動作 |
| Disconnecting | StopAsync 中 |

### 5.2 遷移

```
Disconnected ──(ConnectionLoop start)──▶ Connecting ──(OK)──▶ Connected
                    ▲                         │                    │
                    │                         │ Connect failure    │
                    │                         ▼                    │
                    │                    [5秒 wait]                │
                    │                         │                    │
                    └─────────────────────────┘                    │
                                                                    │
                                       ◀────────(ReceiveLoop ex)───┘
                                                                    │
                                       ◀────────(StopAsync)─────────┘
                                                ▼
                                       Disconnecting ──(close)──▶ Disconnected (terminal)
```

### 5.3 再接続ポリシー

- 接続失敗 / ReceiveLoop 例外 → 5 秒待機 → 再試行
- StopAsync 中の例外 → ログのみで終了
- `CancellationToken` で stop 要求が来たらループを抜ける

---

## 6. KabuOrderPollingService の状態遷移

### 6.1 状態定義

| 状態 | 説明 |
|---|---|
| Stopped | 起動前 / 停止後 |
| Running | 1 秒間隔ループ動作中 |
| StoppingRequested | 停止要求受領 |

### 6.2 遷移とポーリング条件

```
Stopped ──(StartAsync)──▶ Running ──(1 tick)──┐
                          ▲                    │
                          │                    ▼
                          │              Tracker.IsEmpty?
                          │                    │
                          │       YES (skip)─┐ │ NO (poll)
                          │                  │ │
                          │                  └─┴─▶ each pending OrderId:
                          │                          GET /orders?id={id}
                          │                          BuildExecutionEvent (RecType=8)
                          │                          KA.PushExecution
                          │                          IOrderSnapshotNotifier.Notify
                          │                          (State=5 なら Tracker.Untrack)
                          └─────────────────────────┘
                                            
StoppingRequested ◀──(StopAsync)
                          │
                          ▼
                       Stopped
```

---

## 7. Webhook Listener の状態遷移

| 状態 | 説明 |
|---|---|
| NotListening | 起動前 / 停止後 |
| Listening | HttpListener 待受中 |
| Disposing | StopAsync 中 |

```
NotListening ──(StartAsync)──▶ Listening ──(StopAsync)──▶ Disposing ──(close)──▶ NotListening
                                  │
                                  └──(各リクエスト)──▶ ListenLoopAsync → HandleRequestAsync (背景)
```

---

## 8. SignalHandler 処理の判定フロー

下表は `SignalHandler.HandleAsync` の判定順序を状態遷移として記述したもの。

| 検証段階 | 結果 | 次段階 |
|---|---|---|
| 1. IAutoTradeGate.IsEnabled | false | **AutoTradeDisabled** (終了) |
| 1. IAutoTradeGate.IsEnabled | true | 2 へ |
| 2. ISignalAuthenticator.Authenticate | false | **AuthFailed** (終了) |
| 2. ISignalAuthenticator.Authenticate | true | 3 へ |
| 3. IStrategyRegistry.IsEnabled | false | **Ignored("strategy disabled")** (終了) |
| 3. IStrategyRegistry.IsEnabled | true | MarkSignalReceived → 4 へ |
| 4. IAutoTradeInstrumentProvider.ResolvedSymbolCode | null | **Ignored("instrument not resolved")** (終了) |
| 4. IAutoTradeInstrumentProvider.ResolvedSymbolCode | 非 null | 5 へ |
| 5. SignalInterpreter.Interpret | NewOrderIntent | PNO 実行 → **NewOrderDispatched** |
| 5. SignalInterpreter.Interpret | ExitOrderIntent | CP 実行 → **ExitOrderDispatched** |
| 5. SignalInterpreter.Interpret | DotenIntent | Doten 実行 → **DotenDispatched** |
| 5. SignalInterpreter.Interpret | IgnoreIntent | **Ignored(reason)** (終了) |
| 5. SignalInterpreter 例外 | 例外 | **Interpretation_Failed** (終了) |

---

## 9. SignalInterpreter の Intent 決定マトリックス

| prev_market_position | market_position | order_action | 結果 Intent |
|---|---|---|---|
| flat | long | buy | **NewOrderIntent(Buy)** |
| flat | short | sell | **NewOrderIntent(Sell)** |
| long | flat | sell | **ExitOrderIntent(OriginalSide=Buy, 全量)** |
| short | flat | buy | **ExitOrderIntent(OriginalSide=Sell, 全量)** |
| long | long | sell | **ExitOrderIntent(部分返済)** |
| short | short | buy | **ExitOrderIntent(部分返済)** |
| short | long | buy | **DotenIntent(Short→Long)** |
| long | short | sell | **DotenIntent(Long→Short)** |
| flat | flat | * | **IgnoreIntent("no-op")** |
| long | long | buy | **IgnoreIntent("unexpected")** |
| short | short | sell | **IgnoreIntent("unexpected")** |
| その他組合せ | - | - | **IgnoreIntent("undefined transition")** |

---

## 10. 注文タイプと時間条件の対応 (UI 状態)

UI で OrderType が変わると `AvailableTimeInForces` が連動して切り替わる:

| OrderType | LimitPrice | StopPrice | AvailableTimeInForces | Default |
|---|---|---|---|---|
| Market | 無効 (灰色) | 無効 | FAS, FAK | FAS |
| Limit | 必須 | 無効 | FAS, FAK, FOK | FAS |
| Stop | 必須 (指値部分) | 必須 | FAS, FAK, FOK | FAS |
| BestMarket | 無効 | 無効 | FAK | FAK |

---

## 11. デモモード状態

`App.IsDemoMode` が `true` の場合、複数コンポーネントが**起動しない**:

| コンポーネント | 通常 | デモ |
|---|---|---|
| BrokerSessionInitializerService | 起動 | **起動しない** |
| ExecutionStreamSubscriberService | 起動 | **起動しない** |
| OrderTerminationSubscriberService | 起動 | **起動しない** |
| KabuOrderPollingService | 起動 | **起動しない** |
| KabuBoardWebSocketService | 起動 | **起動しない** |
| HttpWebhookListener | 起動 | **起動しない** |
| MainWindow | 起動 | 起動 |
| MainViewModel | LoadInitialStateAsync 等 | **SeedDemoData / InsertDemoLogs** のみ |
| strategies.json / auto-positions.json | 読み書き | **読み書きしない** |

---

## 12. 状態異常時の対処原則

| 状況 | 動作 |
|---|---|
| Order が terminal なのに ApplyExecution 呼ばれた | InvalidOperationException 投出 |
| Position が closed なのに ReserveForClose 呼ばれた | DomainException 投出 |
| Order の RemainingQuantity を超える ApplyExecution | DomainException 投出 |
| Position の HoldQuantity を超える ApplyClosure | DomainException 投出 |
| Position の HoldQuantity を超える ReleaseReservation | DomainException 投出 |

これらは**通常運用では到達しない**例外。発生時はバグかデータ不整合のため、Serilog で Error ログ + UI に状態表示。

---

## 13. 変更履歴

| バージョン | 日付 | 変更内容 |
|---|---|---|
| 1.0.0 | 2026-05-26 | 初版作成 |
