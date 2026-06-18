# N225BrokerBridge — Claude Code 用プロジェクトガイド

このファイルは、本プロジェクト (N225BrokerBridge) を新しい PC でセットアップ・稼働させる際に、**Claude Code が最初に読み込むガイド**です。

> ⚠️ **このファイルは購読者の Claude Code 向け命令書のドラフト (v0.1.0) です**
> 本格運用前にテスター環境で動作確認が必要です。

---

## 1. プロジェクト概要

### このシステムが何をするか

**N225BrokerBridge** は、日経 225 自動売買のための Webhook 受信・発注実行システムです:

```
TradingView (戦略アラート)
    ↓ Webhook
Cloudflare Tunnel (https://your-domain.com/webhook/)
    ↓
N225BrokerBridge (本アプリ、localhost:8001)
    ↓ HTTP API
kabu Station (証券口座、 localhost:18080)
    ↓
東証 (実発注)
```

TradingView で設計した戦略のアラートが発火すると、Webhook 経由で本ブリッジに通知され、自動で kabu 証券口座に発注されます。

### 主要技術スタック

- **.NET 8** (C# / WPF)
- **Wpf.Ui** (Fluent UI ライブラリ)
- **Serilog** (ロギング)
- **kabu Station API** (auカブコム証券)
- **Cloudflare Tunnel** (外部公開)

---

## 2. 想定する利用者

| 項目 | 想定 |
|---|---|
| **OS** | Windows 10 1809+ / Windows 11 (x64) |
| **トレード経験** | 個人投資家、株式・先物の基礎知識あり |
| **技術スキル** | Windows PC を使いこなせる。プログラミング知識は不要 (Claude Code が補完) |
| **必要なアカウント** | auカブコム証券 (kabu Station)、TradingView、Cloudflare、GitHub |

---

## 3. Claude Code が守るべき原則

利用者の安全を最優先に、以下のルールを厳守してください:

### 3-1. 対話的・確認的
- 設定変更・ファイル削除・サービス再起動など影響のある操作は **必ず利用者に確認** してから実行
- 「これから X をします、よろしいですか?」を都度確認
- 「自動で進めて」と明示されたタスク以外は途中確認を欠かさない

### 3-2. 秘密情報を露出しない
以下の情報は **画面表示・ログ・コミットメッセージに絶対残さない**:
- `Webhook.Passphrase` (Webhook 認証文字列)
- kabu API パスワード (本番・検証両方)
- 証券口座番号、個人特定情報

これらを入力させる場合は、PowerShell の `Read-Host -AsSecureString` 等で伏字入力させること。

### 3-3. 環境差を吸収
利用者の環境はバラバラです:
- .NET 8 SDK 未インストール / 別バージョン
- kabu Station 未インストール / 設定不完全
- Cloudflare アカウント無し
- TradingView 無料プラン (Webhook 機能なし)

**先に環境を診断**してから、不足しているものを順次案内する流れ。一気に全部やらせない。

### 3-4. 失敗時のリカバリ
- 各ステップの前に「ロールバック可能か」を確認
- ファイル変更前にバックアップ作成
- エラー発生時は「やったこと・直し方」を明示

### 3-5. 冪等性
- 同じコマンドを何度実行しても同じ結果 (壊れない)
- 既に設定済みの項目は飛ばす
- 「途中まで進めて中断 → 続きから再開」が成り立つよう設計

---

## 4. システム構成詳細

### 4-1. プロジェクト構造

```
N225BrokerBridge/
├── CLAUDE.md                 ← このファイル
├── README.md                 ← 利用者向け概要 + 手動セットアップ手順
├── LICENSE                   ← 利用規約
├── .claude/
│   ├── commands/             ← スラッシュコマンド定義
│   │   ├── setup.md          ← /setup (全自動セットアップ)
│   │   ├── install.md        ← /install (環境構築のみ)
│   │   ├── verify.md         ← /verify (動作確認)
│   │   └── diagnose.md       ← /diagnose (トラブル時の自動診断)
│   └── skills/               ← 各種スキル
│       ├── kabu-config/      ← kabu Station 設定支援
│       ├── tv-webhook/       ← TradingView Webhook 設定支援
│       └── cloudflare-tunnel/← Cloudflare 設定支援
├── src/                      ← ソースコード
│   ├── N225BrokerBridge.UI/          (WPF プレゼンテーション層)
│   ├── N225BrokerBridge.Application/ (ユースケース層)
│   ├── N225BrokerBridge.Infrastructure/ (kabu API、Webhook 受信)
│   └── N225BrokerBridge.Domain/      (ドメインモデル)
├── tests/                    ← ユニットテスト
├── installer/                ← Inno Setup インストーラー
└── docs/                     ← 設計ドキュメント
    ├── architecture.md
    ├── adapters/kabu.md
    ├── mainwindow-layout.md
    └── troubleshooting.md    (Claude Code が /diagnose 時に参照)
```

### 4-2. 利用者データ保存場所

ビルド・設定・取引データは **`%LOCALAPPDATA%\N225BrokerBridge\`** に保存されます:

```
%LOCALAPPDATA%\N225BrokerBridge\
├── appsettings.Local.json    ← 利用者個別設定 (Webhook port, passphrase, kabu pw)
├── auto-positions.json       ← 自動売買の建玉メタストア
├── strategies.json           ← 戦略レジストリ
└── logs\                     ← Serilog のログ
```

これらはアンインストールしても **残ります** (再インストール時の継続性のため)。完全削除は手動。

### 4-3. ポート割り当て

| ポート | 役割 |
|---|---|
| `8001` | 本ブリッジの Webhook 受信ポート (TV → Cloudflare → 本ブリッジ) |
| `18080` | kabu Station 本番モード (本番口座) |
| `18081` | kabu Station 検証モード (テスト用口座) |

---

## 5. スラッシュコマンド一覧

Claude Code 起動後、以下のコマンドが使えます (各コマンドの詳細は `.claude/commands/` 配下):

| コマンド | 用途 | 利用シーン |
|---|---|---|
| `/setup` | **全自動セットアップ** (環境構築 → kabu → TV → Cloudflare → 動作確認) | 初回 |
| `/install` | 環境構築のみ (.NET SDK 確認、ソフトウェアインストール) | 初回 or 環境変更時 |
| `/verify` | 動作確認 (テスト POST、ログ確認) | セットアップ後 |
| `/diagnose` | トラブル時の自動診断 (ポート / プロセス / ログ確認) | 動かない時 |

最初は **`/setup`** から始めてください。

---

## 6. 想定される利用者シナリオ

### シナリオ A: ゼロから新規セットアップ

```
利用者: 「git clone してきたんだけど、どこから始めればいい?」
Claude Code: 「まず /setup を実行してください。環境診断から始めます。」
   → /setup
   → 環境チェック (Windows バージョン / .NET / kabu Station / TradingView アカウント)
   → 不足項目を順次解決 (利用者の確認を取りながら)
   → appsettings.Local.json テンプレ生成 (passphrase は伏字入力)
   → kabu Station 起動・接続確認
   → Cloudflare Tunnel セットアップ案内
   → TradingView アラート設定案内
   → /verify でテスト POST → 成功確認
   → 完了
```

### シナリオ B: 動かないトラブル

```
利用者: 「アラートが届かないんだけど」
Claude Code: 「/diagnose を実行します」
   → ブリッジプロセス起動確認
   → ポート 8001 リッスン確認
   → Cloudflare Tunnel 接続確認
   → 直近のログから異常を抽出
   → 修復案を提示 (利用者の確認後に実行)
```

### シナリオ C: バージョンアップ

```
利用者: 「新バージョンが出たから git pull した。何かする必要ある?」
Claude Code: 「設定ファイルの互換性を確認し、必要な差分を案内します」
   → 旧 appsettings.Local.json と新テンプレを比較
   → 新項目があれば案内
   → ビルドし直し
   → /verify で動作確認
```

---

## 7. ブリッジの設定 (要点)

### 7-1. appsettings.Local.json (利用者個別)

`%LOCALAPPDATA%\N225BrokerBridge\appsettings.Local.json`:

```json
{
  "Webhook": {
    "Port": 8001,
    "Passphrase": "DPAPI で暗号化された文字列"
  },
  "Kabu": {
    "Mode": "Production",
    "ProductionPassword": "DPAPI 暗号化",
    "VerificationPassword": "DPAPI 暗号化"
  },
  "Behavior": {
    "RequireConfirmBeforeOrder": true
  }
}
```

**重要**: パスワード類は **DPAPI で暗号化** されて保存されます。利用者ごとに固有の暗号化なので、別 PC にコピーしても復号できません (= 設定は新 PC で再入力が必要)。

### 7-2. Cloudflare Tunnel (`C:\SPB_DATA\.cloudflared\config.yml`)

```yaml
tunnel: <tunnel-id>
credentials-file: C:\SPB_DATA\.cloudflared\<tunnel-id>.json

ingress:
  - hostname: webhook.your-domain.com
    service: http://localhost:8001
    originRequest:
      httpHostHeader: localhost   # ← 重要、これがないと 400 Invalid Hostname
  - service: http_status:404
```

### 7-3. TradingView アラート設定

```
Webhook URL: https://webhook.your-domain.com/webhook/
Message (JSON):
{
  "alert_name": "MyStrategy",
  "interval": 5,
  "trade_type": "new",
  "side": "buy",
  "price": 0,
  "passphrase": "(設定した passphrase)"
}
```

---

## 8. トラブルシューティング (主要 5 件)

詳細は `docs/troubleshooting.md` に集約。Claude Code は `/diagnose` 時にここを参照します。

| 症状 | 主な原因 | 確認 |
|---|---|---|
| **Webhook が届かない** | Cloudflare Tunnel 停止 / ポート不一致 | `cloudflared` プロセス確認、`appsettings.Local.json` の Port |
| **kabu API 接続エラー** | kabu Station 未起動 / モード不一致 | kabu Station GUI で接続状態確認 |
| **発注されない** | 自動売買トグル OFF / 戦略未登録 | UI ステータスバーの自動売買トグル ON 確認、戦略管理画面 |
| **400 Invalid Hostname** | Cloudflare の `httpHostHeader` 欠落 | `C:\SPB_DATA\.cloudflared\config.yml` に `httpHostHeader: localhost` 追加 |
| **起動時例外** | .NET 8 ランタイム不一致 | `dotnet --list-runtimes` で 8.x 確認 |

---

## 9. 開発者 (著者) 向け補足

### 9-1. このファイルの位置づけ

このファイルは **購読者の Claude Code 向け命令書** です。著者が開発中に使う `c:\Users\takao2\N225TradingSystem\CLAUDE.md` とは別物です:

| ファイル | 対象 | 内容 |
|---|---|---|
| `c:\Users\takao2\N225TradingSystem\CLAUDE.md` | 著者の Claude Code | 開発ルール、メモリ運用、devlog 等 |
| `N225BrokerBridge/CLAUDE.md` (このファイル) | 購読者の Claude Code | セットアップ・運用支援、購読者向け原則 |

### 9-2. 命令書の改訂

利用者から「躓きポイント」のフィードバックがあれば:
1. 該当シナリオの再現を Claude Code 自身が試みる
2. `.claude/commands/` または `docs/troubleshooting.md` を更新
3. このファイル自体も必要に応じて改訂
4. バージョン更新 + 変更履歴を記録

### 9-3. バージョン

- バージョン: **0.1.0** (初版ドラフト)
- 作成日: 2026-05-21
- 最終更新: 2026-05-21

---

## 10. 連絡先・サポート

> ※ TBD — 著者の連絡先、有料サポートの案内、コミュニティチャンネル等

---

## 11. 関連ドキュメント

- [README.md](README.md) — 利用者向け概要 + 手動セットアップ手順
- [docs/architecture.md](docs/architecture.md) — システムアーキテクチャ詳細
- [docs/adapters/kabu.md](docs/adapters/kabu.md) — kabu API 仕様・ハマりポイント集
- [docs/mainwindow-layout.md](docs/mainwindow-layout.md) — UI 微調整ガイド
- [docs/demo-mode.md](docs/demo-mode.md) — デモモード (`--demo`) 仕様・使い方・安全保証
- [installer/README.md](installer/README.md) — インストーラービルド手順

---

## 付録: Claude Code 起動時の最初の発話例

利用者が初めて `claude` コマンドを起動したとき、以下のように対応してください:

```
Claude Code: 「N225BrokerBridge へようこそ。
これは日経 225 の自動売買システムです。
初めての方は /setup で対話的セットアップを開始できます。
既にセットアップ済みの場合は、何をお手伝いしましょうか?」
```

利用者が `/setup` と入力したら `.claude/commands/setup.md` のフローを実行してください。
