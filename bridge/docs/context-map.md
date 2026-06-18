# 境界づけられたコンテキストとコンテキストマップ

## このドキュメントの目的

**境界づけられたコンテキスト (Bounded Context)** は DDD の戦略的設計の中核概念。
システムを「**用語と意味が一貫している範囲**」で区切ることで、巨大化を防ぎ、各部分の自律性を確保する。

**コンテキストマップ (Context Map)** は、コンテキスト同士の **関係 (依存方向・結合の強さ)** を可視化する。

## なぜコンテキストを分けるのか

例: 「注文 (Order)」という言葉は、
- **シグナル受信コンテキスト** では「シグナル発火による注文意思」
- **注文管理コンテキスト** では「ブローカーに送信する注文ライフサイクル」
- **ブローカー統合コンテキスト** では「kabu API の `/sendorder/future` ペイロード」

──同じ言葉でも、コンテキストが違えば意味と関心事が違う。
これを 1 つのクラスでカバーしようとすると神クラス化して保守不能になる。
コンテキストを切り、**境界で変換 (Translation)** することで、各コンテキストは自分の語彙で純粋な設計ができる。

---

## 本プロジェクトの境界づけられたコンテキスト

### 1. シグナル受信コンテキスト (Signal Reception Context)

**責務**: 外部からの売買シグナルを受信・認証・パースし、ドメインイベントとして発行する。

- **入力**:
  - TradingView Webhook (HTTP POST, JSON)
  - 将来: 内部戦略エンジンからの直接シグナル
- **出力**: `SignalReceived` ドメインイベント
- **主要概念**: `Signal`, `Alert`, `Passphrase`, `WebhookEndpoint`
- **典型ロジック**: passphrase 認証、ペイロード妥当性検査、`market_position` 遷移の解釈

### 2. 注文管理コンテキスト (Order Management Context)

**責務**: 注文ライフサイクル全体を管理する。

- **入力**: `SignalReceived` イベント → 注文意思に変換
- **出力**:
  - ブローカー統合コンテキストへ `PlaceOrderCommand`
  - `OrderSubmitted`, `OrderExecuted`, `OrderCancelled` イベント
- **集約**: `Order` (Aggregate Root) — `OrderLine`, `Execution` を内包
- **状態遷移**: `Created` → `Submitted` → `PartiallyFilled` → `Filled` / `Cancelled` / `Expired`
- **不変条件**: 「注文枚数 = 約定累計 + 残未約定」

### 3. 建玉管理コンテキスト (Position Management Context)

**責務**: 現在保有している建玉の状態管理と整合性確保。

- **入力**: `OrderExecuted` イベント (新規 → 建玉追加 / 返済 → 建玉減算)
- **出力**: `PositionUpdated`, `PositionClosed` イベント
- **集約**: `Position` (Aggregate Root) — `Lot` (約定単位) を内包
- **主要ロジック**:
  - 分割約定の集約 (1 注文 = 複数 Lot)
  - 部分返済時の建玉選択 (PositionMatcher ドメインサービス)
  - 残数量・拘束数量の管理
- **不変条件**: 「LeaveQuantity = Σ Lot.LeaveQuantity ≥ 0」

### 4. ブローカー統合コンテキスト (Broker Integration Context)

**責務**: 各証券会社の固有 API をドメインから隔離し、統一インターフェースで提供する。

- **入力**: `PlaceOrderCommand`, `ClosePositionCommand`, `QueryCommand`
- **出力**: ブローカーからの応答を `OrderResult`, `ExecutionEvent` 等のドメイン用語に変換
- **主要概念**: `IBrokerAdapter`, `KabuAdapter`, `RakutenAdapter`
- **典型ロジック**: REST/WebSocket/COM 通信、認証、レート制限、再接続
- **設計原則**: 各 adapter は **自己完結** (kabu の特殊事情は kabu adapter 内に閉じる)

### 5. 戦略管理コンテキスト (Strategy Management Context) **(本プロジェクト外 / Dashboard 担当)**

**責務**: 戦略 (Strategy) の登録・アクティブ管理・パラメータ調整。

- **本プロジェクトでは扱わない**。N225 Dashboard (Python) 側で管理する。
- **本プロジェクトとの接点**:
  - Dashboard が出力する「アクティブ戦略リスト」を読み込み、シグナル受信時のフィルタに使用
  - 接点形式は **共有データベース** または **REST API** (要設計)

### 6. 監査・記録コンテキスト (Audit / Logging Context) **(横断的)**

**責務**: 全コンテキストから発生するイベント・操作を時系列で記録する。

- **入力**: 全ドメインイベント、UI 操作ログ、ブローカー通信ログ
- **出力**: 構造化ログファイル (Serilog 経由)、将来的にイベントストア
- **設計原則**: 他コンテキストから **読まれない** (純粋な書き込み専用)

### 7. アカウント管理コンテキスト (Account Management Context) **(横断的)**

**責務**: ブローカー口座の残高・証拠金・建玉余力の照会。

- **入力**: ブローカー統合コンテキスト経由で取得
- **出力**: `AccountSnapshot` (残高・余力・拘束金)
- **用途**: 注文発注前の余力チェック、UI 表示

---

## コンテキストマップ (関係図)

