# MainWindow.xaml レイアウト仕様 & 調整箇所一覧

このドキュメントは [`src/N225BrokerBridge.UI/Views/MainWindow.xaml`](../src/N225BrokerBridge.UI/Views/MainWindow.xaml) の構造と、UI 微調整の際に変更すべき箇所をまとめたものです。手動で XAML を直接編集する際の参照用。

> **注意**: Visual Studio の XAML デザイナを操作すると意図しない `Grid.ColumnSpan` / `Margin` / `HorizontalAlignment` / `Width` 等が自動付加される。XAML はテキスト直接編集を推奨。

---

## 1. 全体レイアウト構造

```
┌──────────────────────────────────────────────────────────────────────┐
│ [ファイル 戦略 表示 ヘルプ]      [N225 Broker Bridge]   [— ☐ ×] │ ← Row 0 (Custom Title Bar)
├──────────────────────────────────────────────────────────────────────┤
│ ブローカー: 接続中  Webhook: 受信中  状態: ...  [自動売買 ☐] [更新] │ ← Row 1 (Status bar)
├──────────────────────────────────────────────────────────────────────┤
│ ┌──────────────┐ ┌─────────┐ ┌────────────────────────────────────┐ │
│ │ 手動発注     │ │ 現在値  │ │ 戦略一覧                            │ │
│ │ 銘柄         │ │ BID/ASK │ │ ...                                 │ │
│ │ 注文タイプ   │ │         │ │                                     │ │  ← Row 2 (Main content)
│ │ ...          │ │         │ ├────────────────────────────────────┤ │
│ │ [買][売]     │ │         │ │ 建玉一覧                            │ │
│ │ [返][キャ]   │ │         │ │ ...                                 │ │
│ └──────────────┘ └─────────┘ ├────────────────────────────────────┤ │
│ ※左パネル                    │ 注文一覧                            │ │
│                              │ ...                                 │ │
│                              └────────────────────────────────────┘ │
├══════════════════════════════════════════════════════════════════════┤ ← Row 3 (GridSplitter)
│ ログ                                                                  │
│ ...                                                                   │  ← Row 4 (Log)
└──────────────────────────────────────────────────────────────────────┘
```

---

## 2. Window 属性

| 属性 | 現在値 | 役割 |
|---|---|---|
| `Width` | `1280` | ウィンドウ初期幅 |
| `Height` | `820` | ウィンドウ初期高さ |
| `MinWidth` | `1024` | 最小幅 (リサイズ下限) |
| `MinHeight` | `600` | 最小高さ (リサイズ下限) |
| `WindowStyle` | `None` | OS 標準タイトルバーを消す (自前タイトルバーのため) |
| `ResizeMode` | `CanResize` | リサイズ可能 |
| `ExtendsContentIntoTitleBar` | `False` | Wpf.Ui の TitleBar 拡張無効 |
| `WindowBackdropType` | `None` | Mica 効果無効 (`ExtendsContentIntoTitleBar=False` 時の必須) |
| `WindowCornerPreference` | `Round` | Windows 11 のみ角丸 |
| `WindowStartupLocation` | `CenterScreen` | 起動時に画面中央 |

> `ExtendsContentIntoTitleBar=False` のとき `WindowBackdropType` は **必ず `None`** にすること。Mica 等にすると `Cannot apply backdrop effect if ExtendsContentIntoTitleBar is false.` 例外で起動失敗する。

---

## 3. WindowChrome (カスタムタイトルバー)

| 属性 | 現在値 | 役割 |
|---|---|---|
| `CaptionHeight` | `48` | タイトルバーのドラッグ可能領域の高さ。タイトルバー Grid の `Height` と揃える |
| `ResizeBorderThickness` | `4` | ウィンドウ枠ドラッグでリサイズ可能な幅 |
| `CornerRadius` | `0` | (Win10 互換、Win11 は `WindowCornerPreference` が優先) |
| `GlassFrameThickness` | `0` | ガラス枠無効 |

---

## 4. Grid.RowDefinitions (外側 Grid)

| Row | 役割 | Height | MinHeight |
|---|---|---|---|
| **0** | Custom Title Bar (Menu + タイトル + システムボタン) | `Auto` | - |
| **1** | Status bar (ブローカー / Webhook / 状態 / 自動売買トグル) | `Auto` | - |
| **2** | Main content (左パネル + 右側グリッド群) | `*` | - |
| **3** | GridSplitter (Main と Log の境界、ドラッグ可) | `5` | - |
| **4** | Log (ログ一覧) | `280` | `120` |

