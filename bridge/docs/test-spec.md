# テスト仕様 (Test Specification)

**バージョン**: 1.0.0
**作成日**: 2026-05-26
**最終更新**: 2026-05-26
**対象**: `tests/` 配下全テストプロジェクト

---

## 1. このドキュメントの目的

本ブリッジのテスト戦略・カバレッジ範囲・主要シナリオを明文化する。
新規機能追加時に必要なテストの粒度判定 / 回帰検出 / 退行修正の根拠とする。

関連:
- `../../docs/test_policy.md` — 全プロジェクト共通のテスト方針
- [class-design.md](./class-design.md) — テスト対象クラス

---

## 2. テスト戦略

### 2.1 ピラミッド構成

```
                       ┌──────────────────┐
                       │  E2E (手動 検証)  │   ← Webhook→kabu 実発注確認
                       │  少数             │
                       └──────────────────┘
                  ┌────────────────────────────┐
                  │  統合テスト (Infrastructure) │   ← kabu Mapper, パーサ等
                  │  中規模                      │
                  └────────────────────────────┘
            ┌──────────────────────────────────────┐
            │  ユニットテスト (Domain / Application) │  ← 大多数
            │  176+ 件                              │
            └──────────────────────────────────────┘
```

### 2.2 フレームワーク

| ツール | バージョン | 用途 |
|---|---|---|
| xUnit | 2.x | テストランナー / Theory / Fact |
| FluentAssertions | (推奨) | アサーション (現状は組込 Assert で十分) |
| Moq / NSubstitute | (未使用) | モック (本プロジェクトは手書き TestDouble を採用) |

### 2.3 命名規約

- テストクラス: `{TargetClass}Tests` (例: `OrderTests`)
- テストメソッド: `{Method}_{Scenario}_{Expectation}` (英語推奨)
  - 例: `ApplyExecution_WithFullFill_TransitionsToFilled`
  - 日本語混在 OK (本プロジェクトでは可読性優先)

### 2.4 テスト実行

```powershell
# 全テスト
dotnet test

# 特定プロジェクト
dotnet test tests/N225BrokerBridge.Domain.Tests

# カバレッジ収集
dotnet test --collect:"XPlat Code Coverage"
```

---

## 3. テストプロジェクト構成

```
tests/
├── N225BrokerBridge.Domain.Tests/
│   ├── Orders/OrderTests.cs
│   ├── Positions/PositionMatcherTests.cs
│   ├── Positions/PositionTests.cs
│   └── ValueObjects/
│       ├── BrokerCodeTests.cs
│       ├── ExecutionIdTests.cs
│       ├── OrderIdTests.cs
│       ├── PriceTests.cs
│       ├── QuantityTests.cs
│       ├── SideTests.cs
│       └── SymbolCodeTests.cs
├── N225BrokerBridge.Application.Tests/
│   ├── Orders/
│   │   ├── ExecutionApplierTests.cs
│   │   └── PlaceNewOrderUseCaseTests.cs
│   ├── Positions/
│   │   ├── ClosePositionUseCaseTests.cs
│   │   └── DotenUseCaseTests.cs
│   ├── Signals/
│   │   ├── SignalHandlerTests.cs
│   │   └── SignalInterpreterTests.cs
│   └── TestDoubles/
│       ├── FakeBrokerAdapter.cs
│       ├── FixedDateTimeProvider.cs
│       ├── StubAutoPositionMetadataStore.cs
│       ├── StubOrderMetadataStore.cs
│       ├── StubPendingOrderTracker.cs
│       └── StubStrategyRegistry.cs
└── N225BrokerBridge.Infrastructure.Tests/
    ├── Brokers/Kabu/
    │   ├── KabuMappersTests.cs
    │   └── SendOrderBodySimulation.cs
    └── Webhooks/
        └── SignalPayloadParserTests.cs
```

---

## 4. Domain.Tests

### 4.1 OrderTests

対象: `Domain/Orders/Order.cs`

主要シナリオ:

