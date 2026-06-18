# ポジション履歴 (Position History) — 追加仕様書 + 詳細設計

**バージョン**: 1.0.0 (実装完了)
**作成日**: 2026-06-02
**最終更新**: 2026-06-02
**ステータス**: **実装完了・public push 済み (commit c25faf6)**。表示はメニュー「表示→ポジション履歴」から起動。Application 60 + Infrastructure 31 テスト PASS。**バックフィル (§4-7) は未実装** (今夜以降の実取引から記録する方針のため見送り)。実機目視確認は実データ発生後に実施予定。
**目的**: 自動売買 / 手動取引の **決済済み実現損益を 1 か所に集約・永続化** し、専用画面で一覧表示する。スクリーンショットして **実績 (track record) として公開** できる状態を作る。

関連ドキュメント:
- [data-spec.md](./data-spec.md) — 永続化ファイル一覧 (本仕様で `position-history.json` を追加)
- [domain-model.md](./domain-model.md) — Position 集約
- [class-design.md](./class-design.md) §4.2 — 永続化クラス
- [sequence-diagrams.md](./sequence-diagrams.md) — 約定 → 建玉 → 返済フロー
- [mainwindow-layout.md](./mainwindow-layout.md) — UI レイアウト

---

## 1. 背景と動機

### 1-1. なぜ必要か
戦略の実績を公開するには「いつ・どの戦略で・いくら勝った/負けた」の**決済履歴**が要る。
ところが現状、**実現損益はどこにも永続化されていない**:

| データ | 現状 | 実現損益 |
|---|---|---|
| `auto-positions.json` | **オープン中の自動建玉のみ**。決済で `RemoveAsync`/`SyncToActiveSetAsync` により**削除される** | × 残らない |
| `orders-metadata.json` | 注文の `tradeMode`(Auto/Manual)・戦略・新規/返済・`targetExecutionId` を**履歴的に保持** | × 価格なし |
| `logs/*.log` | 「約定検出 … 価格=66440」で約定価格は残る | △ 7 ファイル保持・要突合 |
| UI ライブ損益 | kabu 現在値からの**含み損益をライブ計算**のみ | × 永続化なし |

→ **決済時の実現損益を専用ストアに追記保存** し、画面表示する仕組みを新設する。
これは旧 kabu Station / 旧 N225OrderBridge の「ポジション」CSV (取引結果が全件残る) に相当する位置づけ。

### 1-2. 設計の前提となる運用ルール (重要)
本機能の紐付け正当性は、以下 2 つの運用ルールに依存する:

- **条件 A — 戦略は互いに区別可能であること**: 同時運用する複数戦略は **戦略名 (`alert_name`) または `interval` が異なる**こと。決済候補は `(BrokerCode, Strategy, Interval, TradeMode, Side)` で絞り込まれるため (`ClosePositionUseCase`)、これが満たされれば 3 戦略が同一銘柄を同時に持っても建玉は混線しない。
- **条件 B — 自動建玉を手動決済しないこと**: kabu Station GUI 等でブリッジ外から自動建玉を決済すると、ブリッジ台帳と実口座がズレて履歴が壊れる。自動建玉の決済はブリッジ (TV シグナル) 経由のみとする。

---

## 2. 要件

### 2-1. 機能要件
- **FR-1**: 建玉が決済された (部分決済含む) 各約定ごとに、実現損益を 1 レコード記録する。
- **FR-2**: レコードは**追記型 (削除しない)** で `position-history.json` に永続化する。
- **FR-3**: 専用画面「ポジション履歴」で、**建玉単位の親行 + 分割決済の子行**の 2 階層で表示する。
- **FR-4**: モード (自動のみ/手動のみ/全部)・期間 (当日/今週/今月/全期間)・戦略 でフィルタできる。
- **FR-5**: フィルタ反映のサマリー (合計損益・取引数・勝率・平均損益・累積損益) を表示する。
- **FR-6**: 既存の `orders-metadata.json` + ログから過去分を**一度だけバックフィル**できる (任意・初回シード)。

