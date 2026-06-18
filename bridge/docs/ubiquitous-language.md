# ユビキタス言語辞書 (Ubiquitous Language Glossary)

## このドキュメントの目的

**ユビキタス言語 (Ubiquitous Language)** は、ドメイン駆動設計 (DDD) の中核概念のひとつ。
ドメイン専門家 (本プロジェクトではユーザー) と開発者 (Claude + ユーザー) が **同じ言葉** で会話し、
**同じ言葉でコードを書く** ことで、翻訳ロスや解釈のズレを防ぐ。

- 仕様書・会話・コード・テスト・UI・ログ、**全てで同じ語彙を使う**
- 名前が変わったら全箇所で同期する
- 新しい概念が出たらこの辞書に追加する
- 既存の似た用語と被ったら統合・整理する

## 現 N225OrderBridge との関係

現行 C# コードには typo (`Contrct`, `Iterval`, `Pssword`, `KubuAPIs`, `Infrastrucure`) が散在している。
新プロジェクトでは **typo を撲滅** し、正しいドメイン用語に統一する。
本辞書は「現コードでの呼称 → 新ドメイン用語」のマッピングも兼ねる。

---

## 1. 取引・注文に関する用語

### 1.1 注文 (Order)

| 用語 | 英語表記 | 定義 |
|------|---------|------|
| 注文 | Order | 売買意思を市場に提示する単位。1 注文は 1 ブローカーに対し 1 アクション。 |
| 新規注文 | New Order / Entry Order | 建玉を新規に持つ注文。 |
| 返済注文 | Exit Order / Close Order | 既存建玉を反対売買で決済する注文。 |
| 部分返済 | Partial Close | 既存建玉の一部だけを返済する注文。 |
| ドテン注文 | Doten Order / Reversal Order | 既存建玉を返済して即座に反対方向の新規建玉を持つ注文。日本語独自概念。 |
| 一括注文 | Bulk Order | 複数枚を 1 度に発注する注文 (ピラミッディングではない)。 |
| 注文 ID | Order ID | ブローカーが採番する注文識別子。`OrderID`。 |
| 注文ライフサイクル | Order Lifecycle | 受付 → 送信 → 部分約定 → 約定完了 / 取消 / 失効 の状態遷移。 |

### 1.2 約定 (Execution)

| 用語 | 英語表記 | 定義 |
|------|---------|------|
| 約定 | Execution / Fill | 注文が市場で成立すること。 |
| 全量約定 | Full Fill | 注文枚数すべてが 1 度に約定すること。 |
| 分割約定 | Split Execution / Partial Fill | 1 注文が複数の約定 (fill) に分かれて成立すること。 |
| 約定 ID | Execution ID | ブローカーが各約定 (fill) に採番する識別子。`ExecutionID`。同じ意味で kabu の `HoldID` が建玉識別子として使われる。 |
| 約定価格 | Execution Price / Fill Price | 約定した価格。 |
| 約定枚数 | Execution Quantity / Fill Quantity | その約定で成立した枚数。 |
| 約定通知 | Execution Notification / Fill Event | 約定発生時にブローカーから配信されるイベント。 |

### 1.3 建玉 (Position)

| 用語 | 英語表記 | 定義 |
|------|---------|------|
| 建玉 | Position | 注文約定によって保有している銘柄の状態。 |
| ロット | Lot | 1 約定で発生した建玉の最小単位。分割約定なら 1 注文 = 複数ロット。 |
| 残数量 | Leave Quantity (`LeaveQty`) | 建玉のうち、まだ返済されていない枚数。UI 列「保有」に対応。kabu API では `LeavesQty` (s 付き)。 |
| 拘束数量 | Hold Quantity (`HoldQty`) | 返済注文中で動かせない枚数。UI 列「注文」に対応。 |
| 約定累計 | Cumulative Quantity (`CumQty`) | 1 注文に対し累計で約定した枚数。 |
| 建玉サイド | Position Side | 買建 (Long) または 売建 (Short)。 |
| 平均建値 | Average Entry Price | 建玉の平均取得価格。 |
| 含み損益 | Unrealized PnL | 現在価格と平均建値の差による評価損益。 |
| 実現損益 | Realized PnL | 返済によって確定した損益。 |