| # | テスト | 検証内容 |
|---|---|---|
| O-01 | コンストラクタ初期状態 | State=Created, CumQty=0, RemainingQty=RequestedQty |
| O-02 | ExitOrder で TargetExecutionId 必須 | null なら例外 |
| O-03 | NewOrder で TargetExecutionId 無し | OK |
| O-04 | Auto モードは Interval > 0 | Interval==0 で例外 |
| O-05 | Manual モードは Interval == 0 | Interval > 0 で例外 |
| O-06 | MarkSubmitted で State 遷移 | Created → Submitted |
| O-07 | MarkSubmitted 後の再 MarkSubmitted | 例外 (Created 以外不可) |
| O-08 | ApplyExecution の部分約定 | State=PartiallyFilled, OrderExecutedEvent(IsFullyFilled=false) |
| O-09 | ApplyExecution の全量約定 | State=Filled, IsFullyFilled=true |
| O-10 | RemainingQty を超える ApplyExecution | DomainException |
| O-11 | Filled 状態への再操作 | InvalidOperationException |
| O-12 | MarkTerminated(Cancelled) | State=Cancelled, OrderTerminatedEvent |
| O-13 | terminal 状態への MarkTerminated | 例外 |
| O-14 | StopPrice の制約 | OrderType=Stop のみ > 0 |
| O-15 | RaiseEvent 後の DomainEvents 参照 | リストに反映、ClearDomainEvents で空 |

### 4.2 PositionTests

対象: `Domain/Positions/Position.cs`

| # | テスト | 検証内容 |
|---|---|---|
| P-01 | コンストラクタ | LeaveQty=N, HoldQty=0, PositionOpenedEvent 発火 |
| P-02 | ReserveForClose 正常 | HoldQty += qty, PositionUpdatedEvent |
| P-03 | ReserveForClose で AvailableForClose 超過 | DomainException |
| P-04 | ApplyClosure 部分 | LeaveQty -= qty, HoldQty -= qty, PositionUpdatedEvent |
| P-05 | ApplyClosure 全量 | LeaveQty=0, HoldQty=0, PositionClosedEvent |
| P-06 | ApplyClosure で HoldQty 超過 | DomainException |
| P-07 | ReleaseReservation 正常 | HoldQty -= qty, PositionUpdatedEvent |
| P-08 | ReleaseReservation で HoldQty 超過 | DomainException |
| P-09 | closed 状態 (LeaveQty=0) への操作 | DomainException |
| P-10 | AvailableForClose 計算 | LeaveQty - HoldQty |
| P-11 | IsClosed 判定 | LeaveQty==0 で true |
| P-12 | TradeMode × Interval の制約 | Auto/Manual と Interval の組合せ |

### 4.3 PositionMatcherTests

対象: `Domain/Positions/PositionMatcher.cs`

| # | テスト | 検証内容 |
|---|---|---|
| PM-01 | 単一建玉 全量消化 | Alloc 1 件, Shortfall=0 |
| PM-02 | 複数建玉 跨ぎ消化 | Alloc 2+ 件, FIFO 順, Shortfall=0 |
| PM-03 | 要求 > 残合計 | 残合計まで消化, Shortfall = 不足分 |
| PM-04 | 候補なし | Alloc 空, Shortfall = 要求全量 |
| PM-05 | 要求 0 | Alloc 空, Shortfall = 0 |
| PM-06 | AvailableForClose = 0 の建玉混在 | skip して次へ |
| PM-07 | ExecutionId 順の決定性 | 同じ入力なら毎回同じ Alloc 順 |

### 4.4 ValueObjects Tests

| 対象 | 検証 |
|---|---|
| OrderIdTests | 空文字で例外 / Equals は Value 比較 |
| ExecutionIdTests | 同上 |
| PriceTests | 負値で例外 / Zero 静的 / 演算子 (+, -, <) / 比較 |
| QuantityTests | 負値で例外 / Min 静的 / 演算子 |
| SideTests | Buy/Sell, Opposite, ToKabuCode (1↔2 逆順), ToDisplay |
| BrokerCodeTests | 空で例外 / Kabu/Rakuten 定数 |
| SymbolCodeTests | 空で例外 |

---

## 5. Application.Tests

### 5.1 TestDoubles (テスト用ダブル)

#### FakeBrokerAdapter

