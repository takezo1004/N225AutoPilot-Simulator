# デモモード (`--demo`)

**最終更新**: 2026-05-27
**目的**: ブログ記事/マニュアル用スクリーンショット撮影専用モード。kabu API・Webhook を一切触らずに本物の UI に決め打ちデータを表示する。

> 💡 **動作テストや配布デモには `--simulator` を使うこと**。`--demo` はあくまで画面表示のみ (バックグラウンドサービスが起動しないため、Webhook 受信も手動発注もできない)。動作確認には [`simulator-mode.md`](./simulator-mode.md) を参照。

---

## 1. 概要

`N225BrokerBridge.UI.exe` を `--demo` 引数付きで起動すると、以下の挙動になる:

| 項目 | 通常モード | デモモード (`--demo`) |
|---|---|---|
| kabu API への接続 | 接続する | **一切接続しない** |
| Webhook listener (port 8001) | LISTEN する | **LISTEN しない** |
| 注文ポーリング | 起動する | **起動しない** |
| Position reconciliation | 起動する | **起動しない** |
| WebSocket 板情報受信 | 接続する | **接続しない** |
| 戦略レジストリ (strategies.json) | 読み書きする | **読み書きしない** |
| 建玉メタストア (auto-positions.json) | 読み書きする | **読み書きしない** |
| MainWindow の表示 | 通常通り | **通常通り表示 + 決め打ちデータで埋まる** |

つまり「**画面だけ立ち上がるが、外部とは何ひとつ通信しないし、永続化ファイルにも一切書かない**」状態が作られる。

---

## 2. 用途

### 2-1. ブログ記事 / 利用マニュアル用スクリーンショット

本番口座の建玉や損益が映ったまま画面を公開するのは不適切。検証モード (kabu:18081) は価格 push が来ず銘柄解決もできないので空画面になる。デモモードはこの両方を解決する「本物の UI + 安全なデータ」を提供する。

### 2-2. UI レイアウト調整時の動作確認

XAML を編集して見た目を変えたい時、本番口座を起動せずにそのままビルド・起動して確認できる。

### 2-3. 新規参加者へのデモ説明

「ブリッジってこんな画面ですよ」と見せる時、本番口座にも検証口座にも接続せずに即起動できる。

---

## 3. 使い方

### 3-1. コマンドラインから

```
cd <ブリッジの bin/Debug ディレクトリ>
N225BrokerBridge.UI.exe --demo
```

例:

```
C:\Users\<your-name>\N225BrokerBridge-public\bridge\src\N225BrokerBridge.UI\bin\Debug\net8.0-windows\N225BrokerBridge.UI.exe --demo
```

### 3-2. ショートカット経由

デモ専用ショートカットを 1 個別に作るのが便利:

1. 既存の本番起動ショートカットを右クリック → 「コピー」
2. デスクトップに貼り付け、名前を「N225 Broker Bridge (Demo)」等に変更
3. プロパティを開き「リンク先」フィールドの末尾に **半角スペース + `--demo`** を追加
4. ダブルクリックでデモ起動

本番起動用と別ショートカットにしておくと、誤って本番モードで起動して焦るリスクが減る。

### 3-3. 起動後の挙動

1. 通常通り MainWindow が開く
2. ヘッダーが「接続中 (kabu) / Webhook: 受信中 / 状態: 売注文: Accepted OrderId=DEMO20260525001」になる ← **表示だけ、実際は未接続**
3. 自動売買トグルが ON 表示になる ← **表示だけ、シグナルは来ない**
4. 戦略一覧/建玉/注文/ログ/現在値が決め打ちデータで埋まる
5. **手動発注ボタン (買 注 文 / 売 注 文 / 返 済 / キャンセル) は押しても何も起きない可能性が高い** (kabu API クライアントは DI に存在するが、トークン取得を試みて失敗する)
6. 画面を ×で閉じれば終了。何も残らない

---

## 4. 表示される決め打ちデータ

実装は `MainViewModel.SeedDemoData()` 一箇所に集約。値を変えたい場合はここを編集する。

| 領域 | 値 |
|---|---|
| ブローカー接続 | 接続中 (kabu) |
| Webhook 状態 | 受信中 |
| 直近発注状態 | 売注文: Accepted OrderId=DEMO20260525001 |
| 自動売買トグル | ON |
| 現在値 | 65,420 |
| BID / ASK | 65,420 / 65,415 |
| 銘柄選択 (手動発注パネル) | 日経 225 Micro / 2026 年 6 月限 / 161060023 |
| 注文タイプ / TIF / 数量 | 成行 / FAK / 1 |
| 戦略一覧 | V7_7_fixed_L (15m, 有効, 11:25:33 新規/買/65,400) + TestStrategy (5m, 無効, 11:14:52) |
| 建玉一覧 | 日経225Micro / 自動 / 20260525 / V7_7_fixed_L / 買 1 / 建値 65,400 / 損益 +200 |
| 注文一覧 | 日経225Micro / 自動 / 11:25:33.420 / V7_7_fixed_L / 新規/買/約定済 1/1 / 65,400 |
| ログ | passphrase 拒否 (Warning) + Webhook 受信 + 約定待ちリスト削除 + Position opened + 約定検出 の 5 行 |

---

## 5. 安全保証

デモモードが本番運用に影響しない理由を、コード変更の観点から整理する。

### 5-1. CLI 引数が無ければ追加コードは走らない

