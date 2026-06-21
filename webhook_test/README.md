# webhook_test — シミュレーション用ペイロード一式（offline）

製品1 シミュレーションで、ブリッジ `--simulator`（MockBroker・外部非接続）に **7種の Webhook ペイロード** を撃って挙動を確認するための素材。**kabu / TradingView / Cloudflare / ネット接続は不要。**

## 中身

| ファイル | 内容 |
|---|---|
| `STEP_BY_STEP.md` | offline 7ボタンの手順（**まずこれ**） |
| `payloads/` | 7種のペイロード（TV 実アラートと同形のフル構造） |
| `test_all.ps1` | ダッシュボードを使わず PowerShell から一括 POST する代替手段 |

ペイロード7種：01 認証失敗 / 02 Bad JSON / 03 新規買い / 04 返済 / 05 ドテン / 06 無視(flat→flat) / 07 戦略未登録。

## 使い方

通常は `起動_シミュレーション.bat` でダッシュボードを起動し、**7ボタンを押すだけ**（→ `STEP_BY_STEP.md`）。

手動で撃ちたい場合のみ `test_all.ps1`（MockBroker なので発注ケースも実弾なし）：

```powershell
pwsh -File test_all.ps1                 # 1, 2, 6, 7（発注なし）
pwsh -File test_all.ps1 -IncludeOrder   # 3, 4, 5 も（MockBroker・実弾なし）
```

受信 URL = `http://localhost:8000/webhook/`（POST・`Content-Type: application/json`・passphrase=`abcdefg`）。

## シグナル遷移 → Intent（参考）

| prev | current | action | Intent |
|---|---|---|---|
| flat | long | buy | NewOrder Buy |
| flat | short | sell | NewOrder Sell |
| long | flat | sell | ExitOrder（Long 全量） |
| short | flat | buy | ExitOrder（Short 全量） |
| short | long | buy | Doten（Short→Long） |
| long | short | sell | Doten（Long→Short） |
| その他 | — | — | Ignore |
