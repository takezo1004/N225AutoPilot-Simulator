# アーキテクチャ概要

## このドキュメントの目的

N225BrokerBridge のソフトウェアアーキテクチャ全体像と技術選択の根拠を示す。

関連ドキュメント:
- [ユビキタス言語辞書](./ubiquitous-language.md) — ドメイン用語
- [コンテキストマップ](./context-map.md) — 境界づけられたコンテキスト

---

## 1. アーキテクチャ全体像

### 1.1 レイヤー構造 (DDD 4 層 + クリーンアーキテクチャ)

```
┌─────────────────────────────────────────────────────────┐
│                  UI 層 (Presentation)                    │
│           WPF + WPF UI ライブラリ + MVVM                 │
│           N225BrokerBridge.UI                           │
└──────────────────────────┬──────────────────────────────┘
                           │ (依存)
                           ▼
┌─────────────────────────────────────────────────────────┐
│            アプリケーション層 (Application)              │
│      ユースケース・アプリケーションサービス               │
│      N225BrokerBridge.Application                        │
└──────────────────────────┬──────────────────────────────┘
                           │ (依存)
                           ▼
┌─────────────────────────────────────────────────────────┐
│                  ドメイン層 (Domain) ★中核              │
│      集約・値オブジェクト・ドメインイベント・             │
│      ドメインサービス・リポジトリ抽象                    │
│      N225BrokerBridge.Domain                             │
└─────────────────────────────────────────────────────────┘
                           ▲ (依存逆転 / 実装)
                           │
┌─────────────────────────────────────────────────────────┐
│           インフラストラクチャ層 (Infrastructure)         │
│      ブローカーアダプタ・リポジトリ実装・                 │
│      Webhook 受信・永続化・ロガー                        │
│      N225BrokerBridge.Infrastructure                     │
└─────────────────────────────────────────────────────────┘
```

### 1.2 依存方向の原則

- **ドメイン層は何にも依存しない** (純粋なビジネスロジック)
- **アプリケーション層は Domain にのみ依存**
- **インフラ層は Domain のインターフェースを実装** (依存性逆転の原則 / DIP)
- **UI 層は Application を通じて Domain を利用**

これにより:
- ドメインモデルの変更が外側 (UI / DB / API) に影響されない
- ブローカーや UI を差し替えてもドメインが揺れない
- テストが書きやすい (Domain 層はインフラなしで単体テスト可能)

---

## 2. プロジェクト構成

```
N225BrokerBridge/
├── src/
│   ├── N225BrokerBridge.Domain/
│   │   ├── Common/                  # 値オブジェクト基底、ID 型
│   │   ├── Signals/                 # シグナル集約
│   │   │   ├── Signal.cs
│   │   │   ├── SignalReceived.cs   # ドメインイベント
│   │   │   └── ISignalRepository.cs
│   │   ├── Orders/                  # 注文集約
│   │   │   ├── Order.cs            # 集約ルート
│   │   │   ├── OrderId.cs          # 値オブジェクト
│   │   │   ├── OrderState.cs       # 状態
│   │   │   ├── OrderSubmitted.cs   # ドメインイベント
│   │   │   ├── OrderExecuted.cs
│   │   │   └── IOrderRepository.cs
│   │   ├── Positions/               # 建玉集約
│   │   │   ├── Position.cs         # 集約ルート
│   │   │   ├── Lot.cs              # 子エンティティ
│   │   │   ├── PositionMatcher.cs  # ドメインサービス
│   │   │   └── IPositionRepository.cs
│   │   ├── Brokers/                 # ブローカー抽象
│   │   │   ├── IBrokerAdapter.cs   # 統一インターフェース
│   │   │   ├── BrokerCode.cs       # ブローカー識別
│   │   │   └── ExecutionEvent.cs
│   │   ├── Accounts/                # 口座
│   │   │   ├── BrokerAccount.cs
│   │   │   └── AccountSnapshot.cs
│   │   └── ValueObjects/            # 共通値オブジェクト
│   │       ├── Price.cs
│   │       ├── Quantity.cs
│   │       ├── SymbolCode.cs
│   │       └── Side.cs
│   │
│   ├── N225BrokerBridge.Application/
│   │   ├── Signals/
│   │   │   └── SignalHandler.cs    # アプリケーションサービス
│   │   ├── Orders/
│   │   │   ├── PlaceOrderUseCase.cs
│   │   │   └── ClosePositionUseCase.cs
│   │   ├── Positions/
│   │   │   └── ReconcilePositionsUseCase.cs
│   │   └── Common/
│   │       └── ICommandBus.cs
│   │
│   ├── N225BrokerBridge.Infrastructure/
│   │   ├── Brokers/
│   │   │   ├── Kabu/
│   │   │   │   ├── KabuAdapter.cs
│   │   │   │   ├── KabuApiClient.cs
│   │   │   │   ├── KabuAuthService.cs
│   │   │   │   └── KabuModels/    # API DTO
│   │   │   └── Rakuten/
│   │   │       ├── RakutenAdapter.cs
│   │   │       └── RakutenRtdClient.cs
│   │   ├── Webhooks/
│   │   │   ├── TcpWebhookServer.cs # localhost listener
│   │   │   └── WebhookPayloadParser.cs
│   │   ├── Persistence/
│   │   │   ├── InMemoryOrderRepository.cs
│   │   │   └── InMemoryPositionRepository.cs
│   │   ├── Auditing/
│   │   │   └── SerilogConfiguration.cs
│   │   └── DI/
│   │       └── InfrastructureModule.cs
│   │
│   └── N225BrokerBridge.UI/
│       ├── App.xaml
│       ├── MainWindow.xaml
│       ├── Views/
│       ├── ViewModels/
│       ├── Controls/
│       └── DI/
│           └── UIModule.cs
│
├── tests/
│   ├── N225BrokerBridge.Domain.Tests/        # Domain 単体テスト
│   ├── N225BrokerBridge.Application.Tests/   # ユースケース単体テスト
│   └── N225BrokerBridge.Infrastructure.Tests/ # アダプタ統合テスト
│
├── docs/
│   ├── ubiquitous-language.md
│   ├── context-map.md
│   ├── architecture.md       # 本ドキュメント
│   └── adapters/
│       ├── kabu.md
│       └── rakuten.md
│
└── N225BrokerBridge.sln
```