`--demo` を付けない通常起動では、`App.IsDemoMode = false` のまま。コード上の分岐 (App.xaml.cs の `if (IsDemoMode)`、MainViewModel.cs の `if (App.IsDemoMode)`) はすべて false 側 (= 既存パス) を通る。挙動は本機能追加前と完全に同じ。

### 5-2. 永続化ファイルを一切触らない

`SeedDemoData()` の中で書き換えるのは MainViewModel のメモリ上の ObservableCollection のみ。`%LOCALAPPDATA%\N225BrokerBridge\strategies.json` も `auto-positions.json` も `appsettings.Local.json` も一切読み書きしない。デモ画面を閉じればデータは GC で消える。

特に注意したのは戦略一覧の追加方法:

- 通常モード: `IStrategyRegistry.GetAll()` で永続化済の戦略を取得 → `Strategies.Add()` → `PropertyChanged` ハンドラで `IsEnabled` 変更時に `UpsertAsync` で永続化
- デモモード: registry を経由せず `Strategies.Add(new StrategyRow { ... })` で直接突っ込む。**PropertyChanged ハンドラは登録されない** ため、チェックボックスを触っても registry には書かれない

### 5-3. 外部通信ラインが物理的に遮断されている

App.xaml.cs の BootstrapAsync で `_host.StartAsync()` が呼ばれていないため:

- kabu API クライアントは DI コンテナに存在するが、HostedService として走るバックグラウンド処理 (トークン更新、ポーリング、reconciliation) は何も動かない
- Webhook listener は登録されているがポート 8001 を LISTEN しないので外部 POST は届かない
- WebSocket 板情報受信も接続しないため、値動き push は流れない

つまり「画面の見た目は本番運用中、内部は完全に死んでいる」状態。

### 5-4. デモモードであることをログで明示

App.xaml.cs の起動ログに以下を出力:

```
====================================================
デモモードで起動します (kabu/Webhook 接続なし、決め打ちデータ表示)。
バックグラウンドサービスは一切起動しません。
実際のデータ流し込みは MainViewModel.SeedDemoData() で実行。
====================================================
N225BrokerBridge 起動完了 (デモモード)。
```

ログファイル (`%LOCALAPPDATA%\N225BrokerBridge\logs\`) でも確認可能。

---

## 6. 制限事項

### 6-1. 手動発注ボタンは押さないでください

デモモードでは画面の [買 注 文] / [売 注 文] / [返 済] / [キャンセル] ボタンを押しても、内部の発注ユースケースが kabu トークンを取得しようとして失敗する可能性が高い (例外で UI が停止することもあり)。**操作せず、見るだけ・撮るだけ** に留める。

### 6-2. リアルタイム更新は来ない

価格 push が来ないので現在値・BID・ASK は決め打ち値のまま動かない。スクショ撮影には十分。

### 6-3. デモデータを更新したい場合

`MainViewModel.SeedDemoData()` を編集する。再ビルドが必要。

---

## 7. 実装の中核となるコードの場所

| ファイル | 役割 |
|---|---|
| [`src/N225BrokerBridge.UI/App.xaml.cs`](../src/N225BrokerBridge.UI/App.xaml.cs) | `IsDemoMode` プロパティ + `--demo` 引数判定 + `_host.StartAsync()` の抑止 |
| [`src/N225BrokerBridge.UI/ViewModels/MainViewModel.cs`](../src/N225BrokerBridge.UI/ViewModels/MainViewModel.cs) | コンストラクタの分岐 + `SeedDemoData()` 本体 |

具体的な行番号は変動する可能性があるため、各ファイル内で「`IsDemoMode`」または「`SeedDemoData`」で検索すれば該当箇所が見つかる。

---

## 8. トラブルシュート

### 8-1. `--demo` を付けたのに通常起動になる

`exe` の起動引数が正しく渡っていない可能性。コマンドプロンプトから:

```
N225BrokerBridge.UI.exe --demo
```

と叩いた場合、起動ログに以下が出るはず:

```
デモモードで起動します (kabu/Webhook 接続なし、決め打ちデータ表示)。
```

これが出ない場合は引数が認識されていない。ショートカットの場合は「リンク先」が `"C:\...\N225BrokerBridge.UI.exe" --demo` の形 (実行ファイルパスは引用符内、`--demo` はその外) になっているか確認。

### 8-2. デモ画面でも本番口座の建玉/注文が混ざっている

これは設計上発生しない。もし混ざっていたら、デモモードが正しく有効化されていない (= 通常モードで起動している) 可能性。起動ログを確認。

### 8-3. デモモードを終了した後、本番起動が失敗する

デモモードで触ったデータは永続化されないので、本番起動には影響しない。本番起動が失敗するのは別の原因 (kabu Station 未起動 / Cloudflare Tunnel 停止 等)。`docs/troubleshooting.md` 参照。

---

## 9. 関連ドキュメント

- [`simulator-mode.md`](simulator-mode.md) — `--simulator` モード (動作テスト・配布デモ用、こちらは外部接続を Mock 化したうえで全ロジックを動かす)
- [`architecture.md`](architecture.md) — DDD 4 層構造
- [`dev-rules.md`](dev-rules.md) — 開発ルール全般
- [`troubleshooting.md`](troubleshooting.md) — トラブル時の診断手順
- [`mainwindow-layout.md`](mainwindow-layout.md) — UI レイアウト仕様
