# シミュレータモード (`--simulator`)

**バージョン**: 0.1.0 (初版ドラフト)
**作成日**: 2026-05-27
**最終更新**: 2026-05-27
**目的**: kabu Station にも本物の TradingView にも一切繋がない状態で、ブリッジの全フロー (Webhook 受信 → 発注 → 約定 → 建玉計上 → 返済) を **実際に動かして体験できる** モード。

---

## 1. 概要

`N225BrokerBridge.UI.exe` を `--simulator` 引数付きで起動すると、`KabuAdapter` の代わりに **`MockBrokerAdapter`** (in-memory 実装) が DI 登録され、以下の挙動になる:

| 項目 | 本番モード | デモモード (`--demo`) | **シミュレータモード (`--simulator`)** |
|---|---|---|---|
| 用途 | 実発注 | スクショ撮影 | **動作体験・配布デモ** |
| kabu API への接続 | あり | なし | **なし (MockBrokerAdapter に差し替え)** |
| Webhook listener | LISTEN (本番=8001) | LISTEN しない | **LISTEN する (シミュレータ既定=8000、test_all.ps1 と整合)** |
| 注文ポーリング | 起動 | 起動しない | **起動する (Mock 上で動作)** |
| Position reconciliation | 起動 | 起動しない | **起動する (Mock の建玉と同期)** |
| WebSocket 板情報 | 接続 | 接続しない | **接続しない (PriceStream は Mock 内部で生成)** |
| 戦略レジストリ | 読み書きする | しない | **読み書きする** |
| 建玉メタストア | 読み書きする | しない | **読み書きする (Mock の建玉 ID で蓄積)** |
| 手動発注ボタン | 実発注 | エラー | **Mock 上で約定** |
| 価格表示 | リアルタイム | 固定 | **55,600 ± 50 円のランダム揺らぎ** |

つまり「**外部接続だけ Mock 化、ブリッジ内部のロジックは全部本物のまま動かす**」状態が作られる。

---

## 2. 用途

### 2-1. 配布物の動作デモ
購読者が `kabu Station 口座 + TradingView Pro+` を未取得でも、`--simulator` で起動すれば Webhook 受信 → 発注 → 約定 → 建玉表示の全体動作を体験できる。

### 2-2. 開発者の回帰テスト
コード変更後、kabu Station を起動せずに「シグナル → 発注 → 約定」一連のフローが壊れていないかを即時確認できる。

### 2-3. Webhook ペイロード検証
`docs/webhook_test/payloads/*.json` の 7 種類のペイロード (新規買い / 反対決済 / ドテン / フラット無視 / 認証失敗 / Bad JSON / 未登録銘柄) を `test_all.ps1` で流し、各シナリオの挙動を確認できる。

### 2-4. ブログ第 2 話の素材
「環境がなくても動かせる」を実証する。

---

## 3. 使い方

### 3-1. コマンドライン

```
N225BrokerBridge.UI.exe --simulator
```

### 3-2. 起動後の挙動

1. MainWindow が通常通り開く
2. ステータスバーに **`SIMULATOR`** バッジ (Amber / 注意色) を表示
3. ブローカー接続表示: 「接続中 (Mock)」
4. Webhook 状態: 「受信中 (port 8000、既定値)」
5. 現在値: 55,600 円付近で 1 秒ごとに揺らぐ (55,550〜55,650)
6. 手動発注ボタン → **確認ダイアログ表示 → OK で Mock 上で即時約定** → 建玉一覧に追加
7. Webhook 受信 → 通常通り発注 UseCase 経由で Mock 上で約定
8. 終了時はメモリ状態がすべて消える (永続化なし)

### 3-3. ショートカット

`--demo` と同じ要領で別ショートカットを作るのが推奨。

---

## 4. アーキテクチャ

### 4-1. クラス図 (テキスト)

```
N225BrokerBridge.Domain.Brokers.IBrokerAdapter
         ▲
         │
         ├── N225BrokerBridge.Infrastructure.Brokers.Kabu.KabuAdapter (既存)
         │
         └── N225BrokerBridge.Infrastructure.Brokers.Mock.MockBrokerAdapter (新規)
```

