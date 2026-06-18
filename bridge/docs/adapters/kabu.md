# kabu ステーション API アダプタ — 実装ノート

**最終更新**: 2026-05-23
**目的**: kabu API の **直感に反する仕様 / ハマりポイント** を記録し、再度同じ落とし穴に落ちない。

> ⚠️ kabu API を触る前にこの文書 + `dev-rules.md §2` を必ず読むこと。

---

## 1. ⚠️ BID / ASK の命名が**通常と逆**

### 仕様（kabu API 公式）

`kabu_api.yaml` の `BoardSuccess` に明記:

> 下記にあるBIDとASKとは、**トレーダー目線から見た場合の値**であるため、
> **BidPrice = Sell1 の Price**、**AskPrice = Buy1 の Price** という数値となります。

### 対応表

| 板の場所 | 通常の市場用語 | **kabu API のフィールド名** |
|---|---|---|
| 売り板の最良気配（あなたが**買う**時の値段） | **ASK** | **`BidPrice`** |
| 買い板の最良気配（あなたが**売る**時の値段） | **BID** | **`AskPrice`** |

### kabu の発想

「自分が今やりたい行動の名前」で命名されている:
- 「**BID** (買いに行こう)」 → 売り板を見る → だから `BidPrice` = 売り板 (= 通常 ASK)
- 「**ASK** (売りに行こう)」 → 買い板を見る → だから `AskPrice` = 買い板 (= 通常 BID)

### 具体例

```
板情報 push: 161060019 現在=61580 BidPrice=61585 AskPrice=61575
```

- 売り板最良 = `BidPrice` 61585（買いたい時はこの値段で買える）
- 買い板最良 = `AskPrice` 61575（売りたい時はこの値段で売れる）
- 通常表記: BID=61575 / ASK=61585（ASK > BID で正常スプレッド）

ログで一見「BID > ASK」のように見えるのはこの仕様のせい。バグではない。

### 発注時の正しい使い方（旧 BestMarketOder 踏襲）

| やりたい事 | 取る価格 (kabu フィールド) | 通常用語 |
|---|---|---|
| 買いたい (Buy 対当) | `BidPrice` を hit | (通常 ASK 側 hit) |
| 売りたい (Sell 対当) | `AskPrice` を hit | (通常 BID 側 hit) |

**覚え方**: kabu は「自分の動作名」で命名 → 通常用語と **逆**。混乱したら `kabu_api.yaml:5202` を見る。

実装箇所:
- `MainViewModel.PlaceManualNewOrderAsync` (新規発注の BestMarket)
- `MainViewModel.ExitPosition` (返済の BestMarket)
- 旧コード参照: `N225OrderBridge/N225.Domain/BestMarketOder.cs`

---

## 2. ⚠️ Side コード: kabu API は 1=売 / 2=買

### 仕様

`/sendorder/future` の `Side` フィールド:
- **`"1"` = 売**
- **`"2"` = 買**

### Domain 側の注意

`Domain.ValueObjects.Side` は **`Buy=1, Sell=2`** で定義（kabu と逆）。
これは過去の都合上 enum 値が固まっているため、`int` キャストでそのまま kabu に渡すと **売買が逆転** する。

### 対策（実装済）

`SideExtensions.ToKabuCode()` は明示マッピング:

```csharp
public static int ToKabuCode(this Side side) => side switch
{
    Side.Buy => 2,   // kabu: 2=買
    Side.Sell => 1,  // kabu: 1=売
};
```

**禁止**: `(int)side` を kabu フィールドに直接渡してはいけない。必ず `ToKabuCode()` を経由する。

実装箇所: [`KabuMappers.cs`](../../src/N225BrokerBridge.Infrastructure/Brokers/Kabu/KabuMappers.cs) Line 24, 42

---

## 3. ⚠️ Exchange (市場コード) は時刻ベース動的判定

### 仕様

`/sendorder/future` の `Exchange` フィールド:
- **`2`** = 日通し (24時間)
- **`23`** = 日中（**日中限定**、夜間に送ると拒否される）
- **`24`** = 夜間
- `32/33/34` = SOR系（一部銘柄のみ）

