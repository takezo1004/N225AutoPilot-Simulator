# シミュレーション ステップバイステップ（offline・7ボタン）

**対象**: 製品1 シミュレーション。ブリッジを `--simulator`（MockBroker・外部非接続）で動かし、テストダッシュボードの **7ボタン** で Webhook 受信〜発注〜約定〜建玉計上の挙動を体験する。
**kabu / TradingView / Cloudflare / ネット接続は一切不要。** 所要 約10分。

> 各 STEP の □ を順に消化してください。前の STEP が終わってから次へ。
> 困ったら Claude Code（保険ルート）に頼る：`/install`・`/verify`・`/diagnose`。

---

## STEP 0: 前提（3分）

- [ ] 0-1. **ブリッジが用意済み**（いずれか）
  - **推奨**：`setup.exe` で導入済み（Release から取得・あれば入れない）
  - 代替：ソースからビルド `dotnet build src\N225BrokerBridge.UI -c Debug`
- [ ] 0-2. **Python 3.10+** が入っている（`python --version`）
- [ ] 0-3. **ポート 8000 が空き**（PowerShell で `netstat -ano | findstr ":8000"` → 何も出なければ OK）

---

## STEP 1: 起動（2分）

- [ ] 1-1. ルートの **`起動_シミュレーション.bat`** をダブルクリック
- [ ] 1-2. テストダッシュボードが開き、内部でブリッジを **`--simulator` 起動**（MockBroker・本番口座に一切触れない）
  - 起動時にシミュレータ設定を自動投入：`%LOCALAPPDATA%\N225BrokerBridge\*.simulator.json`／passphrase=`abcdefg`／戦略 `TestStrategy`（interval 5・有効）
- [ ] 1-3. ログに `Webhook リスナー起動完了（受信 URL=http://localhost:8000/webhook/）` が出る

> うまく行かなければ STEP 0 へ戻る。先に進まない。

---

## STEP 2: 7ボタンを順に押す（5分）

推奨順 = 安全ケース（1, 2, 6, 7）→ 発注ケース（3, 4, 5）。**MockBroker なので発注ケースも実弾は一切飛びません。**

| # | ボタン | 期待レスポンス | ブリッジログ（要旨） |
|---|---|---|---|
| 1 | 認証失敗（passphrase 不一致） | 200 `Authenticated_Failed` | passphrase mismatch |
| 2 | Bad JSON | 400 `Bad Request: Invalid JSON.` | parse failed |
| 6 | 無視（flat→flat） | 200 `Ignored_` | Unhandled transition |
| 7 | 戦略未登録 | 200 `Ignored_` | strategy not enabled |
| 3 | 新規買い（flat→long） | 200 `NewOrderDispatched_` | NewOrder Buy qty=1 |
| 4 | 返済（long→flat） | 200 `ExitOrderDispatched_` | ExitOrder |
| 5 | ドテン（short→long） | 200 `DotenDispatched_` | Doten |

- [ ] 各ボタンを押し、ダッシュボードのレスポンス欄とブリッジ UI ログを目視で確認
- [ ] 期待と一致するか □ にチェック（7ケース中 6個以上で合格）

---

## STEP 3: 結果確認（2分・任意）

永続ファイルでも結果を確認できます：

- [ ] `%LOCALAPPDATA%\N225BrokerBridge\strategies.json` … `TestStrategy` の `lastSignalAt` 更新（ケース3〜7）
- [ ] `%LOCALAPPDATA%\N225BrokerBridge\orders-metadata.json` … 新規エントリ（ケース3〜5）
- [ ] `%LOCALAPPDATA%\N225BrokerBridge\logs\n225brokerbridge-<日付>.log` … 受信ログ

---

## トラブルシュート

| 症状 | 原因と対処 |
|---|---|
| ダッシュボードが起動しない | Python 3.10+ 未導入 → README 参照。`/diagnose` |
| ブリッジが起動しない | setup.exe 未導入 or 未ビルド → STEP 0。`/install` |
| 全部 `Authenticated_Failed` | passphrase 不一致（自動投入値は `abcdefg`）→ ブリッジ再起動 |
| すべて `Ignored_` ＋ `not enabled` | `TestStrategy` 未登録 → ブリッジ再起動で自動投入される |
| ポート 8000 使用中 | 別プロセスが占有 → 停止して再起動。`/diagnose` |
| 404 / 405 | 受信 URL は `http://localhost:8000/webhook/`（末尾 `/`・POST） |

---

ダッシュボードを使わず手動で撃ちたい場合は `test_all.ps1`（同フォルダ・`README.md` 参照）。
