---
name: kabu-config
description: kabu Station (auカブコム証券) の設定支援。インストール検出、API 有効化案内、本番/検証モード切替、Token 取得テスト、よくあるハマりポイントの自動回避。/setup や /diagnose から呼び出される。
---

# kabu-config — kabu Station 設定支援スキル

> ⚠️ **このスキルはドラフト (v0.1.0) です。テスター環境での動作確認が必要です。**

## 目的

N225BrokerBridge が依存する kabu Station の設定を、利用者と対話しながら整備する。
インストール状況の検出から、API 設定の有効化、本番/検証モードの切替、Token 取得テストまで一貫支援する。

## 利用シーン

- `/setup` の Step 5 (kabu Station 接続確認) から呼ばれる
- `/diagnose` の症状 4 (kabu API 接続エラー) から呼ばれる
- 利用者が単独で「kabu の設定が不安」と相談した時

---

## 実行フロー

### Step 1: kabu Station インストール検出

```powershell
# 1A. 標準インストール場所を確認
$kabuPaths = @(
    "C:\kabusapi",
    "C:\Program Files\kabu.com Securities\kabuステーション",
    "C:\Program Files (x86)\kabu.com Securities\kabuステーション"
)
$installed = $kabuPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

# 1B. プロセス起動状況
$kabuProc = Get-Process -Name "kabusapi" -ErrorAction SilentlyContinue
```

判定:
| インストール | プロセス | 対応 |
|---|---|---|
| あり | 起動中 | ✅ Step 2 へ |
| あり | 停止中 | 「kabu Station を起動してください」案内 → 利用者起動後に再確認 |
| なし | — | 「kabu Station が見つかりません。auカブコム証券からダウンロード・インストールしてください」 |

### Step 2: API 設定の有効化確認

```
Claude Code 発話:
「kabu Station GUI で「設定」→「API 設定」タブを開いてください。
以下の項目を確認:
  1. 「API を利用する」のチェックが ON
  2. 本番モード / 検証モードのパスワードがそれぞれ設定済
  3. ポート設定: 本番 18080、検証 18081 (デフォルト)
完了したら yes と入力してください。」
```

### Step 3: モード選択

```
Claude Code 発話:
「どちらのモードで運用しますか?
  1. 検証 (Verification, port 18081) — 実発注なし、テスト用
  2. 本番 (Production, port 18080)   — 実際の取引

初回は 1 (検証) を強く推奨します。
本番モードに切り替えるのは、検証で十分動作確認した後にしてください。」
```

選択を `appsettings.Local.json` の `Kabu.Mode` に反映:
```powershell
$config = Get-Content "$env:LOCALAPPDATA\N225BrokerBridge\appsettings.Local.json" -Raw | ConvertFrom-Json
$config.Kabu.Mode = if ($selection -eq 1) { "Verification" } else { "Production" }
$config | ConvertTo-Json -Depth 10 | Out-File "$env:LOCALAPPDATA\N225BrokerBridge\appsettings.Local.json" -Encoding UTF8
```

### Step 4: パスワード入力 (UI 経由で実施)

```
Claude Code 発話:
「kabu API パスワードはアプリ UI 側で入力してください (DPAPI 暗号化のため)。
  1. ブリッジ UI を起動
  2. メニュー: ファイル → 設定
  3. kabu API パスワード欄に入力
  4. 保存

入力完了後、yes と返答してください。」
```

### Step 5: Token 取得テスト

利用者の確認後:
```powershell
$port = if ($mode -eq "Production") { 18080 } else { 18081 }
$apiPwd = Read-Host -AsSecureString "kabu API パスワードを入力 (画面に出ません、テスト目的のみ)"
$bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($apiPwd)
$plainPwd = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
[System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)

try {
    $resp = Invoke-RestMethod -Uri "http://localhost:$port/kabusapi/token" `
        -Method Post -Body (@{ APIPassword = $plainPwd } | ConvertTo-Json) `
        -ContentType "application/json" -TimeoutSec 5
    if ($resp.Token) { "✅ Token 取得成功" } else { "❌ Token が返ってきません" }
} catch {
    "❌ Token 取得失敗: $($_.Exception.Message)"
}
$plainPwd = $null
```

