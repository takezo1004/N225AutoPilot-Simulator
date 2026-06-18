# N225BrokerBridge 設計ドキュメント索引

**最終更新**: 2026-05-26

本フォルダは N225BrokerBridge の**設計・仕様・運用ドキュメントの正本置場**です。

迷ったらまずこの README から該当ファイルへ。配置ルールは `../../docs/DOCUMENT_MAP.md` に従う。

---

## 📚 ドキュメント一覧

### 設計の三層 (要件 → 基本設計 → 詳細設計)

| 階層 | ファイル | 目的 |
|---|---|---|
| **要件** | [requirements.md](./requirements.md) | システムが**何のために**存在するか / 機能要件 F-NN / 非機能要件 NF-X1 |
| **基本設計** | [functional-spec.md](./functional-spec.md) | 機能を**画面 / 入出力 / 操作フロー**の粒度で具体化 |
| **基本設計** | [architecture.md](./architecture.md) | DDD 4 層 + 技術選定の全体像 |
| **基本設計** | [context-map.md](./context-map.md) | 境界づけられたコンテキスト 5 件 |
| **基本設計** | [ubiquitous-language.md](./ubiquitous-language.md) | ユビキタス言語辞書 |
| **基本設計** | [webhook-api-spec.md](./webhook-api-spec.md) | Webhook エンドポイントの完全仕様 |
| **基本設計** | [data-spec.md](./data-spec.md) | 永続化ファイル全 6 種のスキーマ |
| **詳細設計** | [domain-model.md](./domain-model.md) | 集約・エンティティ・値オブジェクト・イベント詳細 |
| **詳細設計** | [class-design.md](./class-design.md) | Application / Infrastructure / UI の主要クラス |
| **詳細設計** | [sequence-diagrams.md](./sequence-diagrams.md) | 13 シナリオ + エラー系のシーケンス |
| **詳細設計** | [state-machines.md](./state-machines.md) | Order / Position / 各コンポーネントの状態遷移 |
| **詳細設計** | [mainwindow-layout.md](./mainwindow-layout.md) | MainWindow.xaml レイアウト構造 |

### 実装・運用支援

| ファイル | 目的 |
|---|---|
| [adapters/kabu.md](./adapters/kabu.md) | kabu API 実装ノート (BID/ASK 逆名・8 ハマりポイント) |
| [dev-rules.md](./dev-rules.md) | 開発ルール (旧コード参照・kabu リファレンス必読) |
| [test-spec.md](./test-spec.md) | テスト戦略・カバレッジ・主要シナリオ |
| [operations.md](./operations.md) | 日々の起動・停止・バックアップ・障害対応 |
| [troubleshooting.md](./troubleshooting.md) | 症状別詳細手順 |
| [demo-mode.md](./demo-mode.md) | `--demo` 起動の仕様 (スクショ撮影専用) |
| [simulator-mode.md](./simulator-mode.md) | `--simulator` 起動の仕様 (動作テスト・配布デモ用、MockBrokerAdapter) |
| [roadmap.md](./roadmap.md) | 未実装機能・将来拡張 |

---

## 🗺 用途別ナビゲーション

### 「このシステムは何をするものか」を知りたい

→ [requirements.md](./requirements.md) §3-§4 → [architecture.md](./architecture.md) §1

### 「新しい機能を追加したい」

1. [requirements.md](./requirements.md) に要件追加 (F-NN)
2. [functional-spec.md](./functional-spec.md) で機能化
3. 影響範囲を [class-design.md](./class-design.md) で確認
4. [sequence-diagrams.md](./sequence-diagrams.md) に新シナリオ追加
5. [domain-model.md](./domain-model.md) を更新 (Domain 層に変更がある場合)
6. [test-spec.md](./test-spec.md) にテスト追加

### 「不具合を直したい」

1. [troubleshooting.md](./troubleshooting.md) で症状を検索
2. [sequence-diagrams.md](./sequence-diagrams.md) で該当フローを確認
3. [state-machines.md](./state-machines.md) で状態異常を確認
4. [test-spec.md](./test-spec.md) の退行防止テストに追加

