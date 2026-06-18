# シーケンス図集 (Sequence Diagrams)

**バージョン**: 1.0.0
**作成日**: 2026-05-26
**最終更新**: 2026-05-26
**対象**: 主要な処理フロー (12 シナリオ)

---

## 1. このドキュメントの目的

主要シナリオの**クラス間の呼び出し順序**と**データの流れ**を ASCII シーケンス図で記録する。
コードを追わなくても流れを把握でき、新規開発者・障害対応者が「次に何が起きるはず」を予測できるようにする。

各シーケンスは Mermaid 風記法と ASCII 図を併記。Mermaid をレンダリングできる環境では Mermaid を、テキストエディタでは ASCII を見る運用。

関連:
- [class-design.md](./class-design.md) — 登場クラスの責務
- [state-machines.md](./state-machines.md) — Order / Position の状態遷移
- [functional-spec.md](./functional-spec.md) — 各シナリオの機能仕様

---

## 2. アクター / コンポーネント凡例

| 略号 | 正式名 |
|---|---|
| TV | TradingView Pine Strategy |
| WL | HttpWebhookListener |
| Parser | SignalPayloadParser |
| SH | SignalHandler |
| Interp | SignalInterpreter |
| SR | IStrategyRegistry |
| AG | IAutoTradeGate |
| AIP | IAutoTradeInstrumentProvider |
| PNO | PlaceNewOrderUseCase |
| CP | ClosePositionUseCase |
| Doten | DotenUseCase |
| MCP | ManualClosePositionUseCase |
| EA | ExecutionApplier |
| KA | KabuAdapter |
| KAC | KabuApiClient |
| KTS | KabuTokenService |
| KOP | KabuOrderPollingService |
| KBWS | KabuBoardWebSocketService |
| OR | IOrderRepository |
| PR | IPositionRepository |
| OMS | IOrderMetadataStore |
| APMS | IAutoPositionMetadataStore |
| Tracker | IPendingOrderTracker |
| MVM | MainViewModel |
| User | ユーザー (人間) |
| Kabu | kabu Station |

---

## 3. シナリオ一覧

| 番号 | シナリオ | §セクション |
|---|---|---|
| S-01 | アプリ起動 | §4 |
| S-02 | Webhook → 新規発注 (Auto) | §5 |
| S-03 | Webhook → 返済 (Auto) | §6 |
| S-04 | Webhook → ドテン | §7 |
| S-05 | Webhook → 認証失敗 / ゲート OFF / 戦略未登録 | §8 |
| S-06 | 手動買発注 | §9 |
| S-07 | 手動返済 | §10 |
| S-08 | 手動キャンセル | §11 |
| S-09 | 新規約定通知 | §12 |
| S-10 | 返済約定通知 | §13 |
| S-11 | 注文取消検出 (ポーリング) | §14 |
| S-12 | 価格ティック受信 (WebSocket) | §15 |
| S-13 | アプリ終了 | §16 |

---

## 4. S-01: アプリ起動

