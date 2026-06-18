# 要件定義書 (Requirements)

**バージョン**: 1.0.0
**作成日**: 2026-05-26
**最終更新**: 2026-05-26
**対象**: N225BrokerBridge v0.x

---

## 1. このドキュメントの目的

本書は N225BrokerBridge が「何のためのソフトウェアか」「誰のために何を実現するか」「どんな制約のもとで動くか」を明文化する。
コードと既存設計ドキュメントから逆引きで再構築した要件であり、今後の機能追加・改修・障害対応の判断根拠となる。

関連ドキュメント:
- [機能仕様書](./functional-spec.md) — 本要件が具体的にどう機能化されているか
- [アーキテクチャ概要](./architecture.md) — 要件を実現する技術構成
- [開発ロードマップ](./roadmap.md) — 未実装要件・将来要件

---

## 2. 背景

### 2.1 前提

ユーザーは日経 225 先物 (ミニ / マイクロ) を自動売買する個人トレーダー。
以下の 2 系統を並行運用している:

1. **機械的戦略群** — TradingView Pine Strategy で開発した戦略を Webhook 経由で本ブリッジへ通知し、kabu Station 経由で au カブコム証券に発注
2. **LLM アドバイザリー (N225LLMAdvisor)** — 朝の分析・日中監視を Claude / Groq で実施（本ブリッジとは独立）

本書は **1** の発注経路を担う N225BrokerBridge の要件を定義する。

### 2.2 旧システムからの移植

N225BrokerBridge は旧 `N225OrderBridge` (典型的な単一機能 WinForms アプリ) を以下の方針で全面リライトしたもの:

- 言語 / フレームワーク: WinForms → **WPF (.NET 8) + Wpf.Ui**
- アーキテクチャ: トランザクションスクリプト → **DDD 4 層 + クリーンアーキテクチャ**
- 命名: 旧コードの typo (`Contrct`, `Iterval`, `Pssword`, `KubuAPIs`, `Infrastrucure`) を全廃し**ユビキタス言語辞書** に統一
- ブローカー結合: kabu 直結 → **`IBrokerAdapter` 抽象による複数ブローカー対応 (Day 1)**
- 永続化: SQLite + CSV ハイブリッド → **JSON ファイル (`%LOCALAPPDATA%/N225BrokerBridge/`)**

> 旧 OrderBridge が固有に抱えていた発注経路バグ（部分返済の予約枠解放漏れ等）は本書 §6 の品質要件で「ゼロにする」と定義する。

---

## 3. 関係者 (Stakeholders)

| 関係者 | 役割 | 関心事 |
|---|---|---|
| **個人トレーダー (ユーザー本人)** | 主たる利用者 | 朝起動 → 日中放置 → 夕方確認 で安定稼働すること。発注漏れ / 二重発注 / 建玉誤認 がないこと |
| **TradingView Pine Strategy** | シグナル発信元 | Webhook 仕様が変わらないこと。発火から発注までの遅延が小さいこと |
| **kabu Station (au カブコム証券)** | 発注先ブローカー | 認証 / 銘柄コード / 約定通知のプロトコルが正しく守られること |
| **本プロジェクト ブログ購読者 (将来)** | システム解説の読者 | 設計判断が追跡可能で、自身でも同等システムが組めること |

---

## 4. システムの目的とスコープ

### 4.1 目的

TradingView 戦略が発火したアラートを、**ユーザー操作なしに**証券会社の実弾発注へ橋渡しする。同時に、人間が手動で発注・返済・キャンセルを行うための画面も提供する。

### 4.2 スコープ内 (本ブリッジが担う)