### 1.4 注文タイプ (Order Type)

| 用語 | 英語表記 | 定義 |
|------|---------|------|
| 成行注文 | Market Order | 価格を指定せず即座に約定させる注文。 |
| 指値注文 | Limit Order | 指定価格以下 (買) / 以上 (売) でのみ約定する注文。 |
| 逆指値注文 | Stop Order | 指定価格に達したらトリガーされる注文。ストップロスに使用。 |
| 最良執行注文 | Best Market Order | kabu API の `SelectedOrder=1` 相当、最良気配で発注。 |

### 1.5 時間条件 (Time In Force, TIF)

| 用語 | 英語表記 | 定義 |
|------|---------|------|
| FAK | Fill And Kill | 約定可能分だけ約定、残りは取消。 |
| FAS | Fill And Store | 約定可能分は約定、残りは指値として板に残す。 |
| FOK | Fill Or Kill | 全量約定できなければ全取消。 |

### 1.6 サイド (Side)

| 用語 | 英語表記 | 定義 |
|------|---------|------|
| 買 | Buy / Long | 買付け方向。 |
| 売 | Sell / Short | 売付け方向。 |

---

## 2. ブローカーに関する用語

### 2.1 ブローカー (Broker)

| 用語 | 英語表記 | 定義 |
|------|---------|------|
| ブローカー | Broker | 証券会社・取引仲介業者。 |
| ブローカーアダプタ | Broker Adapter | 各ブローカー固有 API をドメインから隔離する変換層。 |
| 統一注文インターフェース | Unified Order Interface | 全ブローカーが従う共通契約 (Domain で定義)。 |

### 2.2 個別ブローカー

| 用語 | 英語表記 | 定義 |
|------|---------|------|
| kabu | kabuステーション (au カブコム証券) | REST API + WebSocket、`localhost:18080` 経由。現行 N225OrderBridge で使用中。 |
| 楽天 RSS | Rakuten RSS (楽天証券) | Excel RTD / COM 経由でデータ受信および注文発注 (発注対応詳細はユーザー要確認)。 |
| HoldID | (kabu 用語) | kabu API における建玉識別子。本ドメインでは `ExecutionID` と統合される概念。 |

### 2.3 認証・接続

| 用語 | 英語表記 | 定義 |
|------|---------|------|
| API キー | API Key (`X-API-KEY`) | kabu API 等の認証ヘッダー。 |
| API トークン | API Token | ブローカーから取得する短期認証トークン。 |
| パスフレーズ | Passphrase | Webhook ペイロード認証用の共有秘密。 |

---

## 3. シグナル・戦略に関する用語

### 3.1 シグナル (Signal)

| 用語 | 英語表記 | 定義 |
|------|---------|------|
| シグナル | Signal | 売買判断の発火イベント。 |
| アラート | Alert | TradingView 等のシグナル送信機構。`alert_name` で識別。 |
| Webhook シグナル | Webhook Signal | HTTP POST で受信するシグナル。 |
| インターバル | Interval | シグナル発生時の時間足 (1m, 5m, 60m 等)。 |
| 自動/手動フラグ | Trade Mode | 1=自動取引、0=手動取引。`TradeMode`。 |
| シグナル受信 | Signal Reception | Webhook 等を受け取って解釈する処理。 |
| シグナル採用 | Signal Acceptance | 戦略がアクティブで、フィルター条件を満たしたシグナルを実行に回すこと。 |
| シグナル棄却 | Signal Rejection | フィルター条件を満たさず実行しないシグナル。 |

### 3.2 戦略 (Strategy)