```
User              App.xaml.cs            DI Host           HostedServices         MainWindow
  │                   │                     │                    │                    │
  │─Launch (.exe)─────▶                     │                    │                    │
  │                   │─Parse "--demo"      │                    │                    │
  │                   │─Set IsDemoMode      │                    │                    │
  │                   │─BootstrapAsync─────▶│                    │                    │
  │                   │                     │─Serilog init       │                    │
  │                   │                     │─LocalSettings Load │                    │
  │                   │                     │─Build DI container │                    │
  │                   │                     │  (Add Application, │                    │
  │                   │                     │   Infra, Webhook,  │                    │
  │                   │                     │   Kabu, UI)        │                    │
  │                   │                     │                    │                    │
  │                   │      (NOT demo)     │─StartAsync─────────▶                    │
  │                   │                     │                    │BrokerSessionInit:  │
  │                   │                     │                    │  Step 1 token warm │
  │                   │                     │                    │  Step 2 orders     │
  │                   │                     │                    │  Step 3 positions  │
  │                   │                     │                    │ExecutionStreamSub  │
  │                   │                     │                    │OrderTerminationSub │
  │                   │                     │                    │KabuOrderPolling    │
  │                   │                     │                    │KabuBoardWebSocket  │
  │                   │                     │                    │HttpWebhookListener │
  │                   │                     │◀───OK──────────────│                    │
  │                   │                     │                    │                    │
  │                   │─Resolve MainWindow──▶                    │                    │
  │                   │─Show()─────────────────────────────────────────────────────────▶
  │                   │                     │                    │                    │
  │                   │                     │                    │              [MainViewModel ctor]
  │                   │                     │                    │              ├─Subscribe notifiers
  │                   │                     │                    │              ├─LoadInstruments
  │                   │                     │                    │              ├─LoadStrategies
  │                   │                     │                    │              ├─RefreshStatus
  │                   │                     │                    │              └─(demo) SeedDemo
  │                   │                     │                    │                (real) LoadInitial
  │                   │                     │                    │                       TryResolve
  │                   │                     │                    │                       Register
  ▼                   ▼                     ▼                    ▼                    ▼
```

### 4.1 起動 Step 1〜3 の詳細

```
BrokerSessionInitializerService.StartAsync
  │
  ├─[Step 1] KabuAdapter.GetPositionsAsync (warm-up token)
  │           ├─KabuApiClient.GetPositionsAsync
  │           │   ├─KabuTokenService.GetTokenAsync (cache miss → POST /token)
  │           │   └─GET /positions?product=3 (X-API-KEY)
  │           └─Domain.PositionSnapshot[] 返却
  │  
  ├─[Step 2] IOrderInitialFetcher.InitialFetchOrdersAsync
  │           ├─KabuApiClient.GetOrdersAsync
  │           │   └─GET /orders?product=3
  │           └─UI push (IOrderSnapshotNotifier.SnapshotsUpdated event)
  │
  └─[Step 3] PositionReconciliation
              ├─KabuAdapter.GetPositionsAsync (再取得)
              ├─IAutoPositionMetadataStore.LoadAllAsync
              ├─(各 PositionSnapshot)
              │   ├─メタあり → Position 生成 (TradeMode=Auto, Strategy/Interval メタから)
              │   └─メタなし → Position 生成 (TradeMode=Manual)
              ├─IPositionRepository.AddAsync
              └─IAutoPositionMetadataStore.SyncToActiveSetAsync (dead meta 削除)
```

---

## 5. S-02: Webhook 受信 → 新規発注 (Auto モード)