---

## 3. 主要な設計判断

### 3.1 ブローカー抽象 (Broker Adapter パターン)

**問題**: 各証券会社の API は (REST / WebSocket / COM / TCP / FIX 等) 通信方式から
注文型 (HoldID 体系・約定通知形式・エラーコード) まで全く異なる。

**解決**: ドメイン層に **統一インターフェース** `IBrokerAdapter` を定義し、各 adapter は
このインターフェースを実装する。ドメイン層は具象 adapter を知らない (依存性逆転)。

```csharp
// Domain/Brokers/IBrokerAdapter.cs (抜粋・概念図)
public interface IBrokerAdapter
{
    BrokerCode BrokerCode { get; }
    bool IsConnected { get; }

    Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct);
    Task<OrderResult> ClosePositionAsync(ClosePositionRequest request, CancellationToken ct);
    Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(CancellationToken ct);
    Task<IReadOnlyList<OrderSnapshot>> GetOrdersAsync(CancellationToken ct);
    Task<QuoteSnapshot> GetQuoteAsync(SymbolCode symbol, CancellationToken ct);

    IObservable<ExecutionEvent> ExecutionStream { get; }  // 約定通知ストリーム
    IObservable<PriceTick> PriceStream { get; }           // 価格ティックストリーム
}
```

### 3.2 ドメインイベント駆動

各コンテキストは**イベント発火と購読**で疎結合に連携する。
直接的なメソッド呼び出しではなく、`SignalReceived` → `OrderSubmitted` → `OrderExecuted` → `PositionUpdated`
という流れでイベントが伝播する。

**実装**: `MediatR` (.NET の OSS メッセージング) または自前の `IEventBus`。
本プロジェクトでは初期段階では自前のシンプルな pub/sub を採用、必要に応じて MediatR 導入を検討。

### 3.3 集約境界

| 集約 | ルート | 子要素 | 不変条件 |
|------|--------|--------|---------|
| Order | `Order` | `OrderLine`, `Execution[]` | 注文枚数 = 約定累計 + 残未約定 |
| Position | `Position` | `Lot[]` | LeaveQty = Σ Lot.LeaveQty ≥ 0 |
| Signal | `Signal` | (なし) | Passphrase 認証済み |
| BrokerAccount | `BrokerAccount` | `Balance`, `Margin` | 拘束金額 ≤ 利用可能証拠金 |

**1 集約 = 1 トランザクション** を原則とする。集約をまたぐ整合性はイベント結果整合性 (Eventual Consistency) で扱う。

### 3.4 非同期・並行性

