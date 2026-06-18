# N225BrokerBridge 開発ロードマップ / 未実装事項

開発期間中の **未実装機能 / 将来拡張候補** をここに集約する。  
作業の都度、本ファイルを更新して「次にやること」を見失わないようにする。

最終更新: 2026-05-18

---

## 1. UI 関連の将来拡張

### 1.1 銘柄管理 UI (優先度: 中)
**現状**: `MainViewModel.LoadInstruments` で日経225 Mini/Micro を hardcode。新規銘柄追加には XAML+ViewModel 修正が必要。

**将来案**: 戦略管理ダイアログと同じパターンで「銘柄管理ダイアログ」を新設。
- ユーザーが任意の銘柄を登録 (FutureCode + DisplayName + デフォルト ResolvedSymbolCode)
- 追加・編集・削除可能
- 永続化先: `%LOCALAPPDATA%/N225BrokerBridge/instruments.json`
- 起動時に `TryResolveInstrumentsAsync` で kabu の現月コードに上書き
- 対象想定: 日経225 Mini / Micro / ラージ / TOPIX 先物 / グロース先物 等

**着手条件**: ラージ等の追加銘柄を実運用したくなった時。当面は不要 (Mini/Micro で運用)。

---

### 1.2 建玉一覧のグループ表示・集計 (優先度: 低)
**現状**: グループ化を一旦撤去 (`CollectionViewSource` 廃止)、フラットな DataGrid 表示。

**将来案**: 旧 N225OrderBridge と同じく銘柄ごとに折りたたみ + 合計枚数・平均建値・損益集計を出す。  
ただし `GroupStyle` カスタム Template が DataGrid のカラム幅同期を壊すバグ既出のため、`Expander` ベース等の別実装が必要。

**着手条件**: 建玉が常時数十件以上に膨らんで一覧が見づらくなった時。

---

### 1.3 損益のリアルタイム計算 (優先度: 中)
**現状**: `PositionRow.Profit` が **0m 固定**。価格 push との突合計算が未実装。

**将来案**:
- `IPriceUpdateNotifier` から最新価格を受けて、`Positions` 各行の Profit を再計算
- (現在値 - EntryPrice) × Side 係数 × LeaveQty × 倍率
- 計算は MainViewModel で十分 (Domain に持ち込まない、表示専用)

**着手条件**: 実機運用開始前。トレーダーの状況把握に必須。

---

### 1.4 設定変更後の自動再反映 (優先度: 低)
**現状**: `KabuOptions` 等の Singleton Options は起動時に構築されるため、設定ダイアログでの変更は **アプリ再起動が必要**。

**将来案**: `IOptionsMonitor<T>` ベースに切替えてホットリロード対応。  
または保存時に明示的に「再起動が必要です」ダイアログを出して再起動を促す。

**着手条件**: 開発中の頻繁な設定変更が手間になった時 / 運用フェーズで設定変更頻度が増えた時。

---

### 1.5 UI Fluent テーマでの細線描画 (優先度: 低)
**現状**: DataGrid の縦線が太く見える問題に対処できず、`GridLinesVisibility="None"` の代替案でフォールバック。  
WPF Fluent / Mica テーマで 1px 線が物理的に複数ピクセル幅にレンダリングされる制約あり。

**将来案**: テンプレート完全置換 or テーマレス DataGrid に切替。

**着手条件**: 見た目改善の優先度が上がった時。

---

## 2. ブローカー連携の将来拡張

### 2.1 楽天 RSS アダプタ (Phase 7、優先度: 中)
**現状**: マルチブローカー設計済み (`IBrokerAdapter` インターフェース) だが、`KabuAdapter` のみ実装。

**将来案**: 楽天 RSS の Excel COM/RTD 経由でデータ取得 + 発注する `RakutenAdapter` を Infrastructure 層に追加。
- `RakutenOptions` (DLL パス、ユーザー情報等)
- 楽天 RSS の COM オブジェクト名、関数仕様、発注 API の有無の調査が必要
- 設定ダイアログにブローカー選択追加

**着手条件**: au カブコム以外で取引したくなった時。

---

### 2.2 WebSocket 切断・再接続の強化 (優先度: 中)
**現状**: `KabuBoardWebSocketService` は切断時に 5 秒待ち + 再接続を試みるシンプルな実装。再接続後の registered symbols 再登録が未実装。

**将来案**:
- 切断検知 → 再接続 → SubscribePriceAsync を全銘柄に対して自動再実行
- バックオフ戦略 (1s → 5s → 30s → 60s)
- 接続失敗時の UI 通知

**着手条件**: 本番運用で WebSocket 切断による価格更新停止トラブルが発生した時。

---

### 2.3.5 kabu API 最適化 (優先度: 中、2026-05-18 追加)