| 機能領域 | 内容 |
|---|---|
| シグナル受信 | TradingView Webhook (HTTP POST JSON) の待受・パース・認証 |
| シグナル解釈 | `prev_market_position` / `market_position` / `order_action` から「新規 / 返済 / 部分返済 / ドテン / 無視」を判定 |
| 自動発注 | 解釈結果に基づき kabu 経由で発注 |
| 手動発注 | UI 上の操作で新規・返済・キャンセルを実行 |
| 建玉管理 | 約定通知から建玉を生成・保持・返済時に消化 (FIFO 跨ぎ消化対応) |
| 注文管理 | 受付・約定・取消・失効・拒否のライフサイクル管理。pending 注文の追跡 |
| 戦略レジストリ | 戦略名 × 足 のキーで有効 / 無効を切り替え。最終受信シグナル履歴を表示 |
| 自動売買トグル | 全シグナル受付を 1 クリックで止められるグローバルゲート |
| 価格ティック表示 | 板情報 (Bid / Ask / Last) の WebSocket リアルタイム表示 |
| 限月解決 | 起動時に先物コード (例: `NK225mini`) → 現月の具体銘柄コード に解決 |
| ログ | 構造化ログ (Serilog) を コンソール / ファイル / UI 同時出力 |
| 設定 | Webhook パスフレーズ・kabu パスワード・接続環境を **DPAPI 暗号化** で永続化 |
| デモモード | `--demo` 引数で外部接続を全遮断し、決め打ちデータでスクリーンショット撮影可能 |

### 4.3 スコープ外 (本ブリッジが**やらない**こと)

| やらないこと | 理由 / 代替 |
|---|---|
| 戦略ロジックそのもの | TradingView Pine 側で完結。本ブリッジはシグナル → 発注の "橋渡し" のみ |
| バックテスト | `N225StrategyBuilder` が担当 |
| 市場分析 / 銘柄スクリーニング | `N225LLMAdvisor` が担当 |
| 損益集計 / 確定申告データ | 証券会社の取引履歴を別途利用 |
| マルチユーザー | 個人 1 名利用前提。共有ユーザー認証なし |
| クラウド / リモートアクセス | `localhost` 待受のみ。外部公開は Cloudflare Tunnel で行う |
| iOS / Android / Web UI | WPF (Windows デスクトップ) のみ |
| ストップロスの自動再発注 / トレーリング | TradingView 側で完結 |
| 価格情報の長期保存 | リアルタイム表示のみ。履歴は CSV / DB に書かない |

### 4.4 将来スコープ (現在は未実装、`roadmap.md` 参照)

- 複数ブローカー (楽天証券 等) の `IBrokerAdapter` 実装追加
- LLM 判定経路の合流 (`Application` 層に判定ステップを追加)
- 銘柄管理 UI (現在は Mini / Micro を hardcode)
- 建玉グループ集計表示の復活
- SQLite 永続化への移行

---

## 5. 機能要件 (Functional Requirements)

> 詳細は [functional-spec.md](./functional-spec.md) を参照。本節では番号付きで網羅し、追跡できるようにする。

### F-1. シグナル受信
- F-1.1 `localhost:8001/webhook` で HTTP POST (JSON) を待受
- F-1.2 リクエスト JSON を `SignalPayload` にパース (バリデーション含む)
- F-1.3 パスフレーズ照合に失敗したシグナルは破棄しログに記録
- F-1.4 GET / PUT / その他メソッドは 405 で応答
- F-1.5 Body が JSON でない場合は 400 で応答
- F-1.6 内部例外は 500 で応答し詳細はログのみ (HTTP レスポンスには漏らさない)

### F-2. シグナル解釈
- F-2.1 `prev_market_position` × `market_position` × `order_action` の組合せから 4 種の Intent (`NewOrder` / `Exit` / `Doten` / `Ignore`) のいずれかに分類
- F-2.2 部分返済 (`long → long` で `sell`) を `ExitOrderIntent` として正しく扱う
- F-2.3 `OrderPrice < 0` は 0 に正規化 (成行扱い)
- F-2.4 `OrderContracts` / `MarketPositionSize` が 0 以下は例外
- F-2.5 シグナル発信元の `ticker` (TV ティッカー) は**無視**し、`IAutoTradeInstrumentProvider` の解決済銘柄を採用

### F-3. 自動売買グローバルゲート
- F-3.1 `IAutoTradeGate.IsEnabled = false` の場合、認証以前にシグナルを破棄
- F-3.2 デフォルト `false` (起動直後は OFF)
- F-3.3 UI トグルから即時反映 (volatile bool、thread-safe)
- F-3.4 手動発注は本ゲートの影響を受けない

