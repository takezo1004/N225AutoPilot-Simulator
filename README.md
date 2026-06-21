# N225AutoTrader — シミュレーション（製品1・無料サンプル）

ブリッジ（N225AutoTrader）を **`--simulator`（MockBroker・外部非接続）** で動かし、Webhook 受信 → 発注 → 約定 → 建玉計上の流れを**テストダッシュボードのボタン7種**で体験する無料サンプルです。**kabu / TradingView / Cloudflare / ネット接続は一切不要**。

```
[テストダッシュボード] ──webhook(8000)──> [N225AutoTrader ブリッジ --simulator（MockBroker）]
   7種のペイロードをボタンで発火 → レスポンス／ログを画面で確認
```

---

## インストールには2つの道があります
- **基本ルート（このREADME）**＝自分でビルド＆起動（Claude Code 不要）。
- **保険ルート**＝うまく行かないとき **Claude Code** に手伝ってもらう（[`CLAUDE.md`](CLAUDE.md) ／ `/install` `/verify` `/diagnose`）。

---

## 必要なもの
- Windows 10（1809+）/ 11（x64）
- **.NET 8**：`setup.exe` で導入するなら **.NET 8 Desktop Runtime**／ソースからビルドするなら **.NET 8 SDK**
- **Python 3.10+**（テストダッシュボード。標準ライブラリのみ使用）
- ※ kabu / TradingView / Cloudflare / 独自ドメインは**不要**

---

## 基本ルートの手順

### 1. ブリッジを用意（いずれか）
- **推奨**：`setup.exe` を実行して導入（GitHub Release から取得・「あれば入れない」）。
- **代替（ソースからビルド）**：
  ```powershell
  cd bridge
  dotnet build src\N225BrokerBridge.UI -c Debug
  ```
  `bridge/src/N225BrokerBridge.UI/bin/Debug/net8.0-windows/N225BrokerBridge.UI.exe` が出来ます。

ダッシュボードが導入済み／ビルド済みの exe を自動検出します。

### 2. テストダッシュボードを起動
ルートの **`起動_シミュレーション.bat`** をダブルクリック。
- ダッシュボードが内部でブリッジを `--simulator` 起動（MockBroker・本番口座に一切触れません）。
- 起動時にシミュレータ用設定（`*.simulator.json`・passphrase=`abcdefg`・`TestStrategy` 登録）を `%LOCALAPPDATA%\N225BrokerBridge\` に書き出します（本番設定 `*.Local.json` とは別ファイル）。

### 3. 7種のペイロードを試す
| # | テスト | 期待レスポンス |
|---|---|---|
| 1 | 認証失敗（passphrase 不一致） | `Authenticated_Failed` |
| 2 | Bad JSON | `Bad Request` |
| 3 | 新規買い（flat→long） | `NewOrderDispatched_` |
| 4 | 返済（long→flat） | `ExitOrderDispatched_` |
| 5 | ドテン（short→long） | `DotenDispatched_` |
| 6 | 未定義遷移（flat→flat） | `Ignored_` |
| 7 | 戦略未登録 | `Ignored_` |

レスポンスとブリッジログが画面に表示されます。

---

## 同梱物
| 場所 | 中身 |
|---|---|
| `bridge/` | N225AutoTrader ブリッジ（**ソースのみ**・setup.exe は GitHub Release 添付） |
| `n225_simulator_test_dashboard.py` ＋ `起動_シミュレーション.bat` | テストダッシュボード（Python・stdlib のみ） |
| `webhook_test/` | 7種のペイロード＋手順（`payloads/`・`STEP_BY_STEP.md`・`test_all.ps1`） |
| `templates/` | 設定例（`appsettings.Local.json.example`） |
| `VERSION.json` | 版（sim_runtime / bridge / webhook-spec） |
| `CLAUDE.md`・`.claude/` | 保険ルート（Claude Code 命令書 `/install`・`/verify`・`/diagnose`） |

> ⚠️ **ドラフト v0.4.0（2026-06-18・案A 自己完結へ刷新）。テスター環境での動作確認が必要です。**
> 旧 v0.3.0 の「3リポ構造（public bridge を別 clone）」前提は**案A（bridge 同梱の自己完結）**に置換しました。

ライセンス：Public（無料サンプル）。