### 2-2. 非機能要件
- **NFR-1 (追加のみ)**: 既存の決済ロジック (`Position.ApplyClosure` / `ExecutionApplier`) の挙動を変えない。**記録という副作用を足すだけ**。
- **NFR-2 (ログ非依存)**: 通常運用ではログ突合に依存せず、決済イベント時点で価格込みで自前記録する (ログ突合はバックフィル時のみ)。
- **NFR-3 (層の独立)**: ドメイン層は永続化に依存しない。記録はアプリ層、保存はインフラ層。
- **NFR-4 (公開安全)**: 証券口座番号・残高等は記録も表示もしない。出すのは戦略・価格・枚数・損益のみ。

---

## 3. 表示仕様 (確定版)

> TradingView の「トレード一覧」(`トレード番号 / タイプ / 日時 / シグナル / 価格 / サイズ / 純損益 / 最大順行幅 / 最大逆行幅 / 累積損益`) を **参考**にしつつ、本システム固有の **戦略名・モード**を主軸にする。MFE/MAE (最大順行/逆行幅) は建玉中のティック内データが必要で実運用ブリッジには残らないため**除外**。

### 3-1. 表示方法
- WPF DataGrid。**建玉グループの親行 + 行詳細 (RowDetails) の子グリッド**で 2 階層を表現。
- 親行を折りたたんだ状態 = **1 建玉 1 行** → そのままスクショで実績一覧になる。展開で分割決済の内訳。
- 既定ソート = 建玉日時の新しい順。
- 銘柄名は**画面ヘッダ**に表示 (全件マイクロ等のため行には出さない)。
- Wpf.Ui 準拠。**列ヘッダに Thumb を入れない** (白線バグ回避 / `mainwindow-layout.md` 既知事項)。

### 3-2. 親行 (建玉サマリ)

| 列 | 内容 | TV 対応 | 例 |
|---|---|---|---|
| 建玉番号 | 連番 (新しい順) | トレード番号 | 289 |
| タイプ | 買建 / 売建 | タイプ | 買建 |
| **モード** ★ | 自動 / 手動 | (なし) | 自動 |
| **戦略名** ★ | 建玉を生成した戦略 | シグナル | V7-7-3 |
| 建玉日時 | 新規約定時刻 | 日時 | 06/01 09:01 |
| 建値 | 取得価格 | 価格 | 66,440 |
| 枚数 | 建玉枚数 | サイズ | 3 |
| 実現損益 | 子決済の合計 (＋緑/−赤) | 純損益 | +2,400 |
| 累積損益 | フィルタ内の走算合計 | 累積損益 | +18,300 |

### 3-3. 子行 (分割決済明細)

| 列 | 内容 | 例 |
|---|---|---|
| 決済日時 | 返済約定時刻 | 09:30 |
| 決済シグナル | 返済の種別/シグナル | Long_exit_under |
| 返済値 | 返済価格 | 66,510 |
| 枚数 | 決済枚数 | 1 |
| 損益 | この決済の実現損益 | +700 |

### 3-4. フィルタ / サマリー
- フィルタ: モード `自動のみ / 手動 / 全部` ・ 期間 `当日 / 今週 / 今月 / 全期間` ・ 戦略 `全部 / 個別`
- サマリー (フィルタ反映): `合計実現損益 / 取引数(建玉単位) / 勝ち / 負け / 勝率 / 平均損益`
- **勝率は建玉単位**で数える (3 枚を 1 勝 1 敗 1 勝で決済 → 合計＋なら 1 勝とカウント)。

---

## 4. 詳細設計

### 4-1. 記録の最小単位とフック地点
決済は **`ExecutionApplier.CloseTargetPositionAsync`** (1 か所) で起こる。
`position.ApplyClosure(qty, occurredAt)` の**直後**に、建玉と約定の両方の情報が揃う:

| 取得元 | 値 |
|---|---|
| `position` | `Id`(=建玉=新規 ExecutionId)・`EntryPrice`・`Side`・`Strategy`・`Interval`・`TradeMode`・`Symbol`・`OpenedAt` |
| `execution` | `Price`(返済値)・`Quantity`(決済枚数)・`ExecutedAt`(返済時刻)・`ExecutionId`(返済約定)・`BrokerOrderId` |