### F-4. 戦略レジストリ
- F-4.1 戦略は (AlertName, Interval) を主キーとする
- F-4.2 未登録戦略は受信ログのみ残し発注しない
- F-4.3 登録済でも `IsEnabled = false` は発注しない
- F-4.4 受信時に `LastSignalAt` / `LastTradeType` / `LastSide` / `LastPrice` を更新
- F-4.5 UI からの CRUD (追加 / 編集 / 更新 / キャンセル / 削除)
- F-4.6 `strategies.json` に永続化

### F-5. 新規発注
- F-5.1 `NewOrderIntent` から `Order` 集約を生成、`IBrokerAdapter.PlaceOrderAsync` 呼出
- F-5.2 注文タイプ: 成行 / 指値 / 逆指値 / 最良気配
- F-5.3 時間条件: FAS / FAK / FOK
- F-5.4 結果が `Accepted` なら `MarkSubmitted` + `OrderMetadata` 永続化 + `IPendingOrderTracker` に追加
- F-5.5 `Rejected` / `NetworkError` なら `MarkTerminated(Rejected)`
- F-5.6 永続化は `orders-metadata.json` (Strategy / Interval / TradeMode を保持)

### F-6. 返済発注 (自動)
- F-6.1 `ExitOrderIntent` から候補建玉を `IPositionRepository.FindMatchingForCloseAsync` で検索
- F-6.2 `PositionMatcher.BuildPlan` で消化計画を作成 (FIFO・跨ぎ消化対応)
- F-6.3 各 Allocation について `Position.ReserveForClose` → `Order` 生成 → `ClosePositionAsync`
- F-6.4 `Rejected` の場合は `ReleaseReservation` で拘束解放
- F-6.5 `NetworkError` は拘束を保持 (発注通った可能性のため再照会対象)
- F-6.6 `Shortfall > 0` (要求 > 残合計) は計画分まで発注し警告ログ

### F-7. 返済発注 (手動)
- F-7.1 UI で建玉行を選択 → 「返済」ボタン
- F-7.2 数量未指定なら `AvailableForClose` 全量
- F-7.3 注文タイプデフォルト: 最良気配 (`BestMarket`)
- F-7.4 自動返済と同じドメイン経路を通る (`ClosePositionUseCase` 経由)
- F-7.5 確認ダイアログ表示 (`RequireConfirmBeforeOrder = true` 時)

### F-8. ドテン
- F-8.1 `DotenIntent` で `ClosePositionUseCase` → `PlaceNewOrderUseCase` を順序実行
- F-8.2 返済の約定通知を**待たず**新規を発火 (旧仕様踏襲、一時的両建てを許容)

### F-9. 注文キャンセル
- F-9.1 UI で注文行を選択 → 「キャンセル」ボタン
- F-9.2 `IBrokerAdapter.CancelOrderAsync` 呼出
- F-9.3 取消結果は `IOrderSnapshotNotifier` 経由で UI に反映
- F-9.4 取消完了時、返済注文だった場合は対象建玉の `HoldQuantity` を解放

### F-10. 約定反映
- F-10.1 `IBrokerAdapter.ExecutionStream` (実装は kabu /orders ポーリングで擬似的にストリーム化) を購読
- F-10.2 `ExecutionApplier.ApplyAsync` で `Order.ApplyExecution` を呼ぶ
- F-10.3 新規約定 → `Position` 生成 → `IPositionRepository.AddAsync`
- F-10.4 自動取引建玉は `AutoPositionMetadata` を `auto-positions.json` に永続化
- F-10.5 返済約定 → 対象 `Position.ApplyClosure` → `IsClosed` なら削除

### F-11. 起動時状態復元
- F-11.1 Step 1: kabu トークンの warm-up (`GetPositionsAsync` で API キー初期化)
- F-11.2 Step 2: kabu `/orders` 全件取得 → UI に push
- F-11.3 Step 3: kabu `/positions` 全件取得 → メタの突合 (メタあり = Auto / メタなし = Manual) → 死んだメタ削除
- F-11.4 各 Step の例外は警告ログのみで起動継続 (kabu 未起動でも UI は立ち上がる)

