namespace N225BrokerBridge.Application.Signals;

/// <summary>
/// 外部 (TradingView Webhook 等) から受信したシグナルの素データ。
///
/// インフラ層 (TcpWebhookServer 等) でブローカー外部 JSON をパースしてこの型にバインドする。
/// Application 層はこの DTO を起点にドメインロジックを動かす。
///
/// 現 N225OrderBridge の TcpClientModel.cs から typo・冗長性を除いてクリーンに再定義。
/// </summary>
public sealed record SignalPayload(
    string AlertName,
    int Interval,
    string OrderAction,            // "buy" / "sell"
    string MarketPosition,         // "long" / "short" / "flat"
    string PrevMarketPosition,     // "long" / "short" / "flat"
    int OrderContracts,            // 注文枚数 (新規) または返済要求枚数
    int MarketPositionSize,        // シグナル発火後の建玉サイズ
    int PrevMarketPositionSize,    // シグナル発火前の建玉サイズ
    decimal OrderPrice,            // 戦略指示価格 (0 なら成行)
    // ⚠️ SymbolTicker は **発注先銘柄の決定には使わない** (運用ルール)。
    //   TV のチャート銘柄 (例 "OSE:NK225M1!") がそのまま入るが、SignalHandler は
    //   IAutoTradeInstrumentProvider 経由でブリッジ選択中の銘柄コード (kabu 数値コード) を使う。
    //   ここに残しているのは:
    //     (a) ログ・診断用に「TV 側で何の銘柄を見て送ってきたか」を記録するため
    //     (b) 将来 TV シンボルとブリッジ選択銘柄の不一致を警告ログに残したくなった時のため
    //   詳細: docs/architecture.md §3.5
    string SymbolTicker,           // 銘柄 (TV: "OSE:NK225M1!" 等、発注には使わない)
    string? Passphrase);           // パスフレーズ認証 (null/empty = 認証スキップ)