旧 N225OrderBridge は API ができたばかりの古い時期に作られたため、kabu API の効率的な使い方が一部できていない。
**実装ルール**: [`dev-rules.md` §2](dev-rules.md) — kabu API リファレンスを必ずチェック。

#### 2.3.5.1 ✅ /orders の pending 追跡型ポーリング (2026-05-18 実装済)
- 旧: 毎秒全件取得
- 新: `IPendingOrderTracker` で未約定 OrderID のみ追跡、`/orders?id=xxx` で個別照会

#### 2.3.5.2 ⛔ /sendorder/future 返済の配列指定で 1 リクエスト化 (実装見送り、2026-05-18 確定)

**当初の動機 (自動取引の跨ぎ消化最適化)**:
- 自動取引で跨ぎ消化が発生すると、N 建玉返済 = N 回 `/sendorder` リクエスト
- API 仕様上は `ClosePositions: [...]` 配列で 1 リクエスト化が可能

**実装見送り理由 (2026-05-18 ユーザー判断)**:
1. **旧 N225OrderBridge も配列要素 1 件しか使っていない** (`SendorderFutureExitApi.cs:25-32`)
   → 配列複数指定は実証されていない使い方
2. **GitHub Issue でエラー報告多数**: 「決済指定内容に誤りがあります」(Code 8) など
   ([Issue #126](https://github.com/kabucom/kabusapi/issues/126), [#1080](https://github.com/kabucom/kabusapi/issues/1080))
3. **公式リファレンスに「配列複数指定時の挙動」明示なし**
4. **リスクと見合わない**: N=3 件返済でも 30ms 程度の差、跨ぎ消化の本番運用初日にエラーで返済できないと致命的

**現状維持の方針**:
- `ClosePositionUseCase` で各 Allocation ごとに 1 リクエストずつ送る (旧 N225OrderBridge と同じ)
- 跨ぎ消化は数件レベルなら無視できる効率差

**再検討条件**:
- kabu API リファレンスで「複数指定時の挙動」が明示されたとき
- もしくは GitHub で「複数指定が安定動作する」確実な例が複数出てきたとき

**重要**: この見送り判断は「実装が無理」ではなく「現状の旧実証パターンを尊重して安全運用」が理由。

#### 2.3.5.3 ❌ /positions / /board の絞込パラメータ確認 (未実施)
- `/positions?symbol=xxx` で銘柄絞込が可能か API リファレンスで再確認
- 現状は全件取得 + クライアント側で SymbolMatch

---

### 2.3 注文照会のリトライ / 整合性チェック (優先度: 中)
**現状**: `KabuOrderPollingService` で /orders を 1 秒ごとに取得。ネットワークエラー時は WARN ログのみで継続。発注後の OrderResultStatus.NetworkError 時の追加照会は未実装。

**将来案**:
- ネットワークエラーが連続したら指数バックオフ
- NetworkError で内部 Order を Rejected 扱いした場合、後で /orders に出現したらアラート
- 起動時の注文状態整合性チェック (内部 Order とブローカー注文の突合)

**着手条件**: 本番運用で発注後の状態不整合が発生した時。

---

## 3. ドメイン / アプリケーション層の将来拡張

### 3.1 集約リポジトリの永続化 (優先度: 低)
**現状**: `InMemoryOrderRepository` / `InMemoryPositionRepository` のみ。アプリ終了で状態消失 (kabu から再取得すれば復元可能)。

**将来案**: SQLite (Entity Framework Core) または LiteDB で集約も永続化。

**着手条件**: 取引履歴の精密な追跡 / 監査要件が出てきた時。当面は Serilog 監査ログで代替。

---

### 3.2 トレードルール / リスク管理層 (優先度: 高)
**現状**: 未実装。発注前のリスクチェック (1 銘柄あたりの最大建玉数・1 日あたりの最大損失等) が無い。

**将来案**:
- `IRiskGuard` インターフェース新設
- `PlaceNewOrderUseCase` 内で発注前にチェック
- 違反時は Rejected 相当の結果を返す

**着手条件**: 実機運用開始前。事故防止の最低限の安全網。

---

## 4. テスト / 運用関連

### 4.1 MainViewModel ユニットテスト (優先度: 中)
**現状**: WPF Dispatcher 依存のため未追加。

**将来案**:
- Dispatcher を抽象化 (`IUiDispatcher`) してテストで bypass
- ObservableCollection の挙動をテスト

**着手条件**: UI ロジックが複雑化した時。

---

### 4.2 ✅ シミュレータモードで代替実装 (2026-05-27 設計確定、実装着手中)
**旧記載**: 検証ポート専用のサンプルデータ注入 (優先度: 低)。kabu 検証ポートは /positions, /orders とも常に空配列を返す → UI 一覧の動作確認ができない。当初案は「`_orderMetaStore` から擬似 OrderSnapshot を生成して MainViewModel に流し込む Mock Data Injector」。

**新方針**: `IBrokerAdapter` レベルで `MockBrokerAdapter` を作り、`--simulator` 起動引数で DI 切替する設計に発展。検証ポートに依存せず、kabu Station 未起動でも全フローが動く。

**詳細仕様**: [`simulator-mode.md`](./simulator-mode.md)
**追加要件**: F-18 (requirements.md §5)

---

### 4.3 本番モード切替時の安全ガード (優先度: 中)
**現状**: 設定ダイアログで本番に切り替えると、保存後の再起動でいきなり本番接続。

**将来案**:
- 本番モード切替時に確認ダイアログ (「実発注が飛びます。続行しますか？」)
- 起動直後は「セーフモード」(発注ボタン全部 disable) で開始し、明示的にトレード許可を入れる
- AUTO トグルの ON/OFF を起動直後は強制 OFF

**着手条件**: 実機運用開始前。本番モードでの誤操作防止。

---

## 5. ドキュメント

### 5.1 旧 → 新 移行ガイド (優先度: 低)
**現状**: なし。

**将来案**:
- 旧 N225OrderBridge を停止 → 新 N225BrokerBridge に切替える運用手順
- CSV → JSON 移行 (旧データを新形式にコンバートする一時スクリプト)
- TV Webhook URL は port 8000 維持 (旧 OrderBridge 停止後、新ブリッジが 8000 を引き継ぐ)

**着手条件**: 本番切替が現実視野に入った時。

---

## 6. 完了済み (記録目的)

- ✅ 2026-05-18: 注文メタデータストア (`orders-metadata.json`) 実装、kabu /orders と OrderID で突合
- ✅ 2026-05-18: 戦略管理ダイアログの登録/編集/更新/削除シーケンス整理
- ✅ 2026-05-18: 設定ダイアログに本番/検証 環境セレクタ追加、検証ポート 18081 対応
- ✅ 2026-05-18: 設定ダイアログに「パスワード表示」トグル追加
- ✅ 2026-05-18: ExecutionStreamSubscriberService 追加 (ExecutionStream → ExecutionApplier の致命的バグ修正)
- ✅ 2026-05-18: BrokerSessionInitializer 相当の起動シーケンス整備 (建玉初期ロード等)
- ✅ 2026-05-18: 旧 CSV → 新 JSON 永続化方針の文書化 (architecture.md §3.5.2)
- ✅ 2026-05-18 夜: kabu API 8 ハマり対処 ([adapters/kabu.md](adapters/kabu.md)): Token Singleton / Product=3 / Side ToKabuCode / Exchange 時刻動的 / FrontOrderType 先物専用 / BID-ASK 逆命名 / DerivMonthCalculator / OrderDetail nullable
- ✅ 2026-05-18 夜: 注文タイプ正本マッピング (旧 OrderFactory 踏襲、成行=120/対当=20+Bid|Ask/指値=20/逆指=30)
- ✅ 2026-05-18 夜: Mini ↔ Micro 価格共有・損益再計算
- ✅ 2026-05-18 夜: Accept 直後 OrderRow 即時追加 + LatestSnapshots キャッシュ
- ✅ 2026-05-18 夜: 限月日本語表記 (「2026年6月限」)

---

## 7. 次回優先 (引き継ぎ from handover_20260518_evening.md)

### 7.1 残り注文タイプの動作確認 ⭐⭐⭐
- 対当買・対当売 / 指値買・売 / 返済 (各タイプ) / キャンセル
- 本日成行買のみ動作確認、他は実装完了で未検証

### 7.2 建玉「注文中」と注文一覧の整合 ⭐⭐
**症状**: キャンセル後、Position.HoldQty が解放されない。アプリ内表示が kabu と乖離。

**設計**:
- 紐付け: `Order.TargetExecutionId` ↔ `Position.Id` (ExecutionId)
- 必要ハンドラ: `KabuOrderPollingService.PollOnceAsync` で OrderState 遷移 (Cancelled/Filled) 検知 →
  - Cancelled: `Position.ReleaseReservation(qty)` で HoldQty 解放
  - Filled: `Position.ExecuteClose(qty)` で LeaveQty 減算
- イベント経由で `Application.PositionApplier` 等に適用

### 7.3 逆指値 (Stop) 注文の完全実装
- `KabuSendOrderRequest` DTO に `ReverseLimitOrder` ブロック (TriggerPrice/UnderOver/AfterHitOrderType/AfterHitPrice) 追加
- UI から trigger price 入力 UX