`IBrokerAdapter` を完全に満たす in-memory 実装。`KabuAdapter` との差し替えは DI 層 (`InfrastructureServiceCollectionExtensions`) で起動引数を見て分岐。

### 4-2. DI 切替

```
App.xaml.cs
   ├── --simulator フラグ判定 → App.IsSimulatorMode = true
   └── BootstrapAsync 内で services.AddBrokerBridgeKabu() の代わりに
       services.AddBrokerBridgeMockBroker() を呼ぶ

InfrastructureServiceCollectionExtensions
   ├── AddBrokerBridgeKabu()      (既存、本番)
   └── AddBrokerBridgeMockBroker() (新規、シミュレータ)
        ├── MockBrokerAdapter を IBrokerAdapter として Singleton 登録
        ├── KabuTokenService / KabuApiClient は登録しない
        ├── KabuOrderPollingService は登録しない (Mock 内部で約定通知)
        └── KabuBoardWebSocketService は登録しない
```

### 4-3. データフロー (シナリオ: 新規買いシグナル)

```
TradingView (またはローカル curl)
      │ POST http://localhost:8001/webhook
      ▼
HttpWebhookListener  →  SignalPayloadParser  →  SignalInterpreter
                                                       │
                                                       ▼
                                              PlaceNewOrderUseCase
                                                       │
                                                       ▼
                                              MockBrokerAdapter.PlaceOrderAsync
                                                       │
        ┌──────────────────────────────────────────────┤
        │ 1. OrderResult { Status=Accepted, BrokerOrderId="MOCK-00001" } を即時返却
        │ 2. 50ms 後に ExecutionEvent.OnNext(...) を発火 (約定通知)
        ▼
ExecutionApplier  →  Order/Position 集約更新  →  UI 反映
```

---

## 5. MockBrokerAdapter の責務詳細

### 5-1. 状態

```csharp
private readonly Dictionary<OrderId, OrderSnapshot> _orders = new();
private readonly Dictionary<ExecutionId, PositionSnapshot> _positions = new();
private readonly Subject<ExecutionEvent> _executionSubject = new();
private readonly Subject<PriceTick> _priceSubject = new();
private int _orderIdSequence = 0;
private int _executionIdSequence = 0;
```

すべて in-memory。起動の都度リセット。永続化は一切しない。

### 5-2. メソッド別動作

| メソッド | Mock の動作 |
|---|---|
| `BrokerCode` | `BrokerCode.Mock` (新規追加) を返す |
| `IsConnected` | 常に `true` |
| `PlaceOrderAsync` | OrderId 採番 (例: `MOCK-00001`) → `_orders` に Accepted で追加 → 50ms 後に約定 ExecutionEvent 発火 → `_positions` に建玉追加 |
| `ClosePositionAsync` | 指定 `TargetExecutionId` の建玉を `_positions` から取得 → 残量チェック → OrderId 採番 → 50ms 後に約定 → `_positions` の残量を減らす (0 なら削除) |
| `CancelOrderAsync` | `_orders` の該当注文の State を Cancelled に更新 (実際には Mock では 50ms で約定するのでキャンセル成功は稀) |
| `GetPositionsAsync` | `_positions.Values` を返す |
| `GetOrdersAsync` | `_orders.Values` を返す |
| `GetQuoteAsync` | 55,600 ± 50 円のランダム値で `QuoteSnapshot` を生成 |
| `ExecutionStream` | `_executionSubject.AsObservable()` |
| `PriceStream` | `_priceSubject.AsObservable()` (バックグラウンドで 1 秒ごとに Tick を発火) |
| `SubscribePriceAsync` | no-op (Mock は登録された全銘柄に常時 Tick を流す) |
| `SubscribePricesAsync` | no-op |
| `UnsubscribePriceAsync` | no-op |
| `ResolveFutureSymbolAsync` | `ResolvedSymbol("MOCK-NK225-202606", "日経225Micro Mock", "2026年6月限")` を返す |

### 5-3. 価格ティック生成

`BackgroundService` として `MockPriceTickService` (or MockBrokerAdapter 内部 Timer) が 1 秒ごとに発火:

