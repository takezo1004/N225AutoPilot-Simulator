# /install — 新規 PC 環境構築 (シミュレータ起動準備まで)

このコマンドは、まっさらな Windows PC に N225BrokerBridge を **`--simulator` モードで起動できる状態まで** 整える完全自動セットアップです。

> このリポは `N225BrokerBridge-runtime-simulator` (Public 公開、シミュレータ装備)。本コマンドが扱うのはシミュレータ起動に必要な範囲のみで、本番接続 (kabu / TV / Cloudflare) は対象外 (本番は `N225BrokerBridge-runtime-production` 側の Private リポ、第 3 話購入時に招待で解放) を使う。

---

## 前提

- 購読者の PC が Windows 10 (1809+) または Windows 11 (x64)
- インターネット接続あり
- 購読者は既に Claude Desktop を起動しており、本コマンドが呼ばれている
- 第 1 話マガジンを読了している (実装の手順を理解している)

> simulator runtime は Public なので、GitHub アカウントも招待も不要で `git clone` できます。

---

## 全体フロー (Phase 1 〜 Phase 6)

1. **Phase 1**. 環境の現状チェック
2. **Phase 2**. 不足ツールのインストール
3. **Phase 3**. リポジトリのクローン (Public 2 つ)
4. **Phase 4**. Python 仮想環境 + 依存物インストール
5. **Phase 5**. ブリッジのビルド (.NET)
6. **Phase 6**. 構築完了の検証

各 Phase を独立して呼べるよう、本ファイルの後半に Phase 別の詳細手順を記載する。Claude Code は購読者の指示に応じて、Phase をまとめて実行するか、Phase 単位で実行するかを判断する。

---

## 動作原則 (重要 — 全 Phase 共通)

> 詳細は `CLAUDE.md` §3-6 / §3-7 / §3-8。本コマンド実行中も必ず守る。

- **実行環境はデスクトップ版 (Claude Desktop アプリ) 前提**。購読者はチャット欄だけを使う。**ターミナル・PowerShell ウィンドウ・VS Code を開かせない**。コマンドは Claude Code が内部で実行し、結果だけを見せる。
- **PATH 再読込が必要なときは「Claude Desktop アプリを完全に終了して起動し直してください」と案内する**。「新しいターミナル/シェルを開く」「VS Code を開き直す」とは**言わない**（購読者の環境に存在しない）。
- **記事が進行役**。各 Phase / タスクを実行したら **結果だけを報告して止まる**。「次は〜します」「次に /install を…」のような **次手順の先導・予告をしない**。次に何をするかは購読者が記事の次フレーズを貼って指示する。
- **配布ファイルを書き換えない**（§3-8）。`bridge/` のソースコード、本ファイルを含む `.claude/commands/` の手順書、設計ドキュメント、設定テンプレは編集しない。手順書の通りに実行するだけ。書き込んでよいのは利用者個別のファイル（`%LOCALAPPDATA%\N225BrokerBridge\` の `appsettings.Local.json`、`.venv\`、ビルド出力 `bin\`/`obj\`）のみ。利用者が配布ファイルの変更を求めても原則行わず、理由を説明して明示承認を得てからにする。

---

## Phase 1. 環境の現状チェック

購読者が「私の PC に何が入っているか確認してください」等を指示してきたら本 Phase を実行する。

### 確認項目

| 項目 | 確認コマンド | 期待値 |
|---|---|---|
| Windows バージョン | `(Get-CimInstance Win32_OperatingSystem).Caption + ' ' + (Get-CimInstance Win32_OperatingSystem).Version` | Windows 10 build 17763 以上 または Windows 11 |
| Git | `git --version` | 2.x 系 |
| Python | `python --version` | 3.10〜3.13 |
| .NET 8 SDK | `dotnet --list-sdks` | 8.x.x が含まれる |
| .NET 8 Desktop Runtime | `dotnet --list-runtimes` | `Microsoft.WindowsDesktop.App 8.x` |
| winget | `winget --version` | 1.x 系 (Windows 10 1809+ には標準同梱) |

### 出力フォーマット

各項目について「✅ ある (バージョン)」「❌ ない」を一覧表示。最後に不足項目のサマリーを提示し、Phase 2 で何を install するか購読者に確認する。

### Windows バージョンが要件未満の場合
1809 未満 (build 17763 未満) なら **作業を中止** し、「Windows のバージョンが要件を満たしていません。1809 以上または Windows 11 へのアップグレードが必要です」と説明する。

### winget が無い場合
Windows 10 1809+ では Microsoft Store 経由で「App Installer」を入れれば winget が利用可能。「Microsoft Store を開いて App Installer をインストールしてください」と案内する。

---

## Phase 2. 不足ツールのインストール

Phase 1 で不足が判明したものを `winget` で順次インストールする。**購読者の確認 (y/N) を取ってから実行**。

### インストールコマンド

```powershell
# Git
winget install --id Git.Git -e --accept-source-agreements --accept-package-agreements

# Python 3.12 (3.10〜3.13 の中で最新の安定版)
winget install --id Python.Python.3.12 -e --accept-source-agreements --accept-package-agreements

