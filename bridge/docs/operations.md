# 運用設計 (Operations)

**バージョン**: 1.0.0
**作成日**: 2026-05-26
**最終更新**: 2026-05-26
**対象**: N225BrokerBridge の起動・停止・監視・バックアップ・障害対応

---

## 1. このドキュメントの目的

本ブリッジを日々運用する際の標準手順と注意点をまとめる。
朝の起動 → トレード → 停止のルーティーン / 障害時の対処 / 定期メンテナンスを 1 か所に集約。

関連:
- [troubleshooting.md](./troubleshooting.md) — 症状別詳細手順
- [data-spec.md](./data-spec.md) — 永続化ファイル
- `../../docs/production_checklist.md` — 本番起動前チェックリスト
- `../../docs/monitoring_policy.md` — 監視方針 v2.x

---

## 2. 日々のルーティーン (標準)

### 2.1 朝 (8:30 頃)

```
[1] kabu Station を起動 (デスクトップショートカット or タスクトレイ)
    └─ ログイン (本番口座 / 検証口座)
    └─ kabu Station Web API が localhost:18080 で待受開始

[2] ダッシュボード B を起動
    └─ ショートカット n225_brokerbridge_dashboard.bat
    └─ ステータス LED で kabu 接続確認 (LED 緑)

[3] [ボタン] TradingView 起動 (デバッグモード)
    └─ launch_tradingview_debug_msix.ps1 が走る
    └─ デスクトップで 5 分足チャートを表示状態に
    └─ MCP 経由で操作可能になる

[4] [ボタン] 株式分析実行 (任意、朝の分析が必要な日)
    └─ 新規コンソールで Claude Code が /analyze を実行
    └─ 完了後ブラウザで分析レポートが開く

[5] [ボタン] 起動 (本番)
    ├─ cloudflared tunnel run n225-webhook (Cloudflare Tunnel)
    └─ N225BrokerBridge.UI.exe (本ブリッジ)
    └─ UI でステータス確認:
       ├─ ブローカー: 接続中 (LED 緑)
       ├─ Webhook: 受信中
       └─ 自動売買 OFF が初期状態

[6] UI で「自動売買」トグルを ON にする (任意)
    └─ Webhook 経由のシグナルが発注経路に流れ始める
```

### 2.2 日中

- ステータス LED 緑のまま放置
- 発注 / 約定の度に UI 通知 + ログ追記
- 必要に応じて UI から手動発注 / 返済 / キャンセル

### 2.3 引け後 (15:50 頃)

```
[1] 自動売買トグル OFF (必要なら)
    └─ 夜間取引もしない場合は OFF
    └─ 夜間取引する場合は ON のまま

[2] 必要なら手動で建玉確認
    └─ 建玉一覧 / 注文一覧 を目視
    └─ kabu Station 側と差異がないことを確認

[3] ダッシュボード B の [ボタン] 停止
    ├─ ブリッジ (N225BrokerBridge.UI.exe) を Graceful 終了
    └─ cloudflared を停止

[4] kabu Station をログアウト・終了 (任意)
```

夜間取引する場合は [3] 以降をスキップし、稼働継続。

### 2.4 夜間 (任意)

夜間も自動売買トグル ON で稼働可。kabu / 本ブリッジ / cloudflared がすべて稼働継続している前提。

---

## 3. 起動方法

### 3.1 推奨: ダッシュボード B 経由

```
n225_brokerbridge_dashboard.py を起動
  → [ボタン] 起動 (本番)
     └─ cloudflared.exe tunnel run n225-webhook (background)
     └─ N225BrokerBridge.UI.exe (background)
```

### 3.2 手動起動 (ダッシュボードを使わない場合)

PowerShell で 2 ターミナル:

```powershell
# Terminal 1: cloudflared
cloudflared tunnel run n225-webhook

# Terminal 2: bridge
& "C:\Users\takao2\N225TradingSystem\N225BrokerBridge\src\N225BrokerBridge.UI\bin\Debug\net8.0-windows\N225BrokerBridge.UI.exe"
```

### 3.3 デモモード起動

```powershell
& ".\N225BrokerBridge.UI.exe" --demo
```

外部接続なし、決め打ちデータで UI のみ表示。スクリーンショット / レイアウト調整用。

---

## 4. 停止方法

### 4.1 推奨: ダッシュボード B 経由

```
ダッシュボードの [ボタン] 停止
  ├─ ブリッジ Graceful 終了 (5 秒タイムアウト) → 強制 kill
  └─ cloudflared 停止
```

### 4.2 手動停止

```
1. UI の × ボタンで N225BrokerBridge.UI を閉じる
2. cloudflared のターミナルで Ctrl+C
```

### 4.3 強制終了 (緊急時)

```powershell
Stop-Process -Name "N225BrokerBridge.UI" -Force
Stop-Process -Name "cloudflared" -Force
```

