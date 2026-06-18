namespace N225BrokerBridge.Infrastructure.Brokers.Kabu;

/// <summary>
/// kabu ステーション API 接続設定。appsettings.json から DI バインドする想定。
/// </summary>
public sealed class KabuOptions
{
    /// <summary>kabu ステーション REST API ベース URL (デフォルト: 本番)</summary>
    public string BaseUrl { get; set; } = "http://localhost:18080/kabusapi";

    /// <summary>WebSocket エンドポイント</summary>
    public string WebSocketUrl { get; set; } = "ws://localhost:18080/kabusapi/websocket";

    /// <summary>API パスワード (token 取得用、/token エンドポイント呼び出し時)。
    /// appsettings.Local.json で設定。</summary>
    public string? ApiPassword { get; set; }

    /// <summary>取引パスワード (注文発注・取消時の Password フィールドに使用、いわゆる「取引暗証番号」)。
    /// 旧 N225OrderBridge の「取引パスワード」相当。API パスワードとは別物。</summary>
    public string? OrderPassword { get; set; }

    /// <summary>注文発注 / 返済の HTTP タイムアウト (秒)。デフォルト 5 秒。</summary>
    public int OrderTimeoutSeconds { get; set; } = 5;

    /// <summary>照会系の HTTP タイムアウト (秒)。デフォルト 10 秒。</summary>
    public int QueryTimeoutSeconds { get; set; } = 10;

    /// <summary>金融商品コード (本ブリッジは日経225先物専用)。</summary>
    public int Product { get; set; } = 3;   // 1=現物, 2=信用, 3=先物, 4=OP

    /// <summary>
    /// /register・/board・/symbol で使う市場コード (デフォルト 2 = 日通し、24 時間 push 対象)。
    /// kabu API 仕様: 2=日通し, 23=日中(8:45-15:45), 24=夜間(17:00-翌6:00)。
    /// 注文発注 (/sendorder/future) の Exchange は時刻で 23/24 を動的に判定するため
    /// 本値は使わない (<c>KabuAdapter.GetActiveSessionExchange</c> 参照)。
    /// 日中固定 (23) で register すると夜間に Micro 等の push が止まる事象を確認したため
    /// 2026-05-19 に 23 → 2 へ既定値を変更 (<c>docs/adapters/kabu.md</c> 参照)。
    /// </summary>
    public int Exchange { get; set; } = 2;
}