```
LastPrice = 55,600 + Random.Next(-50, 51)
BidPrice  = LastPrice + 5
AskPrice  = LastPrice - 5
```

(kabu の BID/ASK 命名は通常と逆なので、`docs/adapters/kabu.md §1` の規約に従う)

### 5-4. 約定タイミング

すべての発注は **50ms 後に即時約定** として処理:
- 成行 → 現在価格で約定
- 指値 → 指値価格で約定 (実価格との乖離は無視)
- 逆指値 → 即時約定 (トリガー監視は省略)

これは「テストの再現性最優先」設計。リアル感より「動かしてみると確実に約定する」ことを優先する。

### 5-5. 部分約定

Mock では行わない (常に一括約定)。

---

## 6. 起動フロー

### 6-1. App.xaml.cs の変更

```csharp
public static bool IsSimulatorMode { get; private set; }

protected override async void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    IsDemoMode      = e.Args.Any(a => string.Equals(a, "--demo",      StringComparison.OrdinalIgnoreCase));
    IsSimulatorMode = e.Args.Any(a => string.Equals(a, "--simulator", StringComparison.OrdinalIgnoreCase));
    // (どちらも true は禁止 → 例外。IsDemoMode が優先という設計でも可)
    ...
}
```

排他チェック: `--demo` と `--simulator` を同時指定したら **`--simulator` 優先** (`IsDemoMode` を false に上書きして起動)。起動ログに警告を出す。

### 6-2. DI の切替

```csharp
.ConfigureServices((ctx, services) =>
{
    services.AddBrokerBridgeApplication(...);
    services.AddBrokerBridgeInfrastructure();
    if (IsSimulatorMode)
    {
        services.AddBrokerBridgeMockBroker();   // 新規
    }
    else
    {
        services.AddBrokerBridgeKabu();          // 既存
    }
    services.AddBrokerBridgeWebhook();
    ...
})
```

### 6-3. ホスト起動

`IsSimulatorMode` のときは `_host.StartAsync()` を **呼ぶ** (Webhook listener を起動させるため)。
`IsDemoMode` のときは従来通り呼ばない。

---

## 7. ファイル変更計画

### 7-1. 新規作成

| ファイル | 内容 | 概算行数 |
|---|---|---|
| `src/N225BrokerBridge.Infrastructure/Brokers/Mock/MockBrokerAdapter.cs` | `IBrokerAdapter` 実装 | 250〜350 |
| `src/N225BrokerBridge.Infrastructure/Brokers/Mock/MockBrokerOptions.cs` (任意) | 価格中心値・揺らぎ幅・約定遅延等の設定 | 30 |
| `tests/N225BrokerBridge.Infrastructure.Tests/Brokers/Mock/MockBrokerAdapterTests.cs` | 単体テスト 8〜10 件 | 200 |
| `docs/simulator-mode.md` | このファイル | (このファイル) |

### 7-2. 修正

| ファイル | 修正点 |
|---|---|
| `src/N225BrokerBridge.UI/App.xaml.cs` | `IsSimulatorMode` プロパティ追加 / 引数判定 / DI 分岐 / `_host.StartAsync()` の条件分岐再整理 |
| `src/N225BrokerBridge.Infrastructure/DI/InfrastructureServiceCollectionExtensions.cs` | `AddBrokerBridgeMockBroker()` メソッド追加 |
| `src/N225BrokerBridge.Domain/ValueObjects/BrokerCode.cs` | `Mock` 値を追加 |
| `src/N225BrokerBridge.UI/Views/MainWindow.xaml` | ステータスバーに `SIMULATOR` バッジ表示 (バインディング `App.IsSimulatorMode`) |
| `src/N225BrokerBridge.UI/ViewModels/MainViewModel.cs` | (もしあれば) シミュレータ時のヘッダー文言調整 |
| `docs/README.md` | 索引に `simulator-mode.md` 追加 |
| `docs/requirements.md` | F-18 (シミュレータモード) 追加 |
| `docs/demo-mode.md` | 冒頭に「動作テストには `--simulator` を参照」のクロスリファレンス |
| `docs/roadmap.md` | 4.2 (Mock Data Injector) を「✅ シミュレータモードで代替実装済」として更新 |
| `docs/architecture.md` | (任意) `MockBrokerAdapter` の存在を追記 |

