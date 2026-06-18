using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Positions;

/// <summary>
/// 部分返済の建玉選択ドメインサービス (純粋関数)。
///
/// (BrokerCode, Strategy, Interval, TradeMode, Side) で絞った候補建玉に対し、
/// 要求枚数 (webhook の order_contracts 相当) を ExecutionId 順 (= FIFO 到着順) に
/// min(残要求, AvailableForClose) で消化していく「消化計画」を返す。
///
/// 設計判断 (2026-05-17 ユーザー合意):
///   - ① 選択順序: ExecutionId 順 (どちらが先かは問わないが決定性確保のため明示)
///   - ② 跨ぎ消化: 許可。建玉 A 全消化 + 建玉 B 部分消化のような組み合わせを返す
///   - ③ 要求 > 残合計: 残合計まで消化する計画を返し、不足分を Shortfall として通知
///                       (呼び出し側で警告ログ + WriteMessage する想定)
///
/// 本サービスは集約を**変更しない**。返した計画を元に各 Position.ReserveForClose() を
/// 呼ぶのはアプリケーション層の責務。
/// </summary>
public static class PositionMatcher
{
    /// <summary>
    /// 部分返済の消化計画を作成する。
    /// </summary>
    /// <param name="candidates">候補建玉群 (順序問わず、本メソッド内で ExecutionId 順にソート)</param>
    /// <param name="requested">webhook 要求枚数</param>
    /// <returns>消化計画。Shortfall.IsPositive なら残合計不足。</returns>
    public static ClosurePlan BuildPlan(
        IEnumerable<Position> candidates,
        Quantity requested)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (!requested.IsPositive)
            throw new ArgumentException("Requested quantity must be positive.", nameof(requested));

        var ordered = candidates
            .Where(p => !p.IsClosed && p.AvailableForClose.IsPositive)
            .OrderBy(p => p.Id.Value, StringComparer.Ordinal)
            .ToList();

        var allocations = new List<ClosureAllocation>();
        var remaining = requested;

        foreach (var position in ordered)
        {
            if (remaining.IsZero) break;

            var qtyToClose = Quantity.Min(remaining, position.AvailableForClose);
            allocations.Add(new ClosureAllocation(position, qtyToClose));
            remaining -= qtyToClose;
        }

        return new ClosurePlan(
            Requested: requested,
            Allocations: allocations,
            Shortfall: remaining);
    }
}

/// <summary>
/// 1 建玉に対する消化計画 (建玉 + 消化枚数)。
/// </summary>
public sealed record ClosureAllocation(Position Position, Quantity Quantity);

/// <summary>
/// 全体の消化計画。
/// </summary>
public sealed record ClosurePlan(
    Quantity Requested,
    IReadOnlyList<ClosureAllocation> Allocations,
    Quantity Shortfall)
{
    /// <summary>計画通り全量消化できる (Shortfall == 0)。</summary>
    public bool IsComplete => Shortfall.IsZero;

    /// <summary>計画で実際に消化される合計枚数。</summary>
    public Quantity TotalToClose => Allocations.Aggregate(Quantity.Zero, (sum, a) => sum + a.Quantity);
}