### F-12. ポーリング (注文状態追跡)
- F-12.1 `KabuOrderPollingService` が 1 秒間隔で `IPendingOrderTracker` の対象を `/orders?id={id}` で照会
- F-12.2 pending が空ならネットワークを叩かない (旧仕様の InquiryTimer 準拠)
- F-12.3 約定明細 (kabu `RecType=8`) を検出したら `ExecutionEvent` を発火
- F-12.4 終端状態 (kabu `State=5`) なら `Untrack`
- F-12.5 ExecutionID 重複検出のため `ConcurrentDictionary` でガード

### F-13. 価格ティック (WebSocket)
- F-13.1 `KabuBoardWebSocketService` が `ws://localhost:18080/kabusapi/websocket` に接続
- F-13.2 登録銘柄は per-symbol シリアル `/register` (`SubscribePriceAsync` 反復)
- F-13.3 接続失敗時は 5 秒待機 → 自動再接続
- F-13.4 受信フォーマットは `KabuBoardPushDto`
- F-13.5 `PriceUpdated` イベントで UI に反映

### F-14. 限月解決
- F-14.1 起動時に `AvailableInstruments` 各銘柄を `IBrokerAdapter.ResolveFutureSymbolAsync` で解決
- F-14.2 `(FutureCode, DerivMonth=0)` → 現月の数値銘柄コードを取得
- F-14.3 解決失敗時 `ResolvedSymbolCode = null` → 発注不可 (Ignore 扱い)
- F-14.4 SQ 日境界の自動判定は `DerivMonthCalculator` (Application 層)

### F-15. UI 表示
- F-15.1 メインウィンドウ: ステータスバー / 現在値パネル / 戦略一覧 / 建玉一覧 / 注文一覧 / ログ
- F-15.2 設定ダイアログ: Webhook / kabu 接続 / 確認ダイアログ要否
- F-15.3 戦略管理ダイアログ: CRUD + 最終シグナル履歴
- F-15.4 UI レイアウト (ウィンドウサイズ / カラム幅) を 5 秒ごとに `ui-layout.json` へ自動保存
- F-15.5 ログを UI に最新 1000 件まで保持 (UiLogSink)

### F-16. デモモード
- F-16.1 `--demo` 起動引数で外部接続 (kabu / WebSocket / Webhook) を全停止
- F-16.2 `MainViewModel.SeedDemoData` で決め打ちデータを表示
- F-16.3 `strategies.json` / `auto-positions.json` への書き込み禁止

### F-17. 設定永続化
- F-17.1 `LocalSettingsStore` が `%LOCALAPPDATA%/N225BrokerBridge/appsettings.Local.json` を管理
- F-17.2 機密項目 (Webhook パスフレーズ, kabu API パスワード × 3) は DPAPI で暗号化 (`enc:` プレフィックス)
- F-17.3 `appsettings.json` (配布側) と PostConfigure でマージ
- F-17.4 環境切替 (Production / Verification) は単一 RadioButton セット

### F-18. シミュレータモード
- F-18.1 `--simulator` 起動引数で `IBrokerAdapter` 実装を `KabuAdapter` から `MockBrokerAdapter` に DI 切替
- F-18.2 Webhook listener (port 8001) は通常通り起動。Webhook 受信〜発注〜約定の全フローを Mock 上で再現
- F-18.3 価格ティック (`PriceStream`) は Mock 内部で 1 秒ごとに生成 (中心値 55,600 円 ± 50 円のランダム揺らぎ)
- F-18.4 約定タイミングは 50ms 後の即時約定。指値・逆指値の価格条件は無視
- F-18.5 `--demo` と `--simulator` の同時指定は `--simulator` を優先 (`IsDemoMode=false` に上書き、起動ログに警告)
- F-18.5b 手動発注ボタンは確認ダイアログ経由で Mock 上で即時約定 (本番モードと同じ UX)
- F-18.6 永続化先 (strategies.json / auto-positions.json) は本番と分離 (詳細は [`simulator-mode.md`](./simulator-mode.md) §9-3)
- F-18.7 UI ステータスバーに `SIMULATOR` バッジを表示

詳細仕様は [`simulator-mode.md`](./simulator-mode.md) を参照。

---

## 6. 非機能要件 (Non-Functional Requirements)

### 6.1 信頼性