- 約定通知やリアルタイム価格は **`IObservable<T>` (Rx.NET)** で表現
- HTTP/REST 呼び出しは `async/await`
- WPF UI 更新は `Dispatcher` 経由 (MVVM ViewModel のプロパティ変更通知)
- 並行アクセスする集約 (Position) は **不変オブジェクト + 状態置換** または `lock` で保護

### 3.5 自動売買の銘柄ルーティング (TV ティッカー → kabu 銘柄コード)

**背景**: 利用者が **TradingView のチャートで見ている銘柄** と **本ブリッジ / kabu Station で発注に使う銘柄** は一致しないことがある。
たとえば「TV では Mini (NK225M1!) を見ているが、口座資金の都合で発注は Micro (161060023, 2026年6月限) で行う」運用は十分あり得る。

**設計判断**: TradingView Webhook の `SymbolTicker` フィールド (例: `"OSE:NK225M1!"`) を **発注時には一切使用しない**。
代わりに、本ブリッジで **ユーザーが選択している銘柄の Resolved Symbol Code** (kabu の数値銘柄コード、例: `"161060023"`) を発注先銘柄として採用する。

#### 3.5.1 構成要素

| 構成要素 | 配置層 | 役割 |
|---|---|---|
| `IAutoTradeInstrumentProvider` | Application/Signals | 「自動売買対象銘柄」の状態 (ResolvedSymbolCode / DisplayName / ContractMonth) を保持・参照する抽象 |
| `AutoTradeInstrumentProvider` | Application/Signals | 上記の具象 (Singleton)。スレッドセーフ・変更通知イベント発火 |
| `MainViewModel` の銘柄選択 | UI | 「手動発注パネルの選択銘柄」が同時に「自動売買発注先銘柄」を兼ねる |
| `SignalHandler` | Application/Signals | シグナル受信時に provider から ResolvedSymbolCode を取得し SignalInterpreter に渡す |
| `SignalInterpreter.Interpret(payload, mode, symbol)` | Application/Signals | 第 3 引数の `symbol` を Intent.Symbol に詰める (payload.SymbolTicker は読まない) |

#### 3.5.2 プロバイダ更新タイミング

UI 層 (MainViewModel) が以下のイベントで provider を更新する:

1. **起動時の銘柄解決完了直後** — `TryResolveInstrumentsAsync` で kabu `/symbolname/future` + `/symbol/@/exchange` を叩き、現月コード (例: `NK225micro` → `161060023`) を解決した直後に push する
2. **手動発注パネルで銘柄選択を変更したとき** — `OnManualOrderInstrumentChanged` で新しい `ResolvedSymbolCode` を push する

更新時には情報レベルでログを残す:

```
自動売買対象銘柄: 日経225Micro 2026年6月限 (シンボルコード=161060023)
```

#### 3.5.3 安全側挙動 (未解決時)

起動直後の数秒間は kabu `/symbolname/future` への応答待ちで `ResolvedSymbolCode = null` の状態が続く。
その間に TradingView から自動売買シグナルが届いた場合、本ブリッジは **発注経路を停止** してシグナルを `Ignored_` 扱いで返す:

```
自動売買対象銘柄が未解決 (起動直後/kabu API 応答待ち) のためシグナルを拒否: alert=mesa77s3
```

これは「銘柄解決前の発注は kabu に拒否される (Code=4002001 銘柄が見つからない)」のを未然に防ぎ、誤った body を kabu に投げないための安全側挙動。

#### 3.5.4 運用上の含意

- **戦略ごとに発注銘柄を分けることはできない**。Mini と Micro を同時に自動売買したい場合は本機構の拡張が必要 (`IAutoTradeInstrumentProvider` を「戦略 → 銘柄」マップに進化させる)。
- **手動発注を別銘柄で試す間、自動売買も同じ銘柄に切り替わる**。試験的に Mini を手動で打ちつつ自動売買は Micro を維持したい、といった運用は本機構ではできない (B 案として `AutoTradeInstrument` を `ManualOrderInstrument` と分離する拡張余地あり、現バージョンでは未実装)。
- **TradingView のシンボルを変更しても発注先は変わらない**。Pine 戦略の `symbol` 設定は本ブリッジの発注銘柄に影響しないため、利用者は TV と kabu を独立に設定できる。

### 3.6 永続化

#### 3.6.1 集約リポジトリ
- **初期段階**: メモリ内コレクション (`InMemoryOrderRepository`, `InMemoryPositionRepository`)
- **将来**: SQLite (Entity Framework Core) または LiteDB
- アプリ再起動時の状態復元は **ブローカーから現在ポジション再取得** で行う (kabu のように建玉状態が API で取れる前提)
- 取引履歴は監査ログ (Serilog → ファイル) で残す