⚠️ 強制終了でも UI レイアウトは 5 秒間隔で永続化済 (最後の 5 秒分のレイアウト変更は失われる)。

---

## 5. 接続経路図

```
[TradingView Pine Strategy]
      │ HTTPS Webhook
      ▼
[Cloudflare Edge]
      │ Cloudflare Tunnel
      ▼
[cloudflared (localhost)]
      │ HTTP
      ▼
[N225BrokerBridge.UI.exe (port 8001)]
      │ HTTP
      ▼
[kabu Station Web API (localhost:18080)]
      │ HTTP/TCP
      ▼
[au カブコム証券]
      │
      ▼
[東証 / 大証]
```

---

## 6. ステータス監視

### 6.1 UI でのモニタ

| 表示 | 観点 |
|---|---|
| ヘッダー LED (kabu / Bridge / Tunnel) | 緑 = 健全。1 つでもオフなら確認 |
| StateMessage | 直近の操作結果 |
| ログタブ | Error / Warn 出てないか |
| 建玉一覧 / 注文一覧 | kabu と乖離してないか |

### 6.2 ダッシュボード B でのモニタ

| LED | 意味 |
|---|---|
| kabu | localhost:18080/kabusapi/ が応答するか (3 秒間隔ポーリング) |
| Bridge | N225BrokerBridge.UI のプロセス生存 |
| Tunnel | cloudflared プロセス生存 |

### 6.3 ログでのモニタ

ログファイル: `%LOCALAPPDATA%/N225BrokerBridge/logs/n225brokerbridge-YYYY-MM-DD.log`

確認すべきパターン:

```
[ERR] ← エラー (即対応)
[WRN] ← 警告 (kabu 一時切断・WebSocket 再接続等)
[INF SignalHandler] ← Webhook 受信記録
[INF KabuOrderPolling] ← 約定検出記録
```

PowerShell で過去 1 時間のエラーを抽出:

```powershell
Get-Content "$env:LOCALAPPDATA\N225BrokerBridge\logs\n225brokerbridge-$(Get-Date -Format 'yyyy-MM-dd').log" `
  | Select-String "ERR|WRN" `
  | Select-Object -Last 50
```

---

## 7. 設定変更

### 7.1 接続資格情報の変更

1. UI メニュー「ファイル → 設定」を開く
2. 該当フィールドを入力
3. 「保存」 → `appsettings.Local.json` に DPAPI 暗号化で保存
4. 該当機能の挙動変更タイミングは項目による:

| 項目 | 反映タイミング |
|---|---|
| Webhook パスフレーズ | 即時 (次のシグナル受信から) |
| Webhook ポート | 再起動が必要 |
| kabu API パスワード | 即時 (次のトークン取得時) |
| kabu 取引パスワード | 即時 (次の発注時) |
| 環境 (Production/Verification) | 再起動が必要 (将来) |
| 手動操作確認 | 即時 |

### 7.2 戦略の有効化 / 無効化

UI メニュー「戦略 → 戦略管理」または、メイン画面の戦略一覧グリッドのチェックボックス。

### 7.3 銘柄定義の変更

現状: `MainViewModel.LoadInstruments` で hardcode。XAML+VM 修正・ビルド・再起動が必要。

将来案: 銘柄管理 UI (roadmap.md §1.1)

---

## 8. バックアップ

### 8.1 何を / どこに / いつ

| ファイル | 推奨タイミング | 重要度 |
|---|---|---|
| `strategies.json` | 戦略 CRUD 後・週次 | ⭐⭐⭐ |
| `auto-positions.json` | 自動建玉保有中なら毎日 | ⭐⭐⭐ |
| `appsettings.Local.json` | 設定変更後 (※ 別 PC では復号不可) | ⭐⭐ |
| `orders-metadata.json` | 月次 (取引履歴) | ⭐ |
| `logs/*.log` | 監査要なら月次 | ⭐ |

### 8.2 バックアップスクリプト例

```powershell
# scripts/backup_brokerbridge.ps1
$src = "$env:LOCALAPPDATA\N225BrokerBridge"
$dstRoot = "D:\Backups\N225BrokerBridge"
$ts = Get-Date -Format 'yyyyMMdd_HHmmss'
$dst = Join-Path $dstRoot $ts

New-Item -ItemType Directory -Path $dst -Force | Out-Null
Copy-Item -Path "$src\*.json" -Destination $dst
Copy-Item -Path "$src\logs" -Destination $dst -Recurse

Write-Host "Backed up to $dst"
```

タスクスケジューラで日次 23:00 に実行など。

---

## 9. 障害対応 (Quick リファレンス)

詳細手順は [`troubleshooting.md`](./troubleshooting.md) 参照。本節は最初のフローチャートのみ。

### 9.1 「Webhook が届かない」