`ApplyClosure` は**部分決済でも全決済でも 1 回ずつ呼ばれる**ため、ここで 1 レコード記録すれば、
**分割決済 (3 枚→1 枚ずつ) は自動的に 3 レコード**になり、すべて同じ `EntryExecutionId` を持つ → 表示側で建玉単位にグルーピングできる。

### 4-2. ドメイン / アプリ層の追加

```
N225BrokerBridge.Application.Sync
├─ ClosedTrade            (決済 1 件 = 履歴 1 レコードの DTO)
└─ IClosedTradeStore      (永続化抽象: AppendAsync / LoadAllAsync)
```

`ClosedTrade` フィールド:

| フィールド | 型 | 説明 |
|---|---|---|
| EntryExecutionId | string | 建玉キー (グルーピング軸) |
| ExitExecutionId | string | 返済約定 ID (一意・冪等キー) |
| BrokerCode | string | "kabu" 等 |
| Strategy | string | 戦略名 ("Manual" 含む) |
| Interval | int | 足 (分)。手動=0 |
| TradeMode | string | "Auto" / "Manual" |
| SymbolCode | string | 銘柄コード |
| Side | string | 建玉サイド "Buy"/"Sell" (買建/売建) |
| EntryPrice | decimal | 建値 |
| ExitPrice | decimal | 返済値 |
| Quantity | int | この決済の枚数 |
| ProfitMultiplier | int | 損益単価 (Micro=10/Mini=100/Large=1000) |
| RealizedPnl | decimal | 実現損益 (記録時に確定計算して保存) |
| OpenedAt | DateTime(UTC) | 建玉時刻 |
| ClosedAt | DateTime(UTC) | 返済時刻 |

> `RealizedPnl` を保存済みにすることで履歴ファイルを自己完結 (再計算不要・旧 CSV と同じ思想) にする。生価格も持つので後から検算可能。

### 4-3. 実現損益の計算式
```
direction = (Side == Buy) ? +1 : -1            // 買建は値上がりで利益、売建は逆
RealizedPnl = (ExitPrice - EntryPrice) * direction * Quantity * ProfitMultiplier
```
`ProfitMultiplier` は `InstrumentDefinition.ProfitMultiplier` (UI 層) が正本。
**層の独立 (NFR-3) のため**、この倍率解決をアプリ層から使えるよう
`IContractMultiplierResolver`(銘柄コード→倍率) を新設して DI 注入する
(現状 UI にしかない倍率表をアプリ層共有へ昇格。値は Large=1000 / Mini=100 / Micro=10)。

### 4-4. 永続化 (インフラ層)
```
N225BrokerBridge.Infrastructure.Persistence
└─ JsonClosedTradeStore : IClosedTradeStore
```
- 保存先: `%LOCALAPPDATA%/N225BrokerBridge/position-history.json`
- 形式: `List<ClosedTrade>` を `WriteIndented` + camelCase で出力 (既存ストアと同形式)。
- **追記型**: `AppendAsync` は `ExitExecutionId` をキーに upsert (冪等)。**削除メソッドは持たない** (履歴恒久保持)。
- スレッド安全: `SemaphoreSlim` で保存直列化 (`JsonOrderMetadataStore` と同方式)。
- 起動時 `Load()` で全件メモリ展開。

### 4-5. 記録フローの結線
`ExecutionApplier` に `IClosedTradeStore` と `IContractMultiplierResolver` を注入し、
`CloseTargetPositionAsync` 内 `ApplyClosure` 成功直後に `AppendAsync(ClosedTrade)` を呼ぶ。
**既存の建玉削除・部分更新ロジックは一切変更しない** (NFR-1)。