| 要件 | 内容 | 受入基準 |
|---|---|---|
| **NF-R1** 発注の冪等性 | 同じシグナルを 2 回受信しても 2 回発注しない | `IPendingOrderTracker` で同一 OrderId を二重 Track しない |
| **NF-R2** 部分返済の整合 | 拘束 (Hold) と残数 (Leave) の不一致が出ない | `HoldQuantity <= LeaveQuantity` を `Position` で常時保証 |
| **NF-R3** 起動時の状態復元 | アプリ再起動後、kabu 上の建玉と本ブリッジの建玉が一致する | `BrokerSessionInitializerService` Step 3 で `SyncToActiveSet` |
| **NF-R4** 通信失敗時の安全側挙動 | `NetworkError` 時は発注通った可能性を残し、勝手に取消扱いしない | 拘束は解放しない・終端状態を `Rejected` ではなく検証可能な状態にする |
| **NF-R5** ログの完全性 | 全発注経路で「いつ・誰が・何を・結果は何か」が再現できる | Serilog 構造化ログ + 日次ローテーション 7 日保持 |

### 6.2 パフォーマンス

| 要件 | 内容 | 目安 |
|---|---|---|
| **NF-P1** Webhook 受信から発注までの遅延 | 平均 1 秒以内 | ローカル localhost → kabu localhost の往復のみ。実測 200ms 前後 |
| **NF-P2** ポーリング負荷 | pending が空のとき kabu API を呼ばない | `IPendingOrderTracker.IsEmpty` で即 return |
| **NF-P3** UI 応答 | 約定通知から表示更新まで 1 秒以内 | Dispatcher 経由で UI スレッドに即時マーシャリング |
| **NF-P4** 価格ティック処理 | WebSocket 受信から UI 更新まで 100ms 以内 | 1 push = 1 イベント発火、O(1) 更新 |

### 6.3 セキュリティ

| 要件 | 内容 |
|---|---|
| **NF-S1** パスワード暗号化 | 永続化される全パスワードは DPAPI で暗号化 (`enc:` プレフィックス) |
| **NF-S2** ログのマスキング | `KabuApiClient.SendOrderAsync` のリクエストログでは `Password` を `***` に置換 |
| **NF-S3** 外部公開禁止 | Webhook listener は `localhost` のみバインド。外部公開は Cloudflare Tunnel 経由のみ |
| **NF-S4** Webhook 認証 | `Passphrase` フィールドの完全一致で認証 (空の場合は警告のうえ素通し) |
| **NF-S5** kabu 取引パスワード | `OrderPassword` は注文 1 回ごとに body 同梱、ログには出さない |

### 6.4 可用性

| 要件 | 内容 |
|---|---|
| **NF-A1** kabu 未接続でも UI 起動 | 各 Step は例外捕捉して継続 |
| **NF-A2** Webhook listener 単独障害許容 | listener が落ちても UI / kabu 経路は生存 |
| **NF-A3** 自動再接続 | WebSocket は接続失敗時 5 秒待機 → 再試行 |
| **NF-A4** トークン自動リフレッシュ | 401 応答時に 1 度だけ自動 Refresh + リトライ |

### 6.5 保守性

| 要件 | 内容 |
|---|---|
| **NF-M1** レイヤー責務分離 | Domain は他の層に依存しない。Application は Domain のみ依存 |
| **NF-M2** ユビキタス言語の一貫性 | 用語辞書 ([ubiquitous-language.md](./ubiquitous-language.md)) と Coding 名が一致 |
| **NF-M3** テスト容易性 | 全 UseCase は `FakeBrokerAdapter` で単体テスト可能 |
| **NF-M4** 設定外出し | URL / Port / タイムアウト等は `appsettings.json` で変更可 |
| **NF-M5** デモモードによる UI 単独デバッグ | `--demo` で外部依存なしで UI を起動できる |

### 6.6 運用性

| 要件 | 内容 |
|---|---|
| **NF-O1** 設定変更のホット反映 | 設定ダイアログでの保存後、保存時刻のみ表示。再起動不要な項目と要再起動の項目を明示 |
| **NF-O2** ログの分離 | コンソール / ファイル / UI で同じ内容を出力 |
| **NF-O3** トラブルシュート手順 | `troubleshooting.md` / `dev-rules.md` を参照可能な状態に維持 |
| **NF-O4** 起動・停止手順の標準化 | ダッシュボード B (`n225_brokerbridge_dashboard.py`) の「起動 / 停止」ボタン経由を公式手順とする |