```
TV          WL          Parser      SH          AG  Auth  SR    AIP   Interp    PNO         OR    KA          KAC          Kabu
 │           │             │         │           │   │    │     │     │         │           │     │            │             │
 │POST       │             │         │           │   │    │     │     │         │           │     │            │             │
 │/webhook   │             │         │           │   │    │     │     │         │           │     │            │             │
 │JSON       │             │         │           │   │    │     │     │         │           │     │            │             │
 │──────────▶│             │         │           │   │    │     │     │         │           │     │            │             │
 │           │─Parse(json)─▶         │           │   │    │     │     │         │           │     │            │             │
 │           │           SignalPayload          │   │    │     │     │         │           │     │            │             │
 │           │◀────────────│         │           │   │    │     │     │         │           │     │            │             │
 │           │─HandleAsync─────────▶│           │   │    │     │     │         │           │     │            │             │
 │           │                       │─IsEnabled?▶   │    │     │     │         │           │     │            │             │
 │           │                       │◀────true──│   │    │     │     │         │           │     │            │             │
 │           │                       │─Authenticate?─▶    │     │     │         │           │     │            │             │
 │           │                       │◀──true────────│    │     │     │         │           │     │            │             │
 │           │                       │─IsEnabled(name,interval)?▶    │     │         │           │     │            │             │
 │           │                       │◀────true────────────│    │     │         │           │     │            │             │
 │           │                       │─MarkSignalReceived─▶│    │     │         │           │     │            │             │
 │           │                       │─ResolvedSymbol?─────────────▶  │         │           │     │            │             │
 │           │                       │◀──SymbolCode("167060019")─────│         │           │     │            │             │
 │           │                       │─Interpret(payload, Auto, symbol)─▶      │           │     │            │             │
 │           │                       │◀──NewOrderIntent(Buy, qty=1)─────│      │           │     │            │             │
 │           │                       │─ExecuteAsync(intent)──────────────────▶│           │     │            │             │
 │           │                       │                                       │─new Order  │     │            │             │
 │           │                       │                                       │─AddAsync──▶│     │            │             │
 │           │                       │                                       │─PlaceOrderAsync────▶            │             │
 │           │                       │                                       │           │     │─SendOrderAsync▶            │
 │           │                       │                                       │           │     │            │POST /sendorder│
 │           │                       │                                       │           │     │            │──────────────▶│
 │           │                       │                                       │           │     │            │◀──{OrderId}───│
 │           │                       │                                       │           │     │◀───────────│             │
 │           │                       │                                       │           │     │ OrderResult │             │
 │           │                       │                                       │           │     │ Accepted    │             │
 │           │                       │                                       │           │◀────│             │             │
 │           │                       │                                       │─MarkSubmitted(brokerOrderId)   │             │
 │           │                       │                                       │─OMS.UpsertAsync                │             │
 │           │                       │                                       │─Tracker.Track                  │             │
 │           │                       │                                       │─UpdateAsync─▶                  │             │
 │           │                       │◀───PlaceNewOrderResult────────────────│           │     │            │             │
 │           │◀─NewOrderDispatched(result)─────│ │   │    │     │     │       │           │     │            │             │
 │◀──200 OK──│                       │         │   │    │     │     │       │           │     │            │             │
```

→ 約定通知の後続は S-09。

---

## 6. S-03: Webhook 受信 → 返済 (Auto モード)

§5 と同じ手順で `SignalHandler.HandleAsync` まで進む。`Interpret` が `ExitOrderIntent` を返した場合:

```
SH       CP                              PR                          PositionMatcher    Order        KA               Kabu
 │        │                               │                            │                 │            │                 │
 │─Execute(intent)──────────────────────▶│                            │                 │            │                 │
 │        │─FindMatchingForCloseAsync───▶│                            │                 │            │                 │
 │        │  (broker,strategy,interval,  │                            │                 │            │                 │
 │        │   tradeMode, originalSide)   │                            │                 │            │                 │
 │        │◀──[Position1, Position2]─────│                            │                 │            │                 │
 │        │─BuildPlan(candidates, qty)──────────────────────────────▶                  │            │                 │
 │        │◀──ClosurePlan(Alloc[P1,2], Alloc[P2,3])────────────────────│                 │            │                 │
 │        │                                                                                          │                 │
 │        │ [Allocation 1: P1, 2 枚]                                                                  │                 │
 │        │─P1.ReserveForClose(2)                                                                    │                 │
 │        │─PR.UpdateAsync(P1)                                                                       │                 │
 │        │─new Order (ExitOrder, TargetExecutionId=P1.Id)                                          │                 │
 │        │─OR.AddAsync                                                                              │                 │
 │        │─ClosePositionAsync(req)─────────────────────────────────────────────────────────▶       │                 │
 │        │                                                                                  │─POST /sendorder────────▶│
 │        │                                                                                  │  TradeType=2            │
 │        │                                                                                  │  HoldID=P1.Id           │
 │        │                                                                                  │◀──{OrderId}─────────────│
 │        │◀──OrderResult.Accepted──────────────────────────────────────────────────────────│                         │
 │        │─Order.MarkSubmitted                                                                                       │
 │        │─OMS.UpsertAsync                                                                                            │
 │        │─Tracker.Track                                                                                              │
 │        │─OR.UpdateAsync                                                                                             │
 │        │                                                                                                            │
 │        │ [Allocation 2: P2, 3 枚]  (上記繰り返し)                                                                    │
 │        │                                                                                                            │
 │◀─ClosePositionResult(plan, [Order1, Order2], shortfall=0)──────────────────────────────────────────────────────────│
```

