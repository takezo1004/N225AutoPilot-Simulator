using N225BrokerBridge.Infrastructure.Brokers.Kabu.Dto;

namespace N225BrokerBridge.Infrastructure.Brokers.Kabu;

/// <summary>
/// kabu <c>/positions</c> 照会の生応答結果。「読めて N 件 (0 件含む)」と
/// 「読めなかった (未認証・セッション外・メンテ・通信エラー・解析不能)」を区別する。
///
/// kabu は未認証や夜間セッション切替で建玉配列でなくエラーエンベロープや空応答を返すため、
/// それを「建玉ゼロ」と取り違えないよう、ここで availability を明示して上位 (KabuAdapter →
/// リコンサイル) に伝える。詳細は <see cref="N225BrokerBridge.Domain.Brokers.BrokerPositionsResult"/>。
/// </summary>
public sealed record KabuPositionsResponse(bool IsAvailable, IReadOnlyList<KabuPositionDto> Positions)
{
    public static readonly KabuPositionsResponse Unavailable =
        new(false, Array.Empty<KabuPositionDto>());

    public static KabuPositionsResponse Available(IReadOnlyList<KabuPositionDto> positions) =>
        new(true, positions ?? Array.Empty<KabuPositionDto>());
}