- IBrokerAdapter 実装
- 呼出履歴を ConcurrentQueue で記録 (`PlacedOrders`, `ClosedPositions`, `CancelledOrders`)
- 応答スタブ設定 (`NextPlaceResult` 等で `Accepted`/`Rejected`/`NetworkError` を切替)
- ExecutionStream / PriceStream は Subject<T> で内部 push

#### FixedDateTimeProvider

```csharp
public sealed class FixedDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow { get; set; }
}
```

時刻固定でテスト。`provider.UtcNow = new DateTime(2026, 5, 26, 0, 0, 0, DateTimeKind.Utc);` で操作。

#### Stub{各 Port}

- `StubAutoPositionMetadataStore` — In-memory 実装
- `StubOrderMetadataStore` — In-memory 実装
- `StubPendingOrderTracker` — In-memory 実装
- `StubStrategyRegistry` — In-memory 実装

### 5.2 SignalHandlerTests

| # | テスト | 検証内容 |
|---|---|---|
| SH-01 | 自動売買 OFF | AutoTradeDisabled 返却、UseCase 呼ばれず |
| SH-02 | 認証失敗 | AuthFailed 返却、UseCase 呼ばれず |
| SH-03 | 戦略未登録 | Ignored 返却 |
| SH-04 | 戦略無効 | Ignored 返却 |
| SH-05 | 銘柄未解決 | Ignored 返却 |
| SH-06 | NewOrderIntent | PNO 呼出 → NewOrderDispatched |
| SH-07 | ExitOrderIntent | CP 呼出 → ExitOrderDispatched |
| SH-08 | DotenIntent | Doten 呼出 → DotenDispatched |
| SH-09 | Interpreter 例外 | Interpretation_Failed |
| SH-10 | MarkSignalReceived 呼出 | 戦略の LastSignalAt 更新 |

### 5.3 SignalInterpreterTests

§9 (state-machines.md) の 13 行マトリックス全網羅:

| # | prev | curr | action | 期待 |
|---|---|---|---|---|
| SI-01 | flat | long | buy | NewOrderIntent(Buy) |
| SI-02 | flat | short | sell | NewOrderIntent(Sell) |
| SI-03 | long | flat | sell | ExitOrderIntent(Buy, 全量) |
| SI-04 | short | flat | buy | ExitOrderIntent(Sell, 全量) |
| SI-05 | long | long | sell | ExitOrderIntent(部分) |
| SI-06 | short | short | buy | ExitOrderIntent(部分) |
| SI-07 | short | long | buy | DotenIntent |
| SI-08 | long | short | sell | DotenIntent |
| SI-09 | order_price < 0 | - | - | 0 に正規化 |
| SI-10 | order_contracts == 0 | - | - | 例外 |
| SI-11 | market_position_size == 0 で long | - | - | 例外 |
| SI-12 | symbol が payload.ticker ではなく引数 | - | - | 引数を採用 |

### 5.4 PlaceNewOrderUseCaseTests

| # | テスト | 検証内容 |
|---|---|---|
| PNO-01 | Accepted | Order State=Submitted, OMS Upsert, Tracker Track |
| PNO-02 | Rejected | Order State=Rejected, Tracker 未 Track |
| PNO-03 | NetworkError | Order State=Rejected(reason="NetworkError..."), Tracker 未 Track |
| PNO-04 | 集約に PlaceOrderAsync の戻り値が反映 | BrokerOrderId 設定 |
| PNO-05 | 例外発生時 | Order が Repository に Add されたまま (terminal Reject) |

### 5.5 ClosePositionUseCaseTests

| # | テスト | 検証内容 |
|---|---|---|
| CP-01 | 候補なし | Plan.Allocations 空, Order 発注なし, Shortfall=requested |
| CP-02 | 単一建玉 全量消化 Accepted | Order 1 件, Position.HoldQty 設定 |
| CP-03 | 単一建玉 部分消化 Accepted | Order 1 件 (qty=部分), Position 拘束 |
| CP-04 | 複数建玉 跨ぎ消化 | Order 複数件, 各 Position 拘束 |
| CP-05 | Allocated Rejected | ReleaseReservation 呼出、Order=Rejected |
| CP-06 | Allocated NetworkError | ReleaseReservation 呼ばれない (拘束保持) |
| CP-07 | Shortfall > 0 | 計画分発注 + 警告ログ |
| CP-08 | 全 Allocation が Rejected | 全部解放、Order 全て Rejected |