---

## 8. テスト計画

### 8-1. 単体テスト (xUnit)

`MockBrokerAdapterTests.cs`:

1. `PlaceOrderAsync` (新規買い 1 枚) → 50ms 後に ExecutionEvent 発火、`_positions` に 1 件追加
2. `ClosePositionAsync` (1 枚返済) → 50ms 後に ExecutionEvent 発火、`_positions` から削除
3. `ClosePositionAsync` (部分返済 2 枚中 1 枚) → LeaveQuantity が 1 に減る
4. `ClosePositionAsync` (存在しない PositionId) → 例外 or Rejected
5. `GetPositionsAsync` 初期状態 → 空配列
6. `GetOrdersAsync` 初期状態 → 空配列
7. `PriceStream` → 5 秒 subscribe して 5 件取得できる
8. `ResolveFutureSymbolAsync` → `MOCK-NK225-202606` が返る

### 8-2. 統合テスト (手動)

`docs/webhook_test/payloads/*.json` 全 7 種を `test_all.ps1` で流し、以下を確認:

| ペイロード | 期待動作 |
|---|---|
| 01_auth_failed.json | 401 拒否、Mock に何も影響しない |
| 02_bad_json.txt | 400、Mock に何も影響しない |
| 03_new_buy.json | 200、新規買い建玉 1 件追加、UI に表示 |
| 04_exit_long.json | 200、03 の建玉が返済され消える |
| 05_doten_short_to_long.json | (前提: 売り建玉あり) → 返済 + 新規買い |
| 06_ignored_flat_to_flat.json | 200、状態変化なし |
| 07_not_registered.json | 200、未登録銘柄として無視 |

### 8-3. UI 目視テスト

1. `--simulator` 起動 → ステータスバーに `SIMULATOR` バッジ表示
2. 手動発注 (買 1) → 建玉一覧に 1 件追加
3. その建玉を選択 → 返済ボタン → 建玉一覧から消える
4. 現在値が 1 秒ごとに揺らぐ
5. ×で閉じる → 再起動で状態リセット

---

## 9. 安全保証

### 9-1. 本番モードへの影響なし

`--simulator` を付けない通常起動は `IsSimulatorMode = false` のまま。DI 分岐は false 側 (`AddBrokerBridgeKabu`) を通る。**コードパス的に本番挙動は完全に従来通り**。

### 9-2. Mock の建玉が本番口座に流出しないこと

`MockBrokerAdapter` は HTTP リクエストを一切発行しない (`HttpClient` を持たない)。物理的に外部通信が不可能。

### 9-3. 永続化ファイルは触る (ここがデモモードと異なる)

シミュレータモードでは `strategies.json` / `auto-positions.json` も通常通り書く。これは「ブリッジ内部のロジックを全部本物のまま動かす」設計のため。

**懸念**: シミュレータの建玉メタが `auto-positions.json` に書かれ、本番起動時にゴミとして残る可能性。

**採用 (A 案、2026-05-27 確定)**: シミュレータモード専用の永続化先を分ける。
- `auto-positions.simulator.json`
- `strategies.simulator.json` (戦略レジストリも分離)
- `appsettings.Local.simulator.json` (LocalSettingsStore は元々 DPAPI 暗号化されているので、シミュレータ用は別ファイルにする)