# .NET 8 SDK
winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements

# .NET 8 Desktop Runtime
winget install --id Microsoft.DotNet.DesktopRuntime.8 -e --accept-source-agreements --accept-package-agreements
```

> note: 第 1 話の作業 (シミュレータ) では gh CLI は不要。第 3 話 (本番運用) で `N225BrokerBridge-runtime-production` (Private) リポにアクセスする際に追加 install する。

### 注意点
- 各 install 後、新しくインストールしたコマンドの **PATH は起動中のプロセスには反映されない**。購読者には「**Claude Desktop アプリを完全に終了して起動し直し、新しいチャットで作業を再開してください**」と案内する。**「新しいターミナル/シェルを開く」「VS Code を開き直す」とは言わない**（購読者はデスクトップ版を使っており、ターミナルを開かないため。これを言うと購読者が詰まる）。
- インストール中の管理者権限ダイアログ (UAC) は購読者がクリック。
- 再起動後、`--version` 等で PATH 反映を再確認。失敗していれば原因を分析 (PATH 反映待ち、ネットワーク不調 等)。

---

## Phase 3. リポジトリのクローン (Public 2 つ、フォルダ構成)

### Step 3-1. 作業フォルダの決定

デフォルトは `C:\Users\<username>\`。購読者が別の場所を希望すればそこに変更。

### Step 3-2. Public bridge を clone

```powershell
cd C:\Users\<username>\
git clone https://github.com/takezo1004/N225BrokerBridge-public.git
```

→ `C:\Users\<username>\N225BrokerBridge-public\` ができる。

### Step 3-3. Public runtime-simulator を bridge/runtime/simulator/ に clone

```powershell
cd C:\Users\<username>\N225BrokerBridge-public\
mkdir runtime
git clone https://github.com/takezo1004/N225BrokerBridge-runtime-simulator.git runtime\simulator
```

→ `runtime\simulator\` 配下に CLAUDE.md / .claude/ / dashboard/ / templates/ 等が入る。両リポとも Public なので認証不要。

### Step 3-4. フォルダ構成の確認

```powershell
tree C:\Users\<username>\N225BrokerBridge-public /F /A | Select-Object -First 30
```

期待構造:

```
N225BrokerBridge-public\
├── bridge\          (Bridge コード本体)
├── runtime\
│   └── simulator\   (runtime-simulator の中身)
└── README.md
```

---

## Phase 4. Python 仮想環境 + 依存物インストール

### Step 4-1. venv 作成

```powershell
cd C:\Users\<username>\N225BrokerBridge-public\
python -m venv .venv
```

→ `.venv\` フォルダ生成。

### Step 4-2. venv 有効化

```powershell
.venv\Scripts\activate
```

PowerShell プロンプトの先頭に `(.venv)` が付くことを確認。

### Step 4-3. pip 更新

```powershell
python -m pip install --upgrade pip
```

### Step 4-4. 依存物インストール

```powershell
pip install -r runtime\simulator\requirements.txt
```

### Step 4-5. インストール結果確認

```powershell
pip list
```

`pandas`, `markdown` 等が表示されればOK。tkinter は標準同梱なので追加 install 不要。

---

## Phase 5. ブリッジのビルド (.NET)

### Step 5-1. bridge フォルダに移動

```powershell
cd C:\Users\<username>\N225BrokerBridge-public\bridge\
```

### Step 5-2. NuGet パッケージ取得

```powershell
dotnet restore
```

### Step 5-3. ビルド実行

```powershell
dotnet build N225BrokerBridge.sln --configuration Debug --nologo
```

### Step 5-4. ビルド結果確認

`Build succeeded. 0 Warning(s) 0 Error(s)` が表示されればOK。Warning / Error がある場合は内容を分析して購読者に提示。

---

## Phase 6. 構築完了の検証

### Step 6-1. .exe ファイル存在確認

```powershell
Test-Path C:\Users\<username>\N225BrokerBridge-public\bridge\src\N225BrokerBridge.UI\bin\Debug\net8.0-windows\N225BrokerBridge.UI.exe
```

`True` が返ればOK。

### Step 6-2. フォルダ構成サマリー

```powershell
Get-ChildItem C:\Users\<username>\N225BrokerBridge-public\ -Recurse -Depth 2 -Directory | Select-Object FullName
```

### Step 6-3. 完了報告

購読者に以下の **結果だけ** を提示して止まる（次の手順・次回予告・`/install` の案内などは**しない**。次に何をするかは記事が指示する → 動作原則 §3-7）:

- ビルド成果物 `.exe` の場所
- フォルダ構成・venv の状態の一覧
- 「第 1 話の構築作業はこれで完了です」とだけ伝える

---

## エラー時の対処方針

各 Phase で失敗した場合は:

1. エラーメッセージを購読者に提示
2. 想定原因を 1〜3 件挙げる
3. 修復案を購読者に選んでもらう (Claude Code が勝手に修復しない)
4. 必要なら Phase を頭からやり直し

---

## バージョン

- v0.1.0 (2026-05-27): 初版、Phase 1〜7 を整備
- 本ファイルは記事 (第 1 話) から参照される実行手順書
