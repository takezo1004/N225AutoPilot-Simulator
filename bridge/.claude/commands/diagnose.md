# /diagnose — トラブル時の自動診断

N225BrokerBridge が想定通り動かない時、原因を切り分けて修復案を提示するコマンドです。

> ⚠️ **このコマンドはドラフト (v0.1.0) です。テスター環境での動作確認が必要です。**

## 目的

「アラートが届かない」「発注されない」「起動できない」等の症状から、原因を自動で絞り込み、利用者の承認後に修復を実行する。

## 利用シーン

| シーン | 期待する流れ |
|---|---|
| `/verify` で FAIL が出た | `/diagnose` で原因深掘り → 修復 |
| TradingView 戦略から発注されない | 自動売買 OFF / 戦略未登録 / kabu 接続不能のいずれかを切り分け |
| 朝起動したら動かない | ブリッジ / kabu / cloudflared のどれが止まっているか自動判定 |
| エラー画面が出た | 直近ログ + スタックトレースから事象特定 |

---

## 実行フロー

### Step 0: 症状ヒアリング

```
Claude Code 発話:
「N225BrokerBridge の自動診断を開始します。
症状を 1 つ選んでください:

  1. アラートが届かない (Webhook 不達)
  2. 発注されない (Webhook は届くが注文が出ない)
  3. 起動できない (アプリ起動時にエラー)
  4. kabu Station 接続エラー
  5. その他 / 不明 (全項目自動スキャン)

番号で回答してください。」
```

選択に応じて深掘り Step へ分岐。

---

### Step 1: 共通スキャン (全症状で実行)

並列で以下を実行・結果を内部蓄積:

```powershell
# A. プロセス確認
$bridge = Get-Process -Name "N225BrokerBridge.UI" -EA SilentlyContinue
$kabu = Get-Process -Name "kabusapi" -EA SilentlyContinue
$cf = Get-Process -Name "cloudflared" -EA SilentlyContinue

# B. ポートリッスン状況
$port8001 = Get-NetTCPConnection -LocalPort 8001 -State Listen -EA SilentlyContinue
$port18080 = Get-NetTCPConnection -LocalPort 18080 -State Listen -EA SilentlyContinue
$port18081 = Get-NetTCPConnection -LocalPort 18081 -State Listen -EA SilentlyContinue

# C. 設定ファイル存在確認
$appSettings = Test-Path "$env:LOCALAPPDATA\N225BrokerBridge\appsettings.Local.json"
$cfConfig = Test-Path "C:\SPB_DATA\.cloudflared\config.yml"

# D. 直近ログのエラー抽出 (過去 24h)
$logDir = "$env:LOCALAPPDATA\N225BrokerBridge\logs"
$recentLogs = Get-ChildItem $logDir -Filter "*.log" |
              Where-Object { $_.LastWriteTime -gt (Get-Date).AddDays(-1) }
$errors = $recentLogs | ForEach-Object {
    Select-String -Path $_.FullName -Pattern "ERR |FATAL |Exception" -SimpleMatch |
        Select-Object -Last 5
}
```

---

### Step 2: 症状別ディスパッチ

#### 症状 1: アラートが届かない

```
チェック順序:
A. ブリッジプロセス起動中?       → NO なら「ブリッジを起動してください」
B. ポート 8001 リッスン中?       → NO なら「ブリッジは起動しているが Webhook が初期化されていない」→ ログ要確認
C. cloudflared プロセス起動中?   → NO なら「Cloudflare Tunnel が停止しています」→ 自動起動オプション
D. Cloudflare config.yml 存在?  → NO なら docs/troubleshooting.md §1 案内
E. config.yml に httpHostHeader: localhost 含まれる? → NO なら 400 Invalid Hostname の典型例
F. ローカル POST テスト (http://localhost:8001/webhook/) → FAIL なら認証/passphrase 問題
```

各 NO/FAIL ごとに修復案を提示:
```
Claude Code 発話:
「原因候補: <症状名>
推奨対処: <具体的手順>
自動修復を実行しますか? (yes / no / 自分で対処)」
```

`yes` → スクリプト実行。`no` → 手順だけ案内。

---

#### 症状 2: 発注されない

```
チェック順序:
A. Webhook 受信ログに対象戦略の "Received" 記録あり? → NO なら症状 1 へ戻る
B. 受信後 "Ignored_AutoTradeDisabled" ログ?       → YES なら「自動売買トグル OFF が原因」
C. 受信後 "Ignored_unknown_strategy" ログ?         → YES なら「戦略未登録」→ 戦略管理画面誘導
D. 受信後 "Ignored_KabuApiError" ログ?             → YES なら kabu 接続問題 → 症状 4 へ
E. "OrderRejected" ログ?                             → YES なら kabu 側拒否 → ログ内容から個別判断
```