### 6.1 Shortfall がある場合

要求 = 20、候補残合計 = 8 → Shortfall = 12。
- 計画分 (8 枚) は発注実施
- Shortfall = 12 を warning ログに記録
- 返り値の `Shortfall` フィールドに値を入れる
- UI には StatusMessage で警告表示

### 6.2 Rejected / NetworkError の場合

```
ClosePositionAsync → OrderResult.Rejected
  │
  ├─position.ReleaseReservation(qty)  ← 解放
  ├─PR.UpdateAsync(position)
  └─order.MarkTerminated(Rejected, errorMessage)
```

```
ClosePositionAsync → OrderResult.NetworkError
  │
  ├─position.ReleaseReservation(qty) は呼ばない ← 発注通った可能性
  └─order.MarkTerminated(Rejected, "NetworkError: ...")
```

---

## 7. S-04: Webhook 受信 → ドテン

```
SH        Doten             CP                              PNO
 │         │                  │                               │
 │─Execute(dotenIntent)─────▶│                               │
 │         │                  │                               │
 │         │─ExitOrderIntent変換                              │
 │         │─ExecuteAsync(exitIntent)──▶                       │
 │         │                              [S-03 の手順]        │
 │         │◀──ClosePositionResult──────                       │
 │         │                                                   │
 │         │─NewOrderIntent変換                                 │
 │         │─ExecuteAsync(newIntent)─────────────────────────▶│
 │         │                                                   │ [S-02 の手順]
 │         │◀───PlaceNewOrderResult────────────────────────────│
 │         │                                                   │
 │◀─DotenResult(exitResult, newResult)                         │
```

⚠️ 返済の約定通知を待たずに新規発注を発火するため、一時的に両建てになる可能性がある (仕様)。

---

## 8. S-05: 拒否系シナリオ

### 8.1 自動売買 OFF

```
SH ─IsEnabled?─▶ AG ──false──▶ SH
SH ──return AutoTradeDisabled(alertName)──▶ WL
```

### 8.2 認証失敗

```
SH ─Authenticate(passphrase)?─▶ Auth ──false──▶ SH
SH ──return AuthFailed(alertName)──▶ WL
```

⚠️ HTTP レスポンスは 200 OK で `outcome=AuthFailed`。攻撃者にパスフレーズ正否を漏らさない。

### 8.3 戦略未登録 / 無効

```
SH ─IsEnabled(name,interval)?─▶ SR ──false──▶ SH
SH ──return Ignored("strategy disabled or unregistered")──▶ WL
```

### 8.4 銘柄未解決

```
SH ─ResolvedSymbolCode?─▶ AIP ──null──▶ SH
SH ──return Ignored("instrument not resolved")──▶ WL
```

---

## 9. S-06: 手動買発注

```
User      MainWindow      MVM                    PNO         OR         KA          Kabu
 │           │             │                      │           │          │            │
 │─[クリック]▶             │                      │           │          │            │
 │           │─PlaceBuyOrderCommand                                                    │
 │           │             │─ConfirmDialog?       │           │          │            │
 │◀──確認ダイアログ────────│                      │           │          │            │
 │─[OK]────────────────────▶                      │           │          │            │
 │           │             │─ExecuteAsync(intent)▶│           │          │            │
 │           │             │                      │ [S-02 と同手順]                   │
 │           │             │◀─PlaceNewOrderResult │           │          │            │
 │           │             │─StateMessage更新     │           │          │            │
 │           │             │─Orders.Add(row)      │           │          │            │
 │           │◀────────────│                      │           │          │            │
```

### 9.1 入力検証

- 銘柄 `ResolvedSymbolCode == null` → 発注ボタン無効
- 数量 `<= 0` → NumberBox バリデーション
- 指値要 / 逆指値要 のパターンも入力チェック

