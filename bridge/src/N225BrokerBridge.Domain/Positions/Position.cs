using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.Positions.Events;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Positions;

/// <summary>
/// 建玉集約ルート。
///
/// 1 建玉 = 1 約定 (ExecutionId 単位) で保持する。
/// (現 N225OrderBridge の PositionListEntity と同じモデル。3 枚建てが 1+1+1 で
///  分割約定すれば 3 建玉として保持される)
///
/// 不変条件:
///   - LeaveQuantity ≥ 0
///   - HoldQuantity ≥ 0
///   - HoldQuantity ≤ LeaveQuantity (返済注文中の枚数 ≤ 保有枚数)
///   - 建玉終了 (LeaveQuantity == 0) 後は状態変更不可
///
/// メソッド (返済フロー):
///   1. 返済注文発注時 → ReserveForClose(qty) で HoldQty += qty
///   2. 返済約定通知時 → ApplyClosure(qty) で LeaveQty -= qty, HoldQty -= qty
///   3. 返済注文取消時 → ReleaseReservation(qty) で HoldQty -= qty
/// </summary>
public sealed class Position : AggregateRoot<ExecutionId>
{
    /// <summary>建玉を保有するブローカー。</summary>
    public BrokerCode BrokerCode { get; }
    /// <summary>建玉を生成した戦略名 (Manual の場合 "Manual")。</summary>
    public StrategyName Strategy { get; }
    /// <summary>足 (分) — Auto モード時のみ意味を持つ。Manual モード時は 0。</summary>
    public int Interval { get; }
    /// <summary>建玉を生成した取引モード (自動 / 手動)。</summary>
    public TradeMode TradeMode { get; }
    /// <summary>銘柄コード。</summary>
    public SymbolCode Symbol { get; }
    /// <summary>建玉のサイド (買建玉 / 売建玉)。返済注文の Side は <see cref="SideExtensions.Opposite"/> で決定する。</summary>
    public Side Side { get; }
    /// <summary>建値 (取得価格)。</summary>
    public Price EntryPrice { get; }
    /// <summary>建玉成立時刻 (UTC)。</summary>
    public DateTime OpenedAt { get; }

    /// <summary>残保有枚数 (まだ返済されていない枚数)。</summary>
    public Quantity LeaveQuantity { get; private set; }
    /// <summary>返済注文中の枚数 (新たな返済発注を出せる残量 = LeaveQuantity - HoldQuantity)。</summary>
    public Quantity HoldQuantity { get; private set; }

    /// <summary>
    /// 建玉集約を生成する。1 建玉 = 1 約定 (ExecutionId 単位)。
    /// </summary>
    /// <param name="id">約定 ID (建玉識別子)。</param>
    /// <param name="brokerCode">建玉を保有するブローカー。</param>
    /// <param name="strategy">戦略名 (Manual の場合 "Manual")。</param>
    /// <param name="interval">足 (分)。Auto モードは 1 以上、Manual モードは 0。</param>
    /// <param name="tradeMode">自動 / 手動。</param>
    /// <param name="symbol">銘柄。</param>
    /// <param name="side">建玉サイド (買 / 売)。</param>
    /// <param name="initialQuantity">建玉成立時の初期枚数 (1 以上)。</param>
    /// <param name="entryPrice">建値。</param>
    /// <param name="openedAtUtc">建玉成立時刻 (UTC)。</param>
    /// <exception cref="InvalidValueObjectException">
    /// 不変条件違反: Auto かつ interval &lt;= 0 / interval &lt; 0 / initialQuantity &lt;= 0。
    /// </exception>
    public Position(
        ExecutionId id,
        BrokerCode brokerCode,
        StrategyName strategy,
        int interval,
        TradeMode tradeMode,
        SymbolCode symbol,
        Side side,
        Quantity initialQuantity,
        Price entryPrice,
        DateTime openedAtUtc)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(brokerCode);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(symbol);

        // Interval は Auto 建玉時のみ必須 (戦略の足を識別するため)。Manual 建玉は 0 を許容。
        if (tradeMode == TradeMode.Auto && interval <= 0)
            throw new InvalidValueObjectException($"Auto-mode position requires positive Interval. Got: {interval}");
        if (interval < 0)
            throw new InvalidValueObjectException($"Interval must be non-negative. Got: {interval}");
        if (!initialQuantity.IsPositive)
            throw new InvalidValueObjectException("Position initial quantity must be positive.");

        BrokerCode = brokerCode;
        Strategy = strategy;
        Interval = interval;
        TradeMode = tradeMode;
        Symbol = symbol;
        Side = side;
        EntryPrice = entryPrice;
        OpenedAt = openedAtUtc;