失敗パターン:
- `Connection refused`: kabu Station 起動していない or API 未有効化
- HTTP 401 / Unauthorized: パスワード違い
- Timeout: ファイアウォール / kabu Station 起動直後 (~30 秒待つ)

---

## kabu API のハマりポイント (絶対遵守)

新ブリッジ構築過程で蓄積された 8 ハマりを以下に集約。
詳細は [`docs/adapters/kabu.md`](../../docs/adapters/kabu.md) (TBD) を参照。

### 1. BidPrice / AskPrice が逆命名

kabu API の `BidPrice` = **売り板の最良気配** (=通常の ASK)、`AskPrice` = **買い板の最良気配** (=通常の BID)。
**世界標準と完全に逆**。

```
kabu API: BidPrice (売り板) ←→ 通常: ASK
kabu API: AskPrice (買い板) ←→ 通常: BID
```

ブリッジ内部では既に補正済 (UI で正しい BID/ASK を表示) だが、API 仕様書を読むときは混乱しないこと。

### 2. Side コード (1=売 / 2=買、逆順)

kabu の `/sendoder` API の `Side` パラメータ:
- `"1"` = **売** (Sell)
- `"2"` = **買** (Buy)

世界標準 (FIX) では `1=Buy, 2=Sell` なので逆。ブリッジ内部で変換済。

### 3. Product コード (0=現物, 1=信用, 2=先物, 3=オプション)

`Product` パラメータ:
- `0` = 現物
- `1` = 信用
- `2` = 先物 ← **N225 ミニはこれ**
- `3` = オプション

新ブリッジは `2 (先物)` 固定で運用。

### 4. FrontOrderType の数値 (株式と先物で別系統)

- 株式: `10` = 成行、`20` = 指値、`30` = 逆指値、...
- 先物: `120` = 成行、`20` = 指値、`30` = 逆指値、`13` = 対当値段成行 (BestMarket)、...

**先物用に正しいコードを使うこと**。

### 5. Exchange は時刻判定

`Exchange` パラメータは「どの取引市場で発注するか」:
- 日中 (8:45-15:15): `1` (OSE 日中)
- 夜間 (16:30-翌5:55): `2` (OSE ナイト)

時刻に応じてブリッジが自動判定。

### 6. DerivMonth (限月) は SQ 後に切替必要

`DerivMonth` パラメータ (例: `"202606"`) は満期月。
**毎月第 2 金曜 (SQ 日) で次月限に切替**しないと、満期後に存在しない限月で発注して拒否される。

KengetsuLib (限月自動制御) でブリッジが対応。

### 7. Token は Singleton 必須・8 時間期限

`/token` API で取得した Token はプロセス全体で 1 つだけ保持・使い回す。
複数取得すると古い方が無効化される。8 時間で期限切れ、再取得が必要。

新ブリッジは `TokenStore` で Singleton 管理 + 自動再取得実装済。

### 8. 約定明細の `Records` が null になる場合

`/orders` API の応答で `Records` (約定明細) が `null` になることがある (未約定時、または API 仕様の癖)。
`null` チェックを必ず挟む。

---

## 設計メモ (開発者向け)

### このスキルの設計原則

1. **パスワード入力は SecureString**: メモリから即時クリア
2. **GUI 操作の案内は具体的に**: 「設定タブの API 設定を ON」等、利用者が迷わない表現
3. **失敗時は次の確認 Step を明示**: 「kabu Station 起動」→「API 有効化」→「パスワード」の順で切り分け
4. **モード切替は本番モードへの遷移を確認**: 検証 → 本番への切替時は「実発注になります、続けますか?」を必ず尋ねる

### TBD

- [ ] kabu Station バージョン検出 (API 仕様変更追従用)
- [ ] kabu Station 自動起動 (利用者承認後)
- [ ] パスワード忘れ時の auカブコム証券リカバリ手順案内

### 関連ファイル

- `.claude/commands/setup.md` Step 5: このスキル呼び出し元
- `.claude/commands/diagnose.md` 症状 4: 同上
- `docs/adapters/kabu.md`: kabu API 詳細仕様 (TBD)

### バージョン

- v0.1.0 (2026-05-22、初版ドラフト)