### 「Webhook を設定したい」

→ [webhook-api-spec.md](./webhook-api-spec.md) → `../../docs/webhook_full_setup_manual.md`

### 「kabu API でハマった」

→ [adapters/kabu.md](./adapters/kabu.md) → [dev-rules.md](./dev-rules.md) §2

### 「明日朝の運用手順を知りたい」

→ [operations.md](./operations.md) §2

### 「永続化ファイルをバックアップしたい」

→ [data-spec.md](./data-spec.md) → [operations.md](./operations.md) §8

### 「Claude Code に作業させたい」

→ `../CLAUDE.md` (購読者向け命令書) → [dev-rules.md](./dev-rules.md)

---

## 📐 ドキュメント間の依存

```
[requirements.md]   ← トップレベル
       │
       ├──→ [functional-spec.md]    機能仕様
       │         │
       │         ├──→ [webhook-api-spec.md]   外部 IF
       │         ├──→ [mainwindow-layout.md]  UI 物理
       │         └──→ [data-spec.md]          永続化
       │
       └──→ [architecture.md]       技術選定
                 │
                 ├──→ [context-map.md]        DDD 境界
                 ├──→ [ubiquitous-language.md] 用語
                 ├──→ [domain-model.md]       詳細
                 ├──→ [class-design.md]       詳細
                 │     │
                 │     ├──→ [sequence-diagrams.md]  動的視点
                 │     └──→ [state-machines.md]    状態
                 │
                 └──→ [adapters/kabu.md]     具体実装

[test-spec.md]      ← class-design.md / domain-model.md を参照
[operations.md]     ← functional-spec.md / data-spec.md を参照
[troubleshooting.md] ← sequence-diagrams.md / operations.md を参照
[demo-mode.md]      ← functional-spec.md を参照
[simulator-mode.md] ← class-design.md / domain-model.md / webhook-api-spec.md を参照
[roadmap.md]        ← requirements.md / class-design.md を参照
[dev-rules.md]      ← (横断、すべての設計判断の前提)
```

---

## ✏ ドキュメント更新ルール

### 更新タイミング

| 種別 | タイミング |
|---|---|
| 機能追加 / 削除 | requirements.md → functional-spec.md → class-design.md → sequence-diagrams.md |
| バグ修正 (動作変更を伴う) | 影響範囲のドキュメント + test-spec.md |
| クラス名変更 / リファクタ | class-design.md / domain-model.md |
| 永続化スキーマ変更 | data-spec.md |
| Webhook 仕様変更 | webhook-api-spec.md |
| 起動 / 停止手順変更 | operations.md |

### バージョニング

各ドキュメントは:

- `バージョン: X.Y.Z` を冒頭に明記
- パッチ (typo / 軽微) → +0.0.1
- マイナー (機能追加 / 仕様変更) → +0.1
- メジャー (構造変更) → +1.0
- 末尾の `## 変更履歴` テーブルに 1 行追加

### 不変ルール

- **新規ドキュメントを作る前に DOCUMENT_MAP.md を確認** (`../../docs/DOCUMENT_MAP.md`)
- 同じ用途のドキュメントを複数作らない (1 用途 1 ファイル)
- 既存運用の場所変更は必ずユーザー確認

---

## 🧭 関連 (リポジトリ外)

- 親 README: [`../README.md`](../README.md)
- Claude Code ガイド: [`../CLAUDE.md`](../CLAUDE.md)
- リポジトリ全体のドキュメント地図: [`../../docs/DOCUMENT_MAP.md`](../../docs/DOCUMENT_MAP.md)
- Webhook 構築マニュアル: [`../../docs/webhook_full_setup_manual.md`](../../docs/webhook_full_setup_manual.md)
- 本番チェックリスト: [`../../docs/production_checklist.md`](../../docs/production_checklist.md)
- 監視方針: [`../../docs/monitoring_policy.md`](../../docs/monitoring_policy.md)
- テスト方針 (全体): [`../../docs/test_policy.md`](../../docs/test_policy.md)