### 4-6. UI 層
```
N225BrokerBridge.UI
├─ Views/PositionHistoryWindow.xaml(.cs)   (別ウィンドウ)
├─ ViewModels/PositionHistoryViewModel.cs
└─ ViewModels/ClosedTradeRow.cs            (子行 1 件)
```
- MainWindow に「ポジション履歴」ボタンを 1 つ追加 → `PositionHistoryWindow` を開く。
- ViewModel が `IClosedTradeStore.LoadAllAsync` を読み、フィルタ適用後に
  **`EntryExecutionId` で `CollectionViewSource` グルーピング** (既存ポジション一覧と同じ
  `CollectionViewGroup` + `GroupAggregateConverter` パターンを再利用)。
- 親行 = グループヘッダ (建玉サマリ: 建値=数量加重 or 単一、実現損益=子合計、累積損益=走算)。
- 子行 = グループ内 `ClosedTradeRow` を DataGrid で表示。
- サマリーは ViewModel 側で集計してフッタにバインド。
- 配色は Dark/Light テーマ準拠。損益の正負色は症状ベースで指定 (Claude は色を視認できないため、色コードは別途指定)。

### 4-7. バックフィル (任意・初回シード)
過去分を表示するため、`orders-metadata.json` (Auto/Manual・新規/返済・targetExecutionId) と
`logs/*.log` の「約定検出 … 価格=」を突合して `ClosedTrade` を再構成する
ワンショットツール (アプリ層サービス or 外部スクリプト) を用意する。
**ログ 7 ファイル保持の範囲が限界**である旨を実行時に明示 (取りこぼしを黙殺しない)。
※ 恒久運用は 4-5 のリアルタイム記録が主。バックフィルは初回のみ。

---

## 5. データフロー

```
TV シグナル(返済) ──Webhook──▶ ClosePositionUseCase
                                  │ (戦略で建玉を絞り込み・targetExecutionId 指定)
                                  ▼
kabu 返済約定 ──ExecutionEvent──▶ ExecutionApplier.CloseTargetPositionAsync
                                  │ position.ApplyClosure(qty)  ← 既存
                                  ├─[追加] 実現損益を計算
                                  └─[追加] IClosedTradeStore.AppendAsync(ClosedTrade)
                                            │
                                            ▼
                              position-history.json (追記・恒久)
                                            │
                              PositionHistoryViewModel が読み込み
                                            ▼
                        ポジション履歴画面 (建玉グループ + 分割決済子行) → スクショ公開
```

---

## 6. テスト方針 (`test-spec.md` 準拠)

| ID | 観点 | 期待 |
|---|---|---|
| PH-U1 | 損益計算 (買建/売建 × 利益/損失) | 式どおりの符号・金額 |
| PH-U2 | マイクロ倍率 10 / ミニ 100 | ProfitMultiplier 反映 |
| PH-U3 | 3 枚を 1 枚ずつ決済 | 同一 EntryExecutionId の 3 レコード生成 |
| PH-U4 | 同一銘柄で 2 戦略同時保有 → 片方決済 | 当該戦略のレコードのみ生成 (混線なし) |
| PH-U5 | AppendAsync 冪等 (同 ExitExecutionId 二重) | 1 件のまま |
| PH-U6 | ストア再読込 | 保存内容が一致 |
| PH-U7 | グルーピング/サマリー集計 | 建玉単位の勝率・合計が一致 |

---

## 7. 実装ステップと受け入れ条件

1. ドメイン/アプリ: `ClosedTrade` / `IClosedTradeStore` / `IContractMultiplierResolver` 追加
2. インフラ: `JsonClosedTradeStore` + DI 登録 (`position-history.json`)
3. 結線: `ExecutionApplier` に記録副作用を追加 (既存挙動不変)
4. テスト: PH-U1〜U7 PASS
5. UI: `PositionHistoryWindow` + ViewModel + MainWindow ボタン
6. (任意) バックフィルツール
7. ビルド 0 警告 / 全テスト PASS / 実機 (simulator) で表示確認
8. `sync_to_public.ps1 -Force` → public リポ commit → push

**受け入れ条件**: 自動取引の決済が `position-history.json` に追記され、画面で建玉単位 + 分割決済子行が表示され、自動のみフィルタ + 折りたたみでそのままスクショ公開できる。

---

## 8. バージョン履歴
- **0.1.0** (2026-06-02): 初版。仕様 + 詳細設計を確定。実装着手前のドラフト。