### 5.6 DotenUseCaseTests

| # | テスト | 検証内容 |
|---|---|---|
| D-01 | Short → Long | CP 実行 → PNO 実行、両方の Result 返却 |
| D-02 | Long → Short | 同上 |
| D-03 | CP が候補なし | CP の Shortfall=requested、PNO は通常実行 |
| D-04 | CP Accepted, PNO Rejected | 返済成功・新規失敗のケース |

### 5.7 ExecutionApplierTests

| # | テスト | 検証内容 |
|---|---|---|
| EA-01 | NewOrder 約定 (全量) | Order=Filled, Position 新規, Tracker Untrack, Auto なら APMS Upsert |
| EA-02 | NewOrder 約定 (部分) | Order=PartiallyFilled, Position 新規 (この約定分のみ) |
| EA-03 | ExitOrder 約定 (部分) | Order=PartiallyFilled, 対象 Position.ApplyClosure(qty) |
| EA-04 | ExitOrder 約定 (全量) | Order=Filled, 対象 Position が IsClosed → Repository Remove, APMS Remove |
| EA-05 | 既知の ExecutionId 重複 | スキップ (重複適用しない) — 実装依存 |
| EA-06 | ApplyTerminationAsync (Cancelled) | Order.MarkTerminated(Cancelled), 残数 ExitOrder なら Position.ReleaseReservation |
| EA-07 | ApplyTerminationAsync (Filled 後) | 冪等: 何もせず |

---

## 6. Infrastructure.Tests

### 6.1 KabuMappersTests

対象: `Infrastructure/Brokers/Kabu/KabuMappers.cs`

| # | テスト | 検証内容 |
|---|---|---|
| KM-01 | NewOrder Market → kabu DTO | FrontOrderType=120, TimeInForce 変換 |
| KM-02 | NewOrder Limit → kabu DTO | FrontOrderType=20, LimitPrice |
| KM-03 | NewOrder Stop → kabu DTO | FrontOrderType=30, ReverseLimitOrder block 入る |
| KM-04 | NewOrder BestMarket → kabu DTO | FrontOrderType=20, TimeInForce=FAK |
| KM-05 | ExitOrder → kabu DTO | TradeType=2, HoldID 同梱, Side が反対 |
| KM-06 | TimeInForce マッピング | FAS=1, FAK=2, FOK=3 |
| KM-07 | Side マッピング | Buy(1) → kabu 2, Sell(2) → kabu 1 (逆順) |
| KM-08 | KabuSendOrderResponse → OrderResult | Code=0 → Accepted, Code≠0 → Rejected |
| KM-09 | KabuOrderDto → OrderSnapshot 加重平均価格 | Details の Filled 配列から重み付き平均 |
| KM-10 | KabuPositionDto → PositionSnapshot | Side "1" → Sell, "2" → Buy (逆順) |
| KM-11 | KabuBoardDto → QuoteSnapshot | Bid/Ask フィールドそのまま (kabu の逆命名は呼出側責任) |

### 6.2 SendOrderBodySimulation

実際に kabu に送る Request Body のスナップショット保存。退行検出 (kabu 仕様変更を素早く検知)。

| # | サンプル | 内容 |
|---|---|---|
| Sim-01 | 成行買 (Mini, Auto, 1 枚) | Body 全体スナップショット |
| Sim-02 | 指値売 (Mini, Auto, 1 枚, 38500) | 同上 |
| Sim-03 | 逆指値買 (Mini, Manual, 1 枚, Limit=38000, Stop=38100) | 同上 |
| Sim-04 | 返済 (Mini, Auto, HoldID=EXEC-XXX) | TradeType=2, ClosePositions[].HoldID |

### 6.3 SignalPayloadParserTests

対象: `Infrastructure/Webhooks/SignalPayloadParser.cs`