---

## 10. S-07: 手動返済

```
User      MainWindow      MVM                    MCP         PR         Order       KA          Kabu
 │           │             │                      │           │          │           │            │
 │─[行選択]──▶             │                      │           │          │           │            │
 │           │─SelectedPosition                                                       │            │
 │─[返済]────▶             │                      │           │          │           │            │
 │           │─ExitPositionCommand                                                    │            │
 │           │             │─ConfirmDialog?       │           │          │           │            │
 │◀──確認──────────────────│                      │           │          │           │            │
 │─[OK]────────────────────▶                      │           │          │           │            │
 │           │             │─ExecuteAsync(execId, qty=null,    │          │           │            │
 │           │             │   orderType=BestMarket)─────────▶│          │           │            │
 │           │             │                      │─FindByIdAsync(execId)│           │            │
 │           │             │                      │◀─Position │          │           │            │
 │           │             │                      │─AvailableForClose?   │           │            │
 │           │             │                      │─ReserveForClose(qty)─▶           │            │
 │           │             │                      │─UpdateAsync─▶        │           │            │
 │           │             │                      │─new Order (ExitOrder)│           │            │
 │           │             │                      │─OR.AddAsync          │           │            │
 │           │             │                      │─ClosePositionAsync────────────▶  │            │
 │           │             │                      │                                  │─POST───────▶│
 │           │             │                      │                                  │◀──{OrderId}─│
 │           │             │                      │◀──OrderResult.Accepted──────────│            │
 │           │             │                      │─MarkSubmitted / Track / Update   │            │
 │           │             │◀─ManualCloseResult───│                                  │            │
```

---

## 11. S-08: 手動キャンセル

```
User    MVM                KA              KAC         Kabu       KOP        EA
 │       │                  │               │           │          │          │
 │─[行選択]                                                                    │
 │─[キャンセル]                                                                │
 │       │─CancelOrderAsync(orderId)──▶                                        │
 │       │                  │─CancelOrderAsync▶                                │
 │       │                  │               │─PUT /cancelorder────▶            │
 │       │                  │               │◀──{ResultCode=0}────│            │
 │       │                  │◀──Accepted────│           │          │          │
 │       │◀──Accepted───────│               │           │          │          │
 │       │─StateMessage更新                                                    │
 │                                                                              │
 │ [次のポーリングサイクル]                                                      │
 │                                            │           │─PollOnce            │
 │                                            │           │  /orders?id={id}    │
 │                                            │           │  → State=5 検出     │
 │                                            │           │─push Snapshot───────▶
 │                                                                       │
 │                                                                       │ApplyTerminationAsync
 │                                                                       │─Find Order
 │                                                                       │─MarkTerminated(Cancelled)
 │                                                                       │─(返済注文なら) Position.ReleaseReservation
```

---

## 12. S-09: 新規約定通知

```
Kabu      KOP                          KA            ExecStream     ESS         EA               OR    PR        APMS
 │         │                            │              │              │           │                │     │         │
 │ (1秒経過)                            │              │              │           │                │     │         │
 │         │─PollOnce                   │              │              │           │                │     │         │
 │         │  for each pending OrderId  │              │              │           │                │     │         │
 │         │  GET /orders?id={id}       │              │              │           │                │     │         │
 │         │  Details[].RecType=8 検出  │              │              │           │                │     │         │
 │         │─BuildExecutionEvent        │              │              │           │                │     │         │
 │         │─KA.PushExecution(ev)──────▶│              │              │           │                │     │         │
 │         │                            │─OnNext(ev)──▶│              │           │                │     │         │
 │         │                            │              │─Subscribe────▶           │                │     │         │
 │         │                            │              │              │─DI Scope                   │     │         │
 │         │                            │              │              │─ApplyAsync(ev)─▶            │     │         │
 │         │                            │              │              │           │─FindByBrokerOrderId▶│     │         │
 │         │                            │              │              │           │◀─Order        │     │         │
 │         │                            │              │              │           │─order.ApplyExecution│     │         │
 │         │                            │              │              │           │  (OrderExecutedEvent 発火)
 │         │                            │              │              │           │─OR.UpdateAsync─▶ │     │         │
 │         │                            │              │              │           │─order.IsTerminal?│     │         │
 │         │                            │              │              │           │─Tracker.Untrack │     │         │
 │         │                            │              │              │           │                  │     │         │
 │         │                            │              │              │           │ [TradeType=NewOrder]   │         │
 │         │                            │              │              │           │─new Position    │     │         │
 │         │                            │              │              │           │─PR.AddAsync─────▶     │         │
 │         │                            │              │              │           │   (PositionOpenedEvent 発火)
 │         │                            │              │              │           │─(Auto なら) APMS.UpsertAsync─▶ │
 │
                                       (UI 側)
                                       MVM ◀─OnPositionChanged─ PR.Changed
                                          Positions.Add(row)
```