        LeaveQuantity = initialQuantity;
        HoldQuantity = Quantity.Zero;

        RaiseEvent(new PositionOpenedEvent(
            id, brokerCode, strategy, side, initialQuantity, entryPrice, openedAtUtc));
    }

    /// <summary>建玉が完全に閉じたか。</summary>
    public bool IsClosed => LeaveQuantity.IsZero;

    /// <summary>現在返済注文中の枚数を考慮した、新たに返済発注に出せる残り枚数。</summary>
    public Quantity AvailableForClose => LeaveQuantity - HoldQuantity;

    /// <summary>
    /// 返済注文発注時、指定枚数を拘束する (HoldQty に積む)。
    /// 跨ぎ消化対応: 呼び出し側は <see cref="AvailableForClose"/> を超えない範囲で呼ぶこと。
    /// </summary>
    /// <param name="qty">拘束する枚数 (1 以上、<see cref="AvailableForClose"/> 以下)。</param>
    /// <exception cref="InvalidValueObjectException">qty が 0 以下。</exception>
    /// <exception cref="InvalidOperationException">建玉終了済み / qty が AvailableForClose を超過。</exception>
    public void ReserveForClose(Quantity qty)
    {
        EnsureNotClosed();
        if (!qty.IsPositive)
            throw new InvalidValueObjectException("Reserve quantity must be positive.");
        if (qty > AvailableForClose)
            throw new InvalidOperationException(
                $"Reserve qty {qty} exceeds available {AvailableForClose} (Leave={LeaveQuantity}, Hold={HoldQuantity}).");

        HoldQuantity += qty;
        RaiseEvent(new PositionUpdatedEvent(Id, LeaveQuantity, HoldQuantity, DateTime.UtcNow));
    }

    /// <summary>
    /// 返済約定通知時、LeaveQty / HoldQty を同量だけ減算。
    /// LeaveQty == 0 になったら PositionClosedEvent を発火。
    /// </summary>
    /// <param name="qty">返済約定枚数。事前に <see cref="ReserveForClose"/> 済みであること。</param>
    /// <param name="occurredAtUtc">返済約定時刻 (UTC)。</param>
    /// <exception cref="InvalidValueObjectException">qty が 0 以下。</exception>
    /// <exception cref="InvalidOperationException">
    /// 建玉終了済み / qty が LeaveQuantity 超 / qty が HoldQuantity 超 (Reserve なしでの Closure)。
    /// </exception>
    public void ApplyClosure(Quantity qty, DateTime occurredAtUtc)
    {
        EnsureNotClosed();
        if (!qty.IsPositive)
            throw new InvalidValueObjectException("Closure quantity must be positive.");
        if (qty > LeaveQuantity)
            throw new InvalidOperationException(
                $"Closure qty {qty} exceeds leave {LeaveQuantity}.");
        if (qty > HoldQuantity)
            throw new InvalidOperationException(
                $"Closure qty {qty} exceeds hold {HoldQuantity}. " +
                "Closure should follow a prior ReserveForClose for the same qty.");

        LeaveQuantity -= qty;
        HoldQuantity -= qty;

        RaiseEvent(new PositionUpdatedEvent(Id, LeaveQuantity, HoldQuantity, occurredAtUtc));
        if (LeaveQuantity.IsZero)
        {
            RaiseEvent(new PositionClosedEvent(Id, occurredAtUtc));
        }
    }

    /// <summary>
    /// 返済注文が取消・失効した時、拘束 (HoldQty) を解放する。
    /// </summary>
    /// <param name="qty">解放する枚数。事前の <see cref="ReserveForClose"/> 量と一致させること。</param>
    /// <exception cref="InvalidValueObjectException">qty が 0 以下。</exception>
    /// <exception cref="InvalidOperationException">建玉終了済み / qty が HoldQuantity 超。</exception>
    public void ReleaseReservation(Quantity qty)
    {
        EnsureNotClosed();
        if (!qty.IsPositive)
            throw new InvalidValueObjectException("Release quantity must be positive.");
        if (qty > HoldQuantity)
            throw new InvalidOperationException(
                $"Release qty {qty} exceeds hold {HoldQuantity}.");

        HoldQuantity -= qty;
        RaiseEvent(new PositionUpdatedEvent(Id, LeaveQuantity, HoldQuantity, DateTime.UtcNow));
    }

    private void EnsureNotClosed()
    {
        if (IsClosed)
            throw new InvalidOperationException($"Position {Id} is already closed.");
    }
}
