# N225 AutoPilot シミュレーション（製品1・無料サンプル）— 購読者の Claude Code 用ガイド（保険ルート）

> ⚠️ **ドラフト v0.4.0（2026-06-18・案A 自己完結へ刷新）。テスター環境での動作確認が必要です。**
>
> このファイルは、**基本ルート（[`README.md`](README.md) の自力ビルド＆起動）でうまくいかなかったとき**に、
> 購読者の Claude Code が手伝うための命令書です。基本が動いていれば本ファイルは不要です。
>
> 旧 v0.3.0 の「3リポ構造（public bridge を別 clone）」前提は、**案A（bridge 同梱の自己完結）**に置換しました。

---

## 0. このサンプルの全体像
ブリッジを `--simulator`（MockBroker・外部非接続）で動かし、Webhook の挙動を体験します。**kabu / TradingView / Cloudflare 不要**。
```
[テストダッシュボード] ──webhook(8000)──> [N225 AutoPilot ブリッジ --simulator（MockBroker）]
```
- ブリッジ（`bridge/`・C#/.NET・**ソース同梱／setup.exe は Release**）。テストダッシュボード（Python・stdlib のみ）が `--simulator` 起動＋7種ペイロード発火。

---

## 1. Claude Code が守る原則
1. **対話的・確認的**：ビルド・起動・設定変更は確認してから。
2. **秘密情報を露出しない**（このサンプルは Mock なので実秘密は不要だが原則は同じ）。
3. **環境差を吸収**：先に `/diagnose` → 不足（.NET 8 SDK / Python）を順次解決。
4. **配布ファイルを改変しない**：`bridge/`・ダッシュボード・手順書を書き換えない。書込んでよいのは `%LOCALAPPDATA%\N225BrokerBridge\*.simulator.json`・ビルド出力 `bin/`/`obj/` だけ。
5. **冪等性**：何度実行しても壊れない・済んだ項目は飛ばす。

---

## 2. スラッシュコマンド
| コマンド | 用途 |
|---|---|
| `/install` | 環境構築（ブリッジを用意：**setup.exe で導入**／または `dotnet build src\N225BrokerBridge.UI -c Debug` でビルド → Python 確認） |
| `/verify` | 動作確認（`--simulator` 起動 → テスト POST → レスポンス確認） |
| `/diagnose` | トラブル診断（プロセス／ポート8000／ログ） |

困ったら **`/diagnose`** から。詳細は各 `.claude/commands/*.md`。

---

## 3. 構成・ポート
- ブリッジ実体＝setup.exe 導入先、または build 出力 `bridge/src/N225BrokerBridge.UI/bin/Debug/net8.0-windows/N225BrokerBridge.UI.exe`（ダッシュボードが自動検出）。
- シミュレータ設定＝`%LOCALAPPDATA%\N225BrokerBridge\*.simulator.json`（本番 `*.Local.json` とは別）。
- ポート＝**8000**（simulator webhook）。passphrase＝`abcdefg`、戦略＝`TestStrategy`（自動投入）。

---

## 9. 開発者（著者）向け
本ファイルは**購読者向け命令書**。著者の開発ルールは `c:\Users\takao2\N225TradingSystem\CLAUDE.md`（別物）。配布の正本＝`DISTRIBUTION_MAP.md`。組立＝`distribution/sync_simulator.ps1`、リリース＝`distribution/release_simulator.ps1`。