### 旧コード踏襲ロジック

| 時刻 | Exchange |
|---|---|
| 06:00 〜 15:45 | **23 (日中)** |
| 15:45 以降 〜 06:00 | **24 (夜間)** |

実装: `KabuAdapter.GetActiveSessionExchange()`

参照: 旧 `N225OrderBridge/N225.Domain/ValueObjects/Exchange.cs`

### よくあるエラー

夜間に Exchange=23 で `/sendorder` を送ると `4002017 値段指定エラー` 等の **誤解を招くメッセージ** が返る。実際は Exchange ミスマッチ。

---

## 4. ⚠️ FrontOrderType は先物専用の値を使う

### 先物 (`/sendorder/future`) の有効値（株式とは別表）

| 値 | 意味 | Price の指定 |
|---|---|---|
| `18` | 引成 | `0` |
| `20` | **指値** | 発注金額 |
| `28` | 引指 | 発注金額 |
| `30` | **逆指値** | `0`（`AfterHitPrice` で指定） |
| `120` | **成行** | `0` |

### UI ボタンとのマッピング（旧 OrderFactory パターン踏襲）

| UI 選択 | FrontOrderType | Price | TIF デフォルト |
|---|---|---|---|
| **成行** | `120` | `0` | FAK |
| **対当** | `20` (=指値) | 現 `BidPrice`(Buy) / `AskPrice`(Sell) | FAS |
| **指値** | `20` | ユーザー入力 LimitPrice | UI 選択 |
| **逆指値** | `30` | `0` (+ `AfterHitPrice`) | FAK |

**注**: 旧ブリッジで使われていた値（株式用 18, 27 等）を誤って先物に流用すると `4002012 パラメータ不正:FrontOrderType` が返る。

実装: `KabuMappers.ToKabuFrontOrderType()`

---

## 5. ⚠️ Product コード: 先物用は `3`

`/positions`, `/orders` の `product` クエリ:
- `0` = すべて
- `1` = 現物
- `2` = 信用
- **`3` = 先物**
- `4` = OP

本ブリッジは **先物専用** なので必ず `Product = 3`。

**よくあるエラー**: `Product=2` (信用) のまま `/positions` を叩くと、先物建玉ゼロの場合に **非配列のエラー JSON** (`{"Code":...,"Message":"..."}`) が返り、JSON パースが配列期待で失敗する。

設定箇所: `KabuOptions.Product` + `appsettings.json`

---

## 6. ⚠️ トークンの単一有効ルール

kabu API のトークンは **最後に発行されたものだけが有効**。新トークン発行 = 古いトークン即無効化。

### よくある事故

`KabuTokenService` が **複数インスタンス化** されると、お互いの発行トークンを潰し合って `4001009 APIキー不一致` が連発する。

### 対策（実装済）

`KabuTokenService` と `KabuApiClient` は **必ず Singleton で 1 インスタンス** に統一。
`AddHttpClient<T>()` のデフォルトが **Transient** なので、名前付き HttpClient + `AddSingleton<T>(factory)` パターンで登録する。

実装: [`InfrastructureServiceCollectionExtensions.cs`](../../src/N225BrokerBridge.Infrastructure/DI/InfrastructureServiceCollectionExtensions.cs) `AddBrokerBridgeKabu()`

---

## 7. ⚠️ `/symbolname/future` の DerivMonth=0 は SQ 日に罠

### 仕様（公式注意書き）

> 取引最終日に「0」（直近限月）を指定した場合、日中・夜間の時間帯に関わらず、
> **取引最終日を迎える限月の銘柄コードを返します**。取引最終日を迎える銘柄の取引は
> **日中取引をもって終了**となりますので、ご注意ください。

### 意味

SQ 日（取引最終日）の夜間セッションに `DerivMonth=0` で問い合わせると、**当日 15:15 で終わる失効間際の限月**が返る。それで `/sendorder` すると弾かれる。

### 対策（実装済）