---

## 5. Main content 内 RowDefinitions (右側 Grid)

`Grid.Column="2"` の内側 Grid:

| Row | 役割 | Height |
|---|---|---|
| **0** | Top row (Price + Strategies、横並び) | `Auto` |
| **1** | Positions (建玉一覧) | `*` |
| **2** | Splitter (建玉と注文の境界、ドラッグ可) | `5` |
| **3** | Orders (注文一覧) | `*` |

> Row 1 と Row 3 はどちらも `*` で **1:1 均等**。建玉と注文の比率を変えたい場合、片方を `2*` `1*` に変更すれば 2:1 等になる。

---

## 6. 縦サイズ調整箇所 (主要 7 箇所)

| # | 何を変える | 現在値 | 効果 | 注意 |
|---|---|---|---|---|
| **1** | Window 全体の高さ (`<ui:FluentWindow Height>`) | `820` | 大きく → 全体広く | `MinHeight=600` 以下にはできない |
| **2** | タイトルバー高さ (`<shell:WindowChrome CaptionHeight>`) | `48` | タイトルバー領域 | **3 箇所セット**で変更 (下記注意) |
| **3** | タイトルバー Grid (`<Grid Grid.Row="0" Height>`) | `48` | (#2 と同値) | #2 と揃える |
| **4** | システムボタン (`Style TargetType="Button" Setter Height`、2 箇所) | `48` | (#2 と同値) | #2 と揃える |
| **5** | Log エリア (`<RowDefinition Height>` Log 行) | `280` | 大きく → ログ広く・Main 狭く | `MinHeight=120` |
| **6** | 建玉一覧の最低高さ (`<Border Grid.Row="1" Padding="0" MinHeight>` 建玉) | `150` | 大きく → 建玉が最低限広く | 約 22px/行 + ヘッダー 26px |
| **7** | 注文一覧の最低高さ (`<Border Grid.Row="3" Padding="0" MinHeight>` 注文) | `150` | 大きく → 注文が最低限広く | 同上 |

### ⚠️ タイトルバー高さは 3 箇所セットで変更

`#2 #3 #4` (3 つの場所) を**同じ値に揃える**こと。例: 48 → 56 にするなら 3 箇所すべて 56 に。

---

## 7. 横サイズ調整箇所

| # | 何を変える | 現在値 | 効果 |
|---|---|---|---|
| **1** | Window 全体の幅 (`<ui:FluentWindow Width>`) | `1280` | 大きく → 全体広く |
| **2** | 左パネル (Manual order) の幅 (`<ColumnDefinition Width>` 左) | `340` (`MinWidth=280`) | 大きく → 左パネル広く |
| **3** | 右側コンテンツの最低幅 (`<ColumnDefinition Width>` 右) | `*` (`MinWidth=500`) | 右側の縮小限界 |
| **4** | Price (現在値/BID/ASK) パネル幅 (Main content 内 Top row の `<ColumnDefinition Width>`) | `220` | 大きく → 価格表示広く |
| **5** | 銘柄 ComboBox の幅 (Manual order panel 内 `<ColumnDefinition Width>`) | `150` | 大きく → 銘柄名広く |

---

## 8. Padding / Margin 調整箇所

| 場所 | 現在値 | 役割 |
|---|---|---|
| Status bar Border `Padding` | `8,4` | 縦 4px (ボタン文字確保) |
| Manual order panel Border `Padding` | `8` | 左パネル外周 |
| 価格 Border `Padding` | `6,4` | 価格表示の内側 |
| DataGrid ヘッダー (`GridHeaderStyle Padding`) | `8,2` | 戦略/建玉/注文すべてに適用 |
| セクションヘッダー (戦略/建玉/注文/ログ) `Padding` | `8,2` | 一覧の上部の見出し帯 |
| Main content Grid `Margin` | `4,4,4,0` | メイン領域の外周余白 |
| Log Border `Margin` | `4,2,4,4` | ログ領域の外周余白 |
| Menu `Padding` | `0` | メニュー外枠 (MenuItem が自前 Padding を持つので 0 で OK) |

---

## 9. ボタン / 入力欄のサイズ

| 場所 | 現在値 | 役割 |
|---|---|---|
| 買/売ボタン `Height` | `30` | 主要発注ボタン |
| 返済/キャンセルボタン `Height` | `28` | 副次操作ボタン |
| ボタン Grid の `RowDefinition Height="4"` | `4` | 買売 と 返済キャンセル の縦間隔 |
| ボタン Grid の `ColumnDefinition Width="6"` | `6` | ボタン同士の横間隔 |
| RadioButton (注文タイプ) `FontSize` | `13` | 対当/成行/指値/逆指 |
| RadioButton `Margin` (右側) | `0,0,12,0` | RadioButton 同士の横間隔 |

---

## 10. 色設定

| 場所 | 値 | 役割 |
|---|---|---|
| カスタムタイトルバー背景 | `#2A3340` | タイトルバー全体の塗り |
| 通常ボタン Hover 背景 | `#4A5568` | 最小化/最大化のホバー色 |
| 閉じるボタン Hover 背景 | `#E81123` | Windows 標準の赤 |
| DataGrid 選択行 背景 (`SelectableRowStyle`) | `#2D7CC0` | アクセントブルー |
| DataGrid 選択行 文字色 | `White` | 白文字 |
| DataGrid 非アクティブ選択時 (`InactiveSelectionHighlightBrushKey`) | `#2D7CC0` | アクティブ時と同色 (白く見える現象防止) |

---

## 11. フォント

| 場所 | FontFamily | FontSize | FontWeight |
|---|---|---|---|
| カスタムタイトルバー タイトル文字 | `Cascadia Code` | `13` | `SemiBold` |
| 「手動発注」見出し | (デフォルト) | `14` | `SemiBold` |
| RadioButton (注文タイプ) | (デフォルト) | `13` | (デフォルト) |
| DataGrid セル (`CompactCellStyle`) | (デフォルト) | (デフォルト) | (デフォルト) |
| DataGrid ヘッダー | (デフォルト) | `12` | `SemiBold` |
| 価格表示 (`PriceValueStyle`) | `Consolas` | `20` | `SemiBold` |
| ログ ListBox | `Consolas` | `12` | (デフォルト) |

---

## 12. ハマりポイント

### 12-1. XAML デザイナで誤操作したときの典型的な副産物

| 自動付加属性 | 影響 | 対処 |
|---|---|---|
| `Grid.ColumnSpan="3"` 等 | 列スパン誤指定 | 削除 |
| `Margin="0,0,0,397"` 等の巨大値 | レイアウト壊滅 | 削除 |
| `HorizontalAlignment="Right"` 等 | 配置ずれ | 削除 (`Stretch` がデフォルト) |
| `Width="1280"` 等の固定値 | リサイズ不可 | 削除 |
| `Grid.RowSpan="2"` 等 | 行スパン誤指定 | 削除 |

XAML テキスト直接編集を推奨。デザイナを開く必要がある場合、変更後に必ず XAML を見て不要属性をクリーンアップする。

### 12-2. Wpf.Ui の制約

- `WindowBackdropType` (Mica/Acrylic 等) は `ExtendsContentIntoTitleBar="True"` 必須
- `ui:TitleBar` の `Title` 文字は内部 Template で配置位置が固定、`VerticalAlignment="Center"` などのカスタマイズが効かない (このため自前 `WindowChrome` で実装している)
- `ui:FluentWindow` を継承する場合、`WindowStyle="None"` + `WindowChrome` は組み合わせ可能

### 12-3. ScrollViewer の扱い

現状 Manual order panel に `ScrollViewer` は無し。コンテンツがウィンドウ内に収まる前提。フィールド追加でハミ出した場合は `<ScrollViewer VerticalScrollBarVisibility="Auto">` で `<StackPanel>` を包む。

---

## 13. GridSplitter (ユーザー操作で動的に変えられる箇所)

| Splitter 位置 | ドラッグ操作 |
|---|---|
| **Main content / Log の境界** | Main を縦に大きく/小さく |
| **建玉一覧 / 注文一覧 の境界** | 建玉と注文の比率を変える |
| **左パネル / 右側 の境界** (列間) | 左パネルの幅を変える |

これらは XAML 値が「起動時のデフォルト」になり、実行中のドラッグで上書き可能。

---

## 14. 変更履歴

- 2026-05-21: 初版作成 (Wpf.Ui ui:TitleBar から自前 WindowChrome への移行完了時点)