---

## 7. 制約 (Constraints)

| 種別 | 制約 |
|---|---|
| **言語** | C# / .NET 8 LTS |
| **OS** | Windows 10 / 11 (DPAPI が使えること) |
| **UI フレームワーク** | WPF + [Wpf.Ui 4.x](https://github.com/lepoco/wpfui) |
| **ブローカー** | au カブコム証券 kabu Station (Day 1 唯一) |
| **トンネル** | Cloudflare Tunnel (`cloudflared`) — TradingView から自宅 PC への HTTPS 経路 |
| **ライセンス** | OSS / 個人利用 (本ブリッジは商用配布前提ではない) |
| **データ永続化** | JSON ファイル (`%LOCALAPPDATA%/N225BrokerBridge/`) — SQLite 等への移行は将来課題 |
| **時刻** | すべて UTC で保持し、表示時に JST に変換 (kabu API の `DateTimeOffset` は元から JST) |
| **市場時間** | 06:00–15:45 JST = 日中 (Exchange=23) / その他 = 夜間 (Exchange=24)。発注時に動的判定 |

---

## 8. ユースケース一覧 (要約)

| 番号 | アクター | ユースケース | 主シナリオ |
|---|---|---|---|
| UC-01 | TradingView | 新規発注シグナル受信 | F-1 → F-2 → F-3 → F-4 → F-5 → F-10 |
| UC-02 | TradingView | 返済シグナル受信 | F-1 → F-2 → F-3 → F-4 → F-6 → F-10 |
| UC-03 | TradingView | ドテンシグナル受信 | F-1 → F-2 → F-3 → F-4 → F-8 → F-10 |
| UC-04 | ユーザー | 手動買発注 | F-15 → F-5 → F-10 |
| UC-05 | ユーザー | 手動売発注 | F-15 → F-5 → F-10 |
| UC-06 | ユーザー | 手動返済 | F-15 → F-7 → F-10 |
| UC-07 | ユーザー | 注文キャンセル | F-15 → F-9 |
| UC-08 | ユーザー | 自動売買 ON/OFF | F-15 → F-3 |
| UC-09 | ユーザー | 戦略 CRUD | F-15 → F-4 |
| UC-10 | ユーザー | 設定変更 | F-15 → F-17 |
| UC-11 | システム | 起動時状態復元 | F-11 |
| UC-12 | システム | 注文ポーリング | F-12 |
| UC-13 | システム | 価格ティック受信 | F-13 |
| UC-14 | システム | 限月解決 | F-14 |
| UC-15 | ユーザー (撮影) | デモモード起動 | F-16 |

詳細フローは [sequence-diagrams.md](./sequence-diagrams.md) 参照。

---

## 9. 受入基準 (Acceptance)

本ブリッジが「要件を満たしている」と判定する条件:

1. ✅ 単体テスト 176 件以上が PASS (現状: PASS)
2. ✅ Webhook → kabu 発注のエンドツーエンド検証 Stage 1〜2 が PASS (2026-05-21 完了)
3. ✅ 起動時の建玉同期で kabu と本ブリッジが一致する
4. ✅ 自動売買 OFF 状態でシグナル受信 → 発注されないことが目視確認できる
5. ✅ デモモード起動で外部接続が一切発生しない (ログ確認)
6. ✅ 6.3 のセキュリティ要件 (DPAPI / ログマスク / localhost バインド) を満たしている
7. ⚠️ 現時点で残存する課題は `roadmap.md` を参照

---

## 10. このドキュメントの更新ルール

- 新機能追加時: 該当する F-NN 番号を追加。番号は欠番でも使い回さない
- 非機能要件変更時: NF-X1 等の番号を維持して内容を更新
- スコープ変更時: §4 を更新し `changelog.md` に 1 行追記
- メジャー変更時 (DDD 構造変更等): バージョンを +1.0 し、変更点を `## 変更履歴` に記載

---

## 変更履歴

| バージョン | 日付 | 変更内容 |
|---|---|---|
| 1.0.0 | 2026-05-26 | 初版作成 (旧 OrderBridge からのリライト要件を逆引きで文書化) |