---

## 13. S-10: 返済約定通知

```
KOP                          KA            ExecStream    ESS         EA                  PR        OR        APMS
 │                            │              │             │           │                   │         │         │
 │─PollOnce                   │              │             │           │                   │         │         │
 │  Details[].RecType=8 検出 (返済約定)       │             │           │                   │         │         │
 │─BuildExecutionEvent        │              │             │           │                   │         │         │
 │  TargetPositionId = HoldID をメタから解決                                                              │         │
 │─KA.PushExecution(ev)──────▶│              │             │           │                   │         │         │
 │                            │─OnNext─────▶ │             │           │                   │         │         │
 │                                            │─ApplyAsync─▶           │                   │         │         │
 │                                                          │─FindByBrokerOrderId─────────▶OR        │         │
 │                                                          │◀─Order  │                   │         │         │
 │                                                          │─order.ApplyExecution         │         │         │
 │                                                          │─OR.UpdateAsync               │         │         │
 │                                                          │ [TradeType=ExitOrder]         │         │         │
 │                                                          │─PR.FindByIdAsync(TargetPositionId)
 │                                                          │◀─Position                    │         │         │
 │                                                          │─position.ApplyClosure(qty)   │         │         │
 │                                                          │   (PositionUpdatedEvent または│         │         │
 │                                                          │    PositionClosedEvent 発火) │         │         │
 │                                                          │─IsClosed?                    │         │         │
 │                                                          │  YES: PR.RemoveAsync─────────▶         │         │
 │                                                          │       APMS.RemoveAsync─────────────────▶         │
 │                                                          │  NO:  PR.UpdateAsync                  │         │
```

---

## 14. S-11: 注文取消検出 (ポーリング)

ユーザーが kabu Station 側で直接取消した場合の検出:

```
Kabu (ユーザー手動 or タイムアウト)
 │
 │ /orders State=5, RecType=5/7
 │
KOP─PollOnce
 │ ─GET /orders?id={id}
 │ ─State=5 (終了) を検出
 │ ─Snapshot に該当 + state=Cancelled に変換
 │ ─IOrderSnapshotNotifier.SnapshotsUpdated(snapshots) 発火
 │
OrderTerminationSubscriberService
 │ ─SnapshotsUpdated 購読
 │ ─snapshots.Where(IsTerminal)
 │ ─EA.ApplyTerminationAsync(orderId, reason)─▶
                                        EA
                                         │─FindByBrokerOrderId
                                         │─order.IsTerminal? → if not, MarkTerminated(Cancelled)
                                         │ [ExitOrder で TargetExecutionId あれば]
                                         │─PR.FindById → position.ReleaseReservation(unfilled)
                                         │─PR.UpdateAsync
```

---

## 15. S-12: 価格ティック受信 (WebSocket)