最も多い原因 (B, C) を最初に確認するよう発話順を調整。

---

#### 症状 3: 起動できない

```
チェック順序:
A. .NET 8 Desktop Runtime インストール済?
   → dotnet --list-runtimes で Microsoft.WindowsDesktop.App 8.x 確認
   → NO なら https://dotnet.microsoft.com/download/dotnet/8.0 案内

B. appsettings.Local.json 存在 + JSON valid?
   → NO なら /setup の Step 4 から再実行
   → 壊れていれば backup 取って再生成

C. DPAPI 復号失敗?
   → 別 PC からコピーした設定は復号不能 (DPAPI はユーザー固有)
   → → /setup で再生成

D. ポート 8001 が他プロセスに使用されている?
   → Get-NetTCPConnection -LocalPort 8001 でプロセス特定 → 停止 or ポート変更

E. Windows Event Log にアプリ例外?
   → Get-EventLog -LogName Application -Source ".NET Runtime" -Newest 5
```

---

#### 症状 4: kabu Station 接続エラー

```
チェック順序:
A. kabu Station プロセス起動中?  → NO なら起動案内
B. API 設定有効化済?              → kabu GUI で確認 (利用者操作)
C. 本番/検証モード一致?           → appsettings.Local.json と kabu GUI のモードを照合
D. Token 取得テスト成功?          → /verify Step 3 と同じ
E. ファイアウォール経路確認       → Test-NetConnection -ComputerName localhost -Port 18080/18081
```

---

#### 症状 5: その他 (全項目スキャン)

`/verify` を全 Step 実行 + 共通スキャン結果を統合 → 異常項目を全リストアップ。

---

### Step 3: 修復候補一覧 + 利用者確認

問題が 1 つでも見つかった場合:
```
Claude Code 発話:
「以下の問題が検出されました:

  ❌ 問題 A: <概要>
     原因候補: <具体>
     修復: <手順 or スクリプト>
     自動実行可能: yes/no

  ❌ 問題 B: ...

順に対処しますか? どれから? (A / B / 全部 / 自分で)」
```

利用者選択に応じて修復スクリプトを実行。

---

### Step 4: 修復後の再検証

修復が 1 つでも行われたら、自動で `/verify` 相当の最小チェックを再実行:
- ブリッジプロセス起動中?
- ポート 8001 リッスン中?
- kabu Token 取得可?
- ローカル Webhook 200?

全 PASS → 「問題が解消されました」
まだ FAIL → 「以下の項目が残っています」→ ループ or troubleshooting.md 該当節案内

---

### Step 5: 完了報告

```
Claude Code 発話:
「診断完了。
  検出: N 件
  自動修復: M 件
  手動対処要: K 件

残課題があれば docs/troubleshooting.md の該当節を参照してください。
それでも解決しない場合は GitHub Issue で <連絡先 TBD> までご連絡ください。」
```

---

## 自動修復の安全装置

以下の操作は **必ず利用者確認後に実行**:
- プロセス停止 (kabu Station / cloudflared)
- 設定ファイル書き換え
- ファイアウォール規則変更
- レジストリ操作 (基本的に行わない)

以下は **確認なしで実行可** (副作用なし):
- プロセス起動状態の読み取り
- ログ読み取り
- ネットワーク疎通テスト (HEAD/GET)
- 設定ファイルの内容読み取り

---

## 設計メモ (開発者向け)

### このコマンドの設計原則

1. **症状から逆引き**: 「ログを見て」ではなく「症状を教えてくれ」から始める (利用者にやさしい)
2. **失敗候補は確率順に提示**: よくある原因 (自動売買 OFF / 戦略未登録) を最初に
3. **修復は段階的**: 1 件直したら再検証 → 残り再評価 (一気に複数いじって何が効いたか分からない、を避ける)
4. **秘密情報は表示しない**: ログ抽出時 passphrase / Token / パスワードはマスク

### 連動ファイル

- `docs/troubleshooting.md`: 詳細な症状別対処手順 (このコマンドから参照)
- `.claude/skills/kabu-config/`: kabu 関連修復の skill 化候補
- `.claude/skills/cloudflare-tunnel/`: Cloudflare 関連修復の skill 化候補

### TBD

- [ ] 修復スクリプトを skill 化 (本コマンドはディスパッチに専念)
- [ ] 利用者の症状報告から AI で症状分類 (自然言語入力対応)
- [ ] 過去の `/diagnose` 結果ログ化 (再発検出)

### バージョン

- v0.1.0 (2026-05-22、初版ドラフト)