#### 3.6.2 メタデータストア (旧 CSV → 新 JSON への移行)

旧 N225OrderBridge では戦略・建玉・注文のメタデータを **CSV ファイル 3 種** で永続化していたが、新ブリッジでは **JSON ファイル** に統一する (2026-05-18 確定)。

| 旧 N225OrderBridge (CSV) | 新 N225BrokerBridge (JSON) | 役割 |
|---|---|---|
| `N225V2/Csvfile/Strategy.csv` | `strategies.json` | 戦略一覧 (AlertName/Interval/IsEnabled/Description/LastSignal 系) |
| `N225V2/Csvfile/position.csv` | `auto-positions.json` | 自動建玉メタデータ (ExecutionID → Strategy/Interval/SymbolCode/Side/OpenedAt) |
| `N225V2/Csvfile/order.csv` | `orders-metadata.json` | 注文メタデータ (BrokerOrderId → TradeMode/Strategy/Interval/TargetExecutionId 等) |

**配置場所**: `%LOCALAPPDATA%/N225BrokerBridge/`

**JSON を選んだ理由**:
- スキーマ進化に強い (任意フィールド追加可、不要フィールドは無視)
- 階層化・配列ネスト可 (フラットな CSV では表現困難な構造を扱える)
- `System.Text.Json` 標準ライブラリで簡潔に扱える
- 人間可読 (`WriteIndented = true` + camelCase)
- データ件数 (建玉数百件・注文数千件規模) では性能差は無視できる

**運用フロー** (旧 OrderManager / PositionManager の `ToCsv`/`CsvRead` 相当を JSON 化):
1. **発注時**: `PlaceNewOrderUseCase` / `ClosePositionUseCase` / `ManualClosePositionUseCase` が Accepted を受けた直後、`IOrderMetadataStore.UpsertAsync()` で即座に書き込み (旧: `OrderManager.Append` → `ToCsv`)
2. **約定時**: `ExecutionApplier` が新規建玉を作成した時、自動取引なら `IAutoPositionMetadataStore.UpsertAsync()` で記録
3. **起動時**: `PositionReconciliationService` が kabu `/positions` と `auto-positions.json` を ExecutionId で突合し、TradeMode/Strategy/Interval を復元 (旧: `PositionManager.AppendList`)
4. **起動時 (注文)**: `KabuOrderPollingService` が `/orders` をポーリングするたび `MainViewModel.OnOrderSnapshotsUpdated` で BrokerOrderId をキーに `orders-metadata.json` から TradeMode/Strategy/Interval を引いて UI 反映 (旧: `OrderManager.AppendList` で csv 突合)
5. **削除**: 建玉が消えたら `IAutoPositionMetadataStore.RemoveAsync()` で同期削除 (`SyncToActiveSetAsync` で起動時に古いエントリも一括掃除)

**暗号化**:
- 機密設定 (`appsettings.Local.json`) のみ DPAPI で暗号化保存 (同一 Windows ユーザー + 同一マシン限定)
- 上記 3 つのメタストア (戦略・建玉・注文) は機密ではないため平文 JSON


---

## 4. 横断的関心事 (Cross-Cutting Concerns)

### 4.1 ロギング (Serilog)

- 構造化ログ (JSON) でファイル出力
- レベル: Verbose / Debug / Information / Warning / Error / Fatal
- カテゴリ: `Signal`, `Order`, `Position`, `Broker.Kabu`, `Broker.Rakuten`, `UI`
- 重要操作 (注文発注・建玉変更) は必ず Information 以上で記録

```csharp
_logger.Information(
    "Order placed: OrderId={OrderId} Symbol={Symbol} Side={Side} Qty={Quantity} Strategy={Strategy}",
    order.Id, order.Symbol, order.Side, order.Quantity, order.Strategy);
```

### 4.2 依存性注入 (Microsoft.Extensions.DependencyInjection)

- `Program.cs` または `App.xaml.cs` で `IServiceCollection` を構築
- 各レイヤーごとに DI 拡張メソッド: `AddDomain()`, `AddApplication()`, `AddInfrastructure()`, `AddUI()`
- スコープ:
  - Singleton: `IBrokerAdapter` の各実装、`ILogger`, `IEventBus`
  - Transient: ユースケースクラス
  - Scoped: 必要に応じて (今回は UI ベースなので少数)

### 4.3 設定 (Configuration)

- `appsettings.json` で接続情報・タイムアウト・パスフレーズ等を管理
- 機密 (パスフレーズ・API キー) は `appsettings.Local.json` に分離し、`.gitignore` で除外