```
Kabu          KBWS             KA                  PriceStream    MVM
 │             │                │                    │              │
 │ (WS push)──▶│                │                    │              │
 │ JSON        │─Parse          │                    │              │
 │             │─PriceTick生成   │                    │              │
 │             │─KA.PushPriceTick(tick)─▶            │              │
 │             │                │─OnNext─▶           │              │
 │             │                │                    │─Subscribe──▶ │
 │             │                │                    │              │─OnPriceUpdated
 │             │                │                    │              │  該当 InstrumentDefinition の
 │             │                │                    │              │  LastPrice/BidPrice/AskPrice 更新
 │             │                │                    │              │  (ManualOrderInstrument 該当なら
 │             │                │                    │              │   MVM のプロパティも同期)
 │             │                │                    │              │
 │             │─PriceUpdated event 発火 (Rx 非依存)                  │
 │             │     ──────────▶ (購読者なし、将来用)                  │
```

### 15.1 WebSocket 接続ロス時

```
KBWS.ConnectionLoopAsync:
  │
  │ try {
  │   ClientWebSocket.ConnectAsync(uri)
  │   await ReceiveLoopAsync(ws, ct)
  │ } catch (Exception ex) {
  │   LogWarning("WS disconnected: ...")
  │   await Task.Delay(5000, ct)
  │   (ループ継続 → 再接続)
  │ }
```

---

## 16. S-13: アプリ終了

```
User       MainWindow      App.xaml.cs        DI Host          HostedServices
 │            │                 │                  │                   │
 │─[×ボタン]──▶                 │                  │                   │
 │            │─OnClosing─────▶ │                  │                   │
 │            │                 │─UILayoutStore.Save                   │
 │            │                 │─_host.StopAsync(5s timeout)─▶        │
 │            │                 │                  │─StopAsync (逆順)──▶
 │            │                 │                  │                   │HttpWebhookListener.StopAsync
 │            │                 │                  │                   │KabuBoardWebSocketService.StopAsync
 │            │                 │                  │                   │KabuOrderPollingService.StopAsync
 │            │                 │                  │                   │OrderTerminationSubscriber.StopAsync
 │            │                 │                  │                   │ExecutionStreamSubscriber.StopAsync
 │            │                 │                  │                   │BrokerSessionInitializer.StopAsync
 │            │                 │                  │◀──OK──────────────│
 │            │                 │─Serilog Flush/Close                  │
 │            │                 │─Application.Shutdown                 │
```

---

## 17. エラーシーケンス: kabu 401 自動 Refresh

```
KAC.SendOrderAsync(req)
 │
 │─KTS.GetTokenAsync (キャッシュ token)
 │─POST /sendorder/future (X-API-KEY: token)
 │  ← 401 Unauthorized
 │
 │─KTS.RefreshAsync (強制再取得)
 │  ├─POST /token
 │  └─新 token 返却 + キャッシュ更新
 │
 │─POST /sendorder/future (新 token で再試行)
 │  ← 200 OK
 │
 │─Response 返却
```

---

## 18. エラーシーケンス: 起動時 kabu 未接続

```
BrokerSessionInitializerService.StartAsync
 │
 ├─Step 1 (GetPositions): 例外
 │   catch → LogWarning("kabu warm-up failed: ...")
 │   continue
 │
 ├─Step 2 (InitialFetchOrders): 例外
 │   catch → LogWarning("orders fetch failed: ...")
 │   continue
 │
 └─Step 3 (Reconciliation): 例外
     catch → LogWarning("position sync failed: ...")
     continue
 
 → 例外は伝播せず、StartAsync は正常完了
 → UI は起動する (kabu 未接続表示)
 → ユーザーが kabu Station を起動すれば次回ポーリングで自動復旧
```

---

## 19. シーケンス図のメンテナンス

新しいシナリオを追加するときは:

1. §3 の一覧に S-NN を追加
2. 該当 §セクションを追加
3. 関連する `state-machines.md` / `class-design.md` のリンクを張る
4. クラス変更 (依存追加・メソッド名変更) で図がズレたら本書を更新

---

## 変更履歴

| バージョン | 日付 | 変更内容 |
|---|---|---|
| 1.0.0 | 2026-05-26 | 初版作成 (13 シナリオ + エラー系 2 件) |
