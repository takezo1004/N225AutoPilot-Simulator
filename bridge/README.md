# N225BrokerBridge

日経225先物取引のためのマルチブローカー対応注文ブリッジ。

TradingView Webhook 等から受信した売買シグナルを、各証券会社の API に変換して発注し、ポジション・約定を統合管理する。

## 設計方針

- **言語**: C# / .NET 8 (LTS)
- **UI**: WPF + [WPF UI](https://github.com/lepoco/wpfui) ライブラリ (Fluent / Windows 11 風)
- **アーキテクチャ**: ドメイン駆動設計 (DDD) 4 層
  - Domain / Application / Infrastructure / UI
- **対応 OS**: Windows 10 / 11
- **マルチブローカー**: ブローカーアダプタ層で抽象化し、Day 1 から複数証券会社対応を想定

## ディレクトリ構成

```
N225BrokerBridge/
├── src/
│   ├── N225BrokerBridge.Domain/         # 集約 (Aggregate)・値オブジェクト・ドメインイベント
│   ├── N225BrokerBridge.Application/    # ユースケース・アプリケーションサービス
│   ├── N225BrokerBridge.Infrastructure/ # ブローカーアダプタ・リポジトリ・WebHook 受信
│   └── N225BrokerBridge.UI/             # WPF 画面・ViewModel (MVVM)
├── tests/
│   ├── N225BrokerBridge.Domain.Tests/
│   ├── N225BrokerBridge.Application.Tests/
│   └── N225BrokerBridge.Infrastructure.Tests/
├── docs/                                # 設計ドキュメント (→ docs/README.md が索引)
│   ├── README.md                        # 設計ドキュメント索引
│   ├── requirements.md                  # 要件定義 (F-NN / NF-X1)
│   ├── functional-spec.md               # 機能仕様 (基本設計)
│   ├── architecture.md                  # アーキテクチャ全体像 + 永続化方針
│   ├── context-map.md                   # 境界づけられたコンテキスト図
│   ├── ubiquitous-language.md           # ユビキタス言語辞書 (用語集)
│   ├── domain-model.md                  # ドメインモデル詳細
│   ├── class-design.md                  # Application/Infrastructure/UI 主要クラス
│   ├── sequence-diagrams.md             # 主要 13 シナリオのシーケンス
│   ├── state-machines.md                # Order/Position 等の状態遷移
│   ├── webhook-api-spec.md              # Webhook エンドポイント完全仕様
│   ├── data-spec.md                     # 永続化ファイル全 6 種のスキーマ
│   ├── test-spec.md                     # テスト戦略・主要シナリオ
│   ├── operations.md                    # 起動/停止/バックアップ/障害対応
│   ├── troubleshooting.md               # 症状別詳細手順
│   ├── demo-mode.md                     # --demo 起動の仕様
│   ├── mainwindow-layout.md             # MainWindow.xaml レイアウト
│   ├── dev-rules.md                     # 開発ルール (kabu API リファレンス必須、旧コード参照等)
│   ├── roadmap.md                       # 未実装・将来拡張事項 (優先度別 5 カテゴリ)
│   └── adapters/                        # ブローカー別 API 仕様メモ
│       └── kabu.md
├── .gitignore
├── README.md
└── N225BrokerBridge.sln
```

## 対応予定ブローカー

| ブローカー | API 種別 | ステータス |
|-----------|---------|----------|
| kabu (au カブコム証券) | REST + WebSocket (localhost:18080) | Phase 3 移植予定 |
| 楽天証券 | RSS (Excel RTD / COM) | Phase 5 |
| その他 (SBI / 松井 / 岡三 等) | 未調査 | 順次調査・追加 |

## 開発フェーズ

| Phase | 内容 | ステータス |
|-------|------|----------|
| 1 | 設計ドキュメント | ✅ 完了 |
| 2 | ソリューション骨格 (.sln + 4 プロジェクト) + ドメインモデル | ✅ 完了 |
| 3 | kabu アダプタ移植 | ✅ 完了 (検証ポート動作確認済 / 本番モード継続) |
| 4 | UI (WPF + WPF UI) — 設定/戦略管理/手動発注/建玉一覧/注文一覧 | ✅ 完了 |
| 5 | メタストア整備 (auto-positions.json / orders-metadata.json) | ✅ 完了 |
| 6 | pending 追跡型ポーリング (旧 OrderInquiryList 準拠) | ✅ 完了 |
| 7 | 楽天 RSS アダプタ | ❌ 未着手 ([roadmap §2.1](docs/roadmap.md)) |
| 8 | リスク管理層 | ❌ 未着手 ([roadmap §3.2](docs/roadmap.md)、運用前必須) |

詳細な未実装事項・優先度は [`docs/roadmap.md`](docs/roadmap.md) 参照。
**開発ルール**は [`docs/dev-rules.md`](docs/dev-rules.md) を必ず参照。
**設計ドキュメント全体の索引**は [`docs/README.md`](docs/README.md) を参照。

## 既存システムとの関係

- **N225OrderBridge** (現行・継続稼働): kabu 単独、WinForms、明日からの V7-7 fixed 戦略実機運用に使用
- **N225BrokerBridge** (本プロジェクト): 新装版、マルチブローカー、WPF、並行開発

両者は並行運用し、新ブリッジが安定動作するまで現行ブリッジを継続使用する。

## 関連プロジェクト

- [N225StrategyBuilder](../N225StrategyBuilder/) — 戦略バックテスト
- [N225SignalTrader](../N225SignalTrader/) — TV シグナル + Groq フィルター
- [N225McpServer](../N225McpServer/) — TradingView MCP サーバー
- [N225LLMAdvisor](../N225LLMAdvisor/) — LLM 朝分析

## 利用技術スタック (予定)

| カテゴリ | 採用 |
|---------|------|
| ランタイム | .NET 8 |
| UI | WPF + WPF UI |
| ロガー | Serilog |
| DI | Microsoft.Extensions.DependencyInjection |
| JSON | System.Text.Json |
| 非同期ストリーム | System.Reactive (Rx.NET) |
| HTTP | HttpClient (DI 化) |
| テスト | xUnit |
| ビルド | Visual Studio 2022 / MSBuild |