```
                              [外部システム]
                                    │
                       TradingView Webhook 等
                                    │
                                    ▼
                  ┌──────────────────────────────┐
                  │  シグナル受信コンテキスト       │
                  │  (Signal Reception)          │
                  └──────────────┬───────────────┘
                                 │ SignalReceived event
                                 ▼
                  ┌──────────────────────────────┐
                  │  注文管理コンテキスト          │
                  │  (Order Management)          │
                  └──────────────┬───────────────┘
                                 │ PlaceOrderCommand
                                 ▼
                  ┌──────────────────────────────┐
                  │  ブローカー統合コンテキスト    │
                  │  (Broker Integration)        │
                  │  ┌─────────────────────┐     │
                  │  │ IBrokerAdapter      │     │
                  │  └─────────┬───────────┘     │
                  │            │                 │
                  │  ┌─────────▼─────┐ ┌───────┐ │
                  │  │ KabuAdapter   │ │Rakuten│ │
                  │  └───────┬───────┘ └───┬───┘ │
                  └──────────┼─────────────┼─────┘
                             │             │
                          [kabu API]  [楽天 RSS]
                             │             │
                             ▼             ▼
                       ExecutionEvent (約定通知)
                                 │
                                 ▼
                  ┌──────────────────────────────┐
                  │  建玉管理コンテキスト          │
                  │  (Position Management)       │
                  └──────────────┬───────────────┘
                                 │ PositionUpdated event
                                 ▼
                       (UI / Audit 等で購読)

   横断:  ──────────────────────────────────────────────────
   ┌─────────────────────┐     ┌─────────────────────┐
   │ 監査・記録          │     │ アカウント管理       │
   │ (Audit / Logging)   │     │ (Account Management)│
   └─────────────────────┘     └─────────────────────┘

   外部:  ──────────────────────────────────────────────────
   ┌─────────────────────────────────────────────────┐
   │ 戦略管理コンテキスト (Dashboard 側 / Python)     │
   │ → 共有 DB or REST 経由でアクティブ戦略リスト提供 │
   └─────────────────────────────────────────────────┘
```

---

## コンテキスト間の関係パターン (DDD 標準分類)

### 凡例

- **Customer-Supplier (CS)**: 下流が上流に依存。上流が API を提供。下流の要求を上流が考慮する。
- **Conformist (CF)**: 下流が上流のモデルにそのまま従う (要求はしない)。
- **Anti-Corruption Layer (ACL)**: 下流が上流の影響から自身のモデルを守るための変換層。
- **Shared Kernel (SK)**: 2 つのコンテキストで共有するモデル (慎重に管理)。
- **Open-Host Service (OHS)**: 公開された API として提供。
- **Published Language (PL)**: 公開された言語仕様 (JSON スキーマ等)。

### 本プロジェクトの関係

| 上流 → 下流 | 関係 | 説明 |
|------------|------|------|
| 外部 (TV) → シグナル受信 | **ACL** | TV ペイロード JSON を内部ドメインイベントに変換し、外部仕様変更の影響を遮断 |
| シグナル受信 → 注文管理 | **CS (Published Language)** | `SignalReceived` イベントを公開言語として、注文管理が購読 |
| 注文管理 → ブローカー統合 | **CS** | 注文管理は統一インターフェース (`IBrokerAdapter`) に依存 |
| ブローカー統合 → 個別 API (kabu, 楽天) | **ACL** | 各 adapter が固有 API を統一インターフェースに変換 |
| ブローカー統合 → 建玉管理 | **CS (Event)** | 約定イベント (`ExecutionEvent`) を建玉管理が購読 |
| Dashboard (Python) → シグナル受信 | **CF or CS** | アクティブ戦略リスト提供。連携方式は要設計 |
| 全コンテキスト → 監査 | **CS (Event)** | 全イベントを監査が購読 (書き込み専用) |

---

## 物理配置とコンテキスト境界の関係

DDD のコンテキストはプロジェクト構造に必ずしも 1:1 で対応する必要はない。
本プロジェクトでは以下の対応を採用:

| コンテキスト | C# プロジェクト | フォルダ例 |
|------------|---------------|----------|
| シグナル受信 | `N225BrokerBridge.Domain` + `N225BrokerBridge.Infrastructure` | `Domain/Signals/`, `Infrastructure/Webhooks/` |
| 注文管理 | `N225BrokerBridge.Domain` + `N225BrokerBridge.Application` | `Domain/Orders/`, `Application/Orders/` |
| 建玉管理 | `N225BrokerBridge.Domain` + `N225BrokerBridge.Application` | `Domain/Positions/`, `Application/Positions/` |
| ブローカー統合 | `N225BrokerBridge.Domain` (インターフェース) + `N225BrokerBridge.Infrastructure` (実装) | `Domain/Brokers/`, `Infrastructure/Brokers/Kabu/`, `Infrastructure/Brokers/Rakuten/` |
| 監査・記録 | `N225BrokerBridge.Infrastructure` | `Infrastructure/Auditing/` |
| アカウント管理 | `N225BrokerBridge.Domain` + `N225BrokerBridge.Infrastructure` | `Domain/Accounts/`, `Infrastructure/Accounts/` |

---

## 改版履歴

| 日付 | バージョン | 内容 |
|------|----------|------|
| 2026-05-17 | v1.0 | 初版。6 コンテキスト識別、ASCII 図でマップ提示。 |