| # | テスト | 検証内容 |
|---|---|---|
| SP-01 | 正常 JSON (新規買) | SignalPayload 各フィールド正しい |
| SP-02 | 正常 JSON (返済) | 同上 |
| SP-03 | passphrase 省略 | OK (null として受領) |
| SP-04 | alert_name 省略 | WebhookParseException |
| SP-05 | interval=0 | 例外 |
| SP-06 | interval=-5 | 例外 |
| SP-07 | order_action 不明値 | 例外 |
| SP-08 | order_price 負値 | 0 に正規化 |
| SP-09 | order_contracts double → int | 1.0 → 1, 1.5 → 2 (AwayFromZero) |
| SP-10 | 大文字小文字混在キー | 受け入れ (PropertyNameCaseInsensitive) |
| SP-11 | 余分なフィールド | 無視 |
| SP-12 | JSON 構文エラー | WebhookParseException |

---

## 7. UI 層のテスト方針

現状: UI 層 (XAML + ViewModels) の自動テストはなし。

理由:
- WPF のテストは Coded UI 等で重い
- 手動操作確認で十分カバーできる規模

代替手段:
- ViewModel の Command 内ロジックは Application 層の UseCase 呼出に薄く委譲しているため、UseCase テストでカバー
- UI 単独確認は `--demo` モードで実施

将来案: NSubstitute + xunit で MainViewModel.PropertyChanged 検証。

---

## 8. E2E テスト (手動)

### 8.1 Webhook → kabu 経路の検証

| Stage | 目的 | 確認方法 |
|---|---|---|
| 1 | ローカル直接 POST | curl で `http://localhost:8001/webhook` に投げ、UI で発注を確認 |
| 2 | Cloudflare Tunnel 経由 | curl で `https://your-domain.com/webhook` に投げ、UI で発注を確認 |
| 3 | TradingView 実発火 | TV 戦略のアラートを発火、UI で発注を確認 (実トレード) |

### 8.2 検証用ペイロード

`webhook-api-spec.md` §3.4 のサンプル参照。

### 8.3 検証時の安全策

| 対策 | 詳細 |
|---|---|
| 検証環境を使う | kabu Verification ポート (18081) に切替えて実弾を避ける |
| 自動売買トグル OFF | 暴発時に止められる状態を維持 |
| 小ロット | Micro (約 1/10 ロット) で検証 |
| デモモード | UI 確認のみなら `--demo` |

---

## 9. テストカバレッジ目標

| 層 | 目標 | 現状目安 |
|---|---|---|
| Domain | 90%+ | ~95% (集約・VO ともに充実) |
| Application | 80%+ | ~85% (UseCase 主要パス網羅) |
| Infrastructure | 60%+ | ~50% (kabu Mapper + Parser、kabu 接続部は手動) |
| UI | (測定対象外) | - |

---

## 10. 退行防止: 過去事故とテスト追加

| 過去事故 | 追加されたテスト |
|---|---|
| 部分返済での予約枠解放漏れ | EA-06 (ApplyTerminationAsync で ExitOrder の ReleaseReservation) |
| Side の kabu 値逆順 | KM-07 |
| kabu BID/ASK 逆命名 | KM-11 |
| ExitOrder で TargetExecutionId 必須 | O-02 |
| Auto/Manual と Interval の整合 | O-04, O-05, P-12 |

---

## 11. テスト実行ベストプラクティス

| 状況 | 推奨 |
|---|---|
| ロカール開発 | `dotnet test` 全実行 (~1 秒) |
| CI (将来) | xUnit reporter + coverage 集計 |
| 重大変更前 | カバレッジレポート確認 |
| バグ修正後 | 再現するテストを先に書いてから修正 (TDD) |

---

## 12. 未テスト領域 / 将来課題

| 領域 | 理由 | 優先度 |
|---|---|---|
| KabuApiClient (HTTP モック) | HttpClient のモックが面倒 | 中 |
| KabuOrderPollingService の重複ガード | ConcurrentDictionary の競合シナリオ | 低 |
| KabuBoardWebSocketService の再接続 | 状態遷移の網羅 | 低 |
| LocalSettingsStore DPAPI 暗号化往復 | テスト環境で DPAPI 使用 | 中 |
| MainViewModel の状態管理 | UI 統合テスト導入時に | 低 |

---

## 13. 変更履歴

| バージョン | 日付 | 変更内容 |
|---|---|---|
| 1.0.0 | 2026-05-26 | 初版作成 |