```
1. UI の Webhook LED 確認 → オフなら起動失敗 (ログ確認)
2. cloudflared が動いてるか確認 (ダッシュボード B の Tunnel LED)
3. Cloudflare Dashboard で Tunnel が Healthy か確認
4. TradingView 側 Alert 履歴で Send 成功か確認
5. ローカルで `curl http://localhost:8001/webhook -X POST -d '{}' ...` 検証
```

### 9.2 「kabu API に接続できない」

```
1. kabu Station が起動・ログイン済みか確認
2. localhost:18080/kabusapi/ にブラウザでアクセス (応答が来るか)
3. kabu Station の「設定 → API」で API 接続が有効か確認
4. UI ログで "Token acquired" "401 Unauthorized" などを確認
5. パスワード再設定 → 再起動
```

### 9.3 「発注したのに UI 注文一覧に出ない」

```
1. ログで `PlaceOrderAsync` の応答コードを確認
2. ConfirmDialog で「キャンセル」を押していないか確認
3. 自動売買トグル状態の確認
4. 戦略レジストリで該当戦略が enabled か確認
5. AvailableInstruments で銘柄が解決済みか確認
```

### 9.4 「建玉が kabu と本ブリッジで乖離」

```
1. UI を一度終了
2. 再起動 → Step 3 (PositionReconciliation) で自動再同期される
3. 再同期後に乖離が残るならログで原因確認
   (kabu 側で手動取消した注文の HoldQty 残り等)
```

---

## 10. アップデート手順

### 10.1 本ブリッジのバージョンアップ (将来)

```
1. ダッシュボード B で停止
2. 新バイナリ展開
3. バックアップ (推奨)
4. ダッシュボード B で起動
5. UI ログで起動エラー無いことを確認
6. 設定 / 戦略 / 建玉が前回終了時と同じか確認
```

### 10.2 kabu Station アップデート時

```
1. ブリッジ停止
2. kabu Station 終了・新版インストール
3. kabu Station 再起動・API 有効化確認
4. ブリッジ起動
```

⚠️ kabu Station 大型アップデート後は API レスポンス形式が変わる可能性あり。`adapters/kabu.md` を参照。

---

## 11. 定期メンテナンス

### 11.1 毎日

- ログを目視 (ERR / WRN がないか)
- 建玉残数の確認

### 11.2 毎週

- `strategies.json` バックアップ
- 戦略の有効化状態を見直し

### 11.3 毎月

- `auto-positions.json` / `orders-metadata.json` バックアップ
- ログを別ディスクへアーカイブ
- 不要なテストデータ (検証時の建玉等) を整理

### 11.4 半期

- バックアップの整合性確認 (テスト復元)
- ドキュメント (本書 + 関連書) の更新確認
- パスワード更新

---

## 12. セキュリティ運用

| 項目 | 運用 |
|---|---|
| パスワード変更頻度 | 半期に 1 度 |
| ログの取り扱い | 個人 PC 内のみ。共有しない (kabu 認証情報・取引履歴を含む) |
| ブログ・ブログスクショ | デモモード (`--demo`) で撮影 |
| Cloudflare Tunnel 設定 | `/webhook` パスのみ公開、他はブロック |
| kabu Station ログイン状態 | 不在時は PC をロック (kabu 取引画面が表示されたまま放置しない) |

---

## 13. 災害復旧

### 13.1 ファイル破損 (strategies.json 等)

```
1. ファイルを退避 (`*.json.broken` にリネーム)
2. 直近のバックアップから復元
3. ブリッジ再起動
4. UI で内容確認
```

### 13.2 PC 障害

DPAPI 暗号化のため `appsettings.Local.json` は別 PC で使えない。

復旧手順:

```
1. 新 PC に N225BrokerBridge をインストール
2. .NET 8 SDK + Wpf.Ui 等の依存をインストール (現状はソースビルド)
3. ブリッジ起動 → 「設定」ダイアログでパスワード再入力
4. `strategies.json` / `auto-positions.json` を新 PC の %LOCALAPPDATA% に配置
5. ブリッジ再起動 → 設定反映
```

---

## 14. 起動失敗時のセーフモード

ブリッジが起動しなくなった場合の最終手段:

```
1. ダッシュボード B 停止 → 完全に閉じる
2. `%LOCALAPPDATA%\N225BrokerBridge\ui-layout.json` を削除 (画面が壊れたら)
3. ブリッジ起動 → デフォルトレイアウトで起動
4. それでも起動しないなら `appsettings.Local.json` を退避 (設定が壊れたら)
5. ブリッジ起動 → 設定ダイアログで再入力
```

---

## 15. 連絡先・サポート

- 開発者: takao2 (本リポジトリ所有者)
- 関連 GitHub Issue: (TBD)
- Claude Code: 開発支援用 (本書も Claude Code が更新)

---

## 16. 変更履歴

| バージョン | 日付 | 変更内容 |
|---|---|---|
| 1.0.0 | 2026-05-26 | 初版作成 |