1. ラージ NK225 を `DerivMonth=0` で取得
2. `/symbol/{symbol}@2?info=true` で `TradeEnd` を取得
3. `DerivMonthCalculator.CalculateActiveDerivMonth(tradeEnd, now)` で SQ 日大引け後補正
4. 補正された限月で `/symbolname/future` を再度呼び、Mini/Micro の正しい現月銘柄を取得

実装: `KabuAdapter.CalculateActiveDerivMonthAsync()` + `DerivMonthCalculator`
旧コード参照: `N225OrderBridge/N225.Infrastrucure/KabuSuit/SymbolRequest.cs::Request1()`

---

## 8. ⚠️ `/sendorder/future` の Symbol は **数値の銘柄コード必須** (ティッカー不可)

### 仕様

kabu の発注 API は `Symbol` フィールドに **数値の銘柄コード** (例: `"161060023"`、`"161060019"`) を要求する。
TradingView のティッカー文字列 (`"NK225M1!"`、`"OSE:NK225M1!"` 等) を投げると、kabu は HTTP 400 + `Code=4002001 "銘柄が見つからない"` を返す。

### kabu 銘柄コード体系 (日経 225 系)

| 銘柄 | 先物コード (`FutureCode`) | Resolved Symbol Code 例 (2026年6月限) |
|---|---|---|
| 日経 225 ラージ | `NK225` | `161060018` |
| 日経 225 Mini | `NK225mini` | `161060019` |
| 日経 225 Micro | `NK225micro` | `161060023` |

- 数値コードは **限月ごとに変わる** (SQ 日を跨ぐと別の番号)
- ブリッジ起動時に `/symbolname/future?FutureCode=...&DerivMonth=0` で現月のコードを解決し、`InstrumentDefinition.ResolvedSymbolCode` に保持
- 限月計算ロジックは `DerivMonthCalculator` (SQ 日前日大引け後・夜間セッション補正を含む)

### TradingView ティッカーとの分離

本ブリッジは **TradingView Webhook の `SymbolTicker` フィールドを発注に使用しない**。
代わりに、UI で選択中の銘柄の Resolved Symbol Code を発注先銘柄として採用する。
これは「TV では Mini を見ながら、kabu と本ブリッジでは Micro を発注銘柄として使う」運用を許容するための設計判断。

詳細: [`architecture.md` §3.5 自動売買の銘柄ルーティング](../architecture.md#35-自動売買の銘柄ルーティング-tv-ティッカー--kabu-銘柄コード)
トラブル時: [`troubleshooting.md` §6 銘柄が見つからない (4002001)](../troubleshooting.md#6-kabu-が銘柄が見つからない-code4002001-で拒否する)

### ログの注意

`KabuApiClient.SendOrderInternalAsync` は送信 body 全体を Information レベルでログ出力する。
**取引暗証番号 (`Password` フィールド) は出力直前に `***` でマスク**してから書き込む (生 body は一切ファイルに残らない)。

```
/sendorder/future 送信 body={"Password":"***","Symbol":"161060023","Exchange":24,...}
```

ログを Slack やサポートに共有する際もこのままで安全。kabu に実送信される body には正規のパスワードが入っている (HTTPS over localhost のため通信路は漏洩リスク低)。

---

## 9. WebSocket push は値動き時のみ

`/websocket` の板情報 push は **値動き / 板変化があった時のみ** 流れる。
場が閉まっている時間帯（昼休み・夜間引け後等）は push が来ないため、現在値・損益が静止する。

### 対策（実装済）

起動時に `/board` を **1 回 REST でプル** して初期価格を取得し、損益計算を初期化する。
実装: `MainViewModel.TryResolveInstrumentsAsync` 末尾の `GetQuoteAsync` ループ。

---

## 関連ドキュメント

- [`dev-rules.md`](../dev-rules.md) — 開発ルール全般
- [`architecture.md`](../architecture.md) — DDD 4 層構造
- [`roadmap.md`](../roadmap.md) — 未実装事項
- [`../../kabu_api.yaml`](../../../kabu_api.yaml) — kabu API 完全仕様 (OpenAPI)
- 公式リファレンス: https://kabucom.github.io/kabusapi/reference/index.html
