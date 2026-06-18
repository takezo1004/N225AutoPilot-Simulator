# N225BrokerBridge 開発ルール

新ブリッジ開発で**必ず守るルール** をここに集約。Claude/開発者は実装前に必ず確認すること。

---

## 1. 旧コードを必ず参照する

新ブリッジは「旧 N225OrderBridge の移植 + マルチブローカー対応の新仕様」が方針。**勝手な新仕様化は禁止**。

### 必須手順

1. 該当機能を実装する前に **旧 N225OrderBridge の該当箇所を読む**
2. 「これは旧通りに移植する」「これは新仕様で変える (理由付き)」を明示判断
3. 変える場合の判断理由はコードコメント or PR 説明に残す
4. 迷ったら **旧を踏襲** がデフォルト

### 過去の失敗例 (反面教師)

| 機能 | 私のミス | 旧の正しい挙動 |
|---|---|---|
| /orders ポーリング | 毎秒全件取得 | OrderInquiryList で pending のみ追跡し、空ならネットワーク叩かない |
| Order/Position の Interval 不変条件 | 全てで Interval > 0 必須 | 手動 (Manual) は Interval=0 を許容 |
| LoadInstruments の銘柄コード | null にして TryResolve で解決 | fallback 値を持ち、解決成功時のみ上書き |
| LoadStrategies の初期 seed | 空なら V7-7-fixed を強制 seed | seed しない (ユーザーが手動追加) |

---

## 2. kabu API リファレンスを必ずチェック (2026-05-18 追加)

kabu API を呼ぶコードを書く場合 **公式リファレンスを実装前に確認する**。

### 🚨 kabu API のハマりポイント集 ([adapters/kabu.md](adapters/kabu.md))

**kabu API を触る前に必ず読む** — 直感に反する仕様を集約:

1. **BID / ASK の命名が通常と逆** (`BidPrice` = 売り板 = 通常 ASK)
2. **Side コード 1=売 / 2=買** (Domain enum と逆順、`ToKabuCode()` 必須)
3. **Exchange は時刻ベース動的判定** (日中 23 / 夜間 24)
4. **FrontOrderType は先物専用** (株式値を流用するとエラー)
5. **Product=3 (先物)** 固定 (デフォルト 2 だと /positions パース失敗)
6. **トークン単一有効** (KabuTokenService は Singleton 必須)
7. **`/symbolname/future` の DerivMonth=0 は SQ 日に罠** (失効限月が返る)
8. **WebSocket push は値動き時のみ** (場閉鎖時は静止)

### リファレンス
- **公式リファレンス**: https://kabucom.github.io/kabusapi/reference/index.html
- **公式ポータル**: https://kabucom.github.io/kabusapi/ptal/
- **OpenAPI yaml**: [`../../../kabu_api.yaml`](../../kabu_api.yaml) (ローカル)

### 必須手順

1. **エンドポイントの全パラメータを確認** (特にフィルタ系のクエリパラメータ)
2. **必要なデータだけ取得する** ような呼び方を選ぶ
3. **複数件を 1 リクエストにまとめられる** API は活用する
4. レスポンス構造を確認し、無駄なデータ転送を避ける

### 旧ブリッジが API 活用しきれていない箇所 (新ブリッジで改善)

| API | 旧の使い方 | API 仕様上の最適 | 新ブリッジ対応状況 |
|---|---|---|---|
| `/orders` | 全件取得 | `?id=xxx` で 1 件指定可 | ✅ `GetOrderByIdAsync` 実装済 (2026-05-18) |
| `/sendorder/future` (返済) | 1 注文 1 リクエスト | `ClosePositions: [...]` 配列で複数返済を 1 リクエスト | ❌ **要実装** (roadmap §2.4) |
| `/positions` | 全件取得 + クライアント側フィルタ | `?symbol=xxx` で銘柄絞込可 | ❌ 要確認 |
| `/board` | 1 銘柄ずつ | (確認要) | (現状 WebSocket push で代替) |

### 理由

旧 N225OrderBridge は **kabu API が出たばかりの古い時期に作成** されており、当時の知識・ドキュメント不足で API を最適に使えていない箇所がある。
新ブリッジでは API リファレンスを再確認し、**「全件取得 → クライアント側フィルタ」のような非効率なパターンを避ける**。

これにより:
- ネットワーク負荷低減 → レスポンス改善
- kabu Station への負荷低減
- ログ騒音低減

---

## 3. ロギング方針

旧 N225OrderBridge は log4net で起動時 → 初期化 → 各イベントを詳細記録していた (実運用で証跡として有用)。新ブリッジでも **起動から各段階のログを Serilog で出力** すること。

詳細は [`architecture.md` §4.1](architecture.md) のロギング節を参照。

---

## 4. テスト・ビルド

- ドメイン層 / アプリケーション層の変更後は **必ず `dotnet test`** を実行
- UI 修正は **ビルド成功 + 起動成功 + ログでエラーなし** を確認
- 自分で確認できない UI 動作 (操作系) は **ユーザーに依頼して確認結果をログで取り出す**

---

## 5. 設定変更

- DPAPI 暗号化保存 (機密) と平文 JSON (非機密メタ) の使い分けを守る
- 詳細は [`architecture.md` §3.5.2](architecture.md) を参照