| 用語 | 英語表記 | 定義 |
|------|---------|------|
| 戦略 | Strategy | シグナルを生成する売買ロジックの単位。 |
| 戦略 ID | Strategy ID / Strategy Name | `alert_name` で識別される戦略の名前。 |
| アクティブ戦略 | Active Strategy | 現在シグナルを採用する設定の戦略。 |
| 戦略一覧 | Strategy List | UI 上で管理される戦略の一覧。チェックボックスでアクティブ/非アクティブを切替。 |

### 3.3 ポジション遷移 (Market Position Transition)

TradingView 戦略のペイロードに含まれる遷移状態。新規・返済・ドテンの判定に使う。

| 遷移パターン | 意味 | 注文タイプ |
|------------|------|----------|
| flat → long | 新規買建 | 新規買い注文 (`AutoBuyNewOrder`) |
| flat → short | 新規売建 | 新規売り注文 (`AutoSellNewOrder`) |
| long → flat | 買建全返済 | 売り返済注文 (`AutoSellExitOrder`) |
| short → flat | 売建全返済 | 買い返済注文 (`AutoBuyExitOrder`) |
| long → long (qty減) | 買建部分返済 | 売り返済注文 |
| short → short (qty減) | 売建部分返済 | 買い返済注文 |
| long → short | 売りドテン | 売りドテン注文 (`AutoDotenSellOrder`) = 買建返済 + 新規売建 |
| short → long | 買いドテン | 買いドテン注文 (`AutoDotenBuyOrder`) = 売建返済 + 新規買建 |

---

## 4. 銘柄・市場に関する用語

| 用語 | 英語表記 | 定義 |
|------|---------|------|
| 銘柄 | Symbol | 取引対象。日経225ミニは `OSE:NK225M1!` (TV) / `167060019` (kabu) 等の銘柄コードで表現。 |
| 銘柄コード | Symbol Code | ブローカー固有の銘柄識別子。 |
| 限月 | Contract Month / Kengetsu | 先物の決済月。SQ で決済される。 |
| SQ | Special Quotation | 特別清算指数。先物オプションの最終決済価格。 |
| ロット倍率 | Multiplier | 1 枚あたりの想定元本倍率。日経225ミニは 100 倍。 |
| 気配 | Quote / Board | 板情報 (BID/ASK 等)。 |
| 取引所 | Exchange | OSE (大阪取引所) 等。 |

---

## 5. アーキテクチャ用語 (DDD)

### 5.1 戦術的設計パターン

| 用語 | 英語表記 | 定義 | 本プロジェクトでの例 |
|------|---------|------|------------------|
| 値オブジェクト | Value Object | 同一性ではなく値で等価判定するオブジェクト。不変。 | `Price`, `Quantity`, `SymbolCode`, `Side` |
| エンティティ | Entity | 一意な識別子を持ち、属性が変わっても同一性が保たれるオブジェクト。 | `Order`, `Position`, `Strategy` |
| 集約 | Aggregate | 関連するエンティティ・値オブジェクトの一貫性境界。1 集約 = 1 トランザクション境界。 | `OrderAggregate`, `PositionAggregate` |
| 集約ルート | Aggregate Root | 集約の入口となるエンティティ。外部からはルート経由でしかアクセスできない。 | `Order`, `Position` |
| リポジトリ | Repository | 集約の永続化・取得を担う。コレクション的インターフェース。 | `IOrderRepository`, `IPositionRepository` |
| ドメインサービス | Domain Service | 特定のエンティティに属さないドメインロジック。 | `PositionMatcher` (どの建玉から返済するか判定) |
| ドメインイベント | Domain Event | ドメイン内で発生した事実。発火後に他コンテキストへ伝播。 | `OrderSubmitted`, `OrderExecuted`, `PositionClosed` |
| アプリケーションサービス | Application Service | ユースケースを 1 つ実行する。Domain を組み合わせる薄い層。 | `SignalHandler` (シグナル受信→注文発注のフロー) |
| ファクトリ | Factory | 複雑な集約の生成を担う。 | `OrderFactory` |