### 4.4 エラーハンドリング

- ドメイン例外: `DomainException` 基底クラス + 個別例外 (`PositionNotFoundException` 等)
- インフラ例外: ブローカー API エラーは adapter 内で適切にラップ → ドメイン用語の例外に変換
- UI 層: 例外を ViewModel でキャッチ → 通知表示 + ログ
- 致命的例外: ロガー出力 + クラッシュレポート保存 + ユーザー通知

---

## 5. テスト戦略

### 5.1 単体テスト (Domain / Application)

- **対象**: 集約・値オブジェクト・ドメインサービス・ユースケース
- **手法**: xUnit + Moq (必要時のみ; ドメイン層はモック不要が理想)
- **目標カバレッジ**: Domain 90%+ / Application 80%+

### 5.2 統合テスト (Infrastructure)

- **対象**: ブローカーアダプタ・Webhook サーバー・リポジトリ
- **手法**: 本番 API を直接叩かない (モック HTTP サーバー or fixture 応答)
- **kabu 統合テスト**: kabu ステーションのテスト環境がある場合のみ実機テスト

### 5.3 E2E テスト

- **対象**: Webhook 受信 → 注文発注 → 建玉更新の全経路
- **手法**: モック adapter を使い、テスト用 Webhook ペイロードを投入
- **頻度**: リリース前確認用

---

## 6. ビルド・配布

### 6.1 ビルド

- Visual Studio 2022 でソリューションを開いてビルド
- CLI: `dotnet build N225BrokerBridge.sln -c Release`
- 出力: `src/N225BrokerBridge.UI/bin/Release/net8.0-windows/`

### 6.2 配布形式

- **Self-Contained 配布** (.NET ランタイム同梱の単一実行可能ファイル)
- ターゲット: `win-x64`
- インストーラ: 将来的に WiX または Inno Setup で MSI 化を検討

```powershell
dotnet publish src/N225BrokerBridge.UI -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

### 6.3 アップデート

- 初期: 手動配布 (実行ファイル差し替え)
- 将来: ClickOnce 自動更新も検討対象

---

## 7. セキュリティ

### 7.1 Webhook 認証

- `passphrase` フィールドを必須化 (現 N225OrderBridge と同じ方式を踏襲)
- 不一致なら受信を破棄してログ警告
- 将来: HMAC 署名検証も検討

### 7.2 API キー保管

- `appsettings.Local.json` (.gitignore 対象) に格納
- 配布時は Windows DPAPI で暗号化することを検討

### 7.3 リスニングポート

- Webhook 受信は **localhost のみ** にバインド (現 N225OrderBridge と同じ方針)
- 外部からの直接アクセスは不可。Cloudflare Tunnel 等は外部運用責任

---

## 8. パフォーマンス目標

| 指標 | 目標 | 備考 |
|------|------|------|
| Webhook 受信 → 注文発注 (P95) | < 200ms | kabu API タイムアウトは 5 秒 |
| 約定通知 → 建玉更新 (P95) | < 100ms | UI 更新含めず |
| UI 起動時間 | < 3 秒 | コールドスタート |
| メモリ常駐 | < 200MB | アイドル時 |

---

## 9. 既存システムとの統合

### 9.1 現 N225OrderBridge との並行運用

- 同じ Webhook ポート (`localhost:8000`) は **使用しない** (現行と競合するため)
- 新ポート (`localhost:8001` 等) を設定し、TradingView Webhook を二重発火させて並行運用
- 安定動作確認後、現行を停止して新ブリッジに移行

### 9.2 N225 Dashboard (Python) との関係

- **直接統合しない** (言語スタック分離方針)
- 接点候補:
  - 共有 SQLite DB を読み書き (戦略リスト・実行履歴等)
  - 新ブリッジが公開する REST API を Dashboard が呼ぶ
  - どちらにするかは Phase 4 (UI) 時点で決定

---

## 改版履歴

| 日付 | バージョン | 内容 |
|------|----------|------|
| 2026-05-17 | v1.0 | 初版起草。DDD 4 層 + ブローカー抽象 + 横断関心事を整理。 |
| 2026-05-23 | v1.1 | 3.5 「自動売買の銘柄ルーティング」節を新設。TV ティッカーを使わずブリッジ選択銘柄の Resolved Symbol Code を発注先銘柄とする方針・未解決時の安全側挙動・運用上の含意を明文化。旧 3.5/3.5.1/3.5.2 (永続化) を 3.6/3.6.1/3.6.2 に番号繰り下げ。 |