`%LOCALAPPDATA%\N225BrokerBridge\` 直下に並べる。本番ファイルとは独立、相互に干渉しない。

---

## 10. 制約事項 (Mock では再現しないこと)

- **指値の価格条件**: 指値・逆指値の発火条件は無視。常に即時約定。
- **部分約定**: 一括約定のみ。分割約定シミュレーションなし。
- **約定遅延の大小**: 常に 50ms。リアルな遅延揺らぎはなし。
- **kabu API のエラーコード**: Rejected ケースは Mock では発生しない (パラメータ妥当性チェックは最低限のみ)。
- **WebSocket 切断**: Mock の `PriceStream` は内部 Timer で生成するため、切断・再接続シナリオは再現しない。
- **本番口座の建玉再同期 (起動時 reconciliation)**: Mock は再起動するたびに建玉 0 から始まるため、reconciliation は no-op に近い。

これらが必要になったら個別に「Mock の高度化」を `roadmap.md` に登録して対応。

---

## 11. シミュレータと既存の `--demo` モードの使い分け

| 用途 | 推奨モード |
|---|---|
| ブログ記事用にきれいな UI スクショを撮る (建玉・損益も決め打ち) | `--demo` |
| 「動かしてみたい」読者に体験してもらう | `--simulator` |
| 開発中に「全部のフローが壊れていないか」を kabu 起動せず確認したい | `--simulator` |
| UI 微調整時にとにかく起動だけ早くしたい | `--demo` |
| Webhook ペイロード検証 | `--simulator` |

---

## 12. 配布フローへの影響

| 項目 | 影響 |
|---|---|
| `N225BrokerBridge-public` リポ | `MockBrokerAdapter` 含めて配布される (Mock も配布物の一部) |
| `N225BrokerBridge-runtime` リポ | `.claude/commands/setup.md` から `--simulator` を起動する案内を追加 |
| ブログ第 2 話 | 「`--simulator` 起動 → Webhook テストペイロードで動作確認」を主軸に書く |
| `sync_to_public.ps1` | 変更不要 (allowlist で `N225BrokerBridge` 全体が対象) |

---

## 13. 実装着手順序

1. `BrokerCode.Mock` 追加 (Domain)
2. `MockBrokerAdapter` 新規作成 + 単体テスト (Infrastructure)
3. `AddBrokerBridgeMockBroker()` 追加 (Infrastructure.DI)
4. `App.xaml.cs` に `IsSimulatorMode` + 引数判定 + DI 分岐追加
5. `MainWindow.xaml` ステータスバーに SIMULATOR バッジ
6. ビルド → 起動 → 目視確認
7. Webhook ペイロード 7 種をテスト
8. 永続化分離案 (§9-3 A 案) を実装
9. README/requirements/roadmap/demo-mode の関連ドキュメント更新

---

## 14. 関連ドキュメント

- [requirements.md](./requirements.md) §5 F-18 (新規追加予定)
- [demo-mode.md](./demo-mode.md) — `--demo` モードとの違い
- [architecture.md](./architecture.md) — DDD 4 層構造
- [class-design.md](./class-design.md) — `IBrokerAdapter` クラス階層
- [adapters/kabu.md](./adapters/kabu.md) — 本物の kabu アダプタ実装ノート
- [roadmap.md](./roadmap.md) §4.2 — 旧 Mock Data Injector 案 (本仕様で代替される)

---

## 15. 確認事項 (2026-05-27 ユーザー確定)

| # | 項目 | 確定値 |
|---|---|---|
| 1 | モード名 | **`--simulator`** |
| 2 | 価格中心値 | **55,600 円** |
| 3 | 価格揺らぎ幅 | **±50 円** |
| 4 | 約定遅延 | **50ms** |
| 5 | 永続化分離方針 | **A 案: `*.simulator.json` で分離** |
| 6 | 手動発注ボタン挙動 | **確認ダイアログを出してから Mock 上で即時約定** |
| 7 | `--demo` と `--simulator` 同時指定 | **`--simulator` 優先 (両方指定なら simulator として起動)** |

---

## 16. 変更履歴

| 日付 | バージョン | 内容 |
|---|---|---|
| 2026-05-27 | 0.1.0 | 初版ドラフト作成。MockBrokerAdapter 設計 + DI 切替 + 7 ペイロードテスト計画を明文化 |
| 2026-05-27 | 0.1.1 | §15 ユーザー確認事項 7 件を確定値で更新。同時指定は `--simulator` 優先、手動発注は確認ダイアログ経由に変更 |
| 2026-05-27 | 0.1.2 | 実装完了 + 起動確認済。Webhook ポートを 8001 → 8000 (test_all.ps1 と整合する appsettings.json 既定値) に訂正 |