### 5.2 戦略的設計パターン

| 用語 | 英語表記 | 定義 |
|------|---------|------|
| 境界づけられたコンテキスト | Bounded Context | 用語と意味が一貫している範囲。コンテキストが違えば同じ単語でも意味が違う。 |
| コンテキストマップ | Context Map | コンテキスト間の関係を示した図。 |
| ユビキタス言語 | Ubiquitous Language | ドメインで使われる共通語彙。本ドキュメントそのもの。 |

### 5.3 本プロジェクトの境界づけられたコンテキスト

| コンテキスト | 責務 |
|------------|------|
| シグナル受信 (Signal Reception) | Webhook / 内部シグナルの受信・パース・認証 |
| 注文管理 (Order Management) | 注文ライフサイクルの管理 |
| 建玉管理 (Position Management) | 建玉の保持・更新・整合性 |
| ブローカー統合 (Broker Integration) | 各社 API の抽象化と実装 |
| 戦略管理 (Strategy Management) | 戦略の登録・アクティブ管理 (※ Dashboard 側で実施) |
| 監査・記録 (Audit / Logging) | 取引記録・操作ログ |

---

## 6. 現 N225OrderBridge からの命名整理

### typo・違和感の修正一覧

| 現行 (typo / 違和感) | 新ドメイン用語 | 備考 |
|--------------------|--------------|------|
| `Contrct` (PositionManager メソッド) | `OnExecuted` または `ApplyExecution` | typo + より意図明確 |
| `Iterval` (PositionListEntity) | `Interval` | typo |
| `Pssword` (SendOrderEntity) | `Password` | typo |
| `KubuAPIs` (フォルダ) | `Brokers/Kabu` | typo + 多社対応の配置 |
| `Infrastrucure` (プロジェクト名) | `Infrastructure` | typo |
| `Xepired` (Cancel/Revocation を集約) | `OnOrderTerminated` | より明確 |
| `OnoderEntity` (旧ファイル名) | `OrderEntity` | typo |
| `OrederManagerEntity` (旧) | `OrderManagerEntity` | typo |
| `AutoOrderfiled` (Filed = Field の typo) | `AutoOrderField` または `OrderRequestBuilder` | typo |
| `DictionaryAddHoldQty` (実装は上書き) | `SetHoldQty` | 命名と実装の不一致を解消 |

### kabu API ↔ ドメイン用語

| kabu API | ドメイン用語 |
|---------|------------|
| `LeavesQty` | `LeaveQuantity` / `RemainingQuantity` |
| `HoldQty` | `HoldQuantity` / `LockedQuantity` |
| `CumQty` | `CumulativeQuantity` |
| `OrderQty` | `OrderQuantity` |
| `HoldID` | `ExecutionId` (建玉識別子) |
| `OrderId` | `OrderId` |
| `RecType=8` | `ExecutionDetailRecord` (約定明細レコード) |
| `CashMargin=2` | `MarginNew` (新規建て) |
| `CashMargin=3` | `MarginExit` (返済) |

---

## 7. 用語追加・変更の運用ルール

- **新しい用語が出たら**: 該当セクションに追加し、Git でコミット。コミットメッセージは `docs(ulang): add "○○"`
- **名前を変えるとき**: 影響範囲 (コード・テスト・ログ・UI・他ドキュメント) を全て同期。本辞書の改名は **コード改名と同時にコミット**。
- **似た用語が出たとき**: どちらかに統合し、片方を「同義語 / Deprecated」として注記。
- **会話で別の用語を使ったとき**: 「これは辞書の `○○` ですよね」と確認するクセをつける。

## 改版履歴

| 日付 | バージョン | 内容 |
|------|----------|------|
| 2026-05-17 | v1.0 | 初版起草。現 N225OrderBridge からの用語抽出・整理。 |
