using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Positions;

/// <summary>
/// 建玉集約の永続化抽象。Domain 層に置き、Infrastructure 層で実装する。
/// </summary>
public interface IPositionRepository
{
    /// <summary>新規建玉を保存。</summary>
    Task AddAsync(Position position, CancellationToken ct = default);

    /// <summary>既存建玉の状態更新を保存 (LeaveQty/HoldQty 変更後)。</summary>
    Task UpdateAsync(Position position, CancellationToken ct = default);

    /// <summary>建玉を削除 (LeaveQty == 0 で消滅した場合)。</summary>
    Task RemoveAsync(ExecutionId id, CancellationToken ct = default);

    /// <summary>ExecutionId で建玉を取得。なければ null。</summary>
    Task<Position?> FindByIdAsync(ExecutionId id, CancellationToken ct = default);

    /// <summary>
    /// 返済対象候補の建玉群を絞り込む。
    /// (BrokerCode, Strategy, Interval, TradeMode, Side) でフィルタ。
    /// 跨ぎ消化のため、複数件が返ることを想定。
    /// </summary>
    Task<IReadOnlyList<Position>> FindMatchingForCloseAsync(
        BrokerCode brokerCode,
        StrategyName strategy,
        int interval,
        TradeMode tradeMode,
        Side originalSide,
        CancellationToken ct = default);

    /// <summary>未終端 (LeaveQty > 0) な全建玉一覧。</summary>
    Task<IReadOnlyList<Position>> FindActiveAsync(CancellationToken ct = default);
}
