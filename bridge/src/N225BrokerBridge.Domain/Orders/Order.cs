using N225BrokerBridge.Domain.Common;
using N225BrokerBridge.Domain.Orders.Events;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Domain.Orders;

/// <summary>
/// 注文集約ルート。
///
/// 1 注文 = 1 ブローカーへの 1 アクション。
/// 注文は内部 Id (Guid) で識別され、ブローカーから採番された OrderId は受付後に紐付ける。
///
/// 不変条件:
///   - RequestedQuantity = Σ Executions.Quantity + RemainingQuantity
///   - 終端状態 (Filled/Cancelled/Expired/Rejected) からは状態遷移不可
///   - Execution 追加は Submitted / PartiallyFilled 状態でのみ可能
///   - Submitted への遷移は Created 状態でのみ可能
///
/// 状態遷移:
///   Created → Submitted → PartiallyFilled → Filled
///                       ↘  Cancelled / Expired / Rejected
/// </summary>
public sealed class Order : AggregateRoot<Guid>
{
    /// <summary>発注先ブローカー識別コード。</summary>
    public BrokerCode BrokerCode { get; }
    /// <summary>戦略名 (Auto は alert_name 等、Manual は "Manual" 固定)。</summary>
    public StrategyName Strategy { get; }
    /// <summary>足 (分) — Auto モード時のみ意味を持つ。Manual モード時は 0。</summary>
    public int Interval { get; }
    /// <summary>自動 / 手動の取引モード。</summary>
    public TradeMode TradeMode { get; }
    /// <summary>銘柄コード。</summary>
    public SymbolCode Symbol { get; }
    /// <summary>売買サイド (Buy / Sell)。</summary>
    public Side Side { get; }
    /// <summary>新規 / 返済の区分。</summary>
    public TradeType TradeType { get; }
    /// <summary>注文タイプ (成行 / 指値 / 対当 / 逆指値)。</summary>
    public OrderType OrderType { get; }
    /// <summary>有効期間条件 (FAS / FAK / FOK)。</summary>
    public TimeInForce TimeInForce { get; }
    /// <summary>発注枚数 (1 以上)。</summary>
    public Quantity RequestedQuantity { get; }
    /// <summary>指値価格 (成行では <see cref="Price.Zero"/>)。</summary>
    public Price LimitPrice { get; }
    /// <summary>逆指値トリガー価格 (逆指値以外では <see cref="Price.Zero"/>)。</summary>
    public Price StopPrice { get; }

    /// <summary>返済注文の場合、対象建玉の ExecutionId (新規注文では null)。</summary>
    public ExecutionId? TargetExecutionId { get; }

    /// <summary>注文の現在状態。<see cref="OrderState"/> 参照。</summary>
    public OrderState State { get; private set; }

    /// <summary>ブローカー採番の OrderId (Submitted 後に設定)。</summary>
    public OrderId? BrokerOrderId { get; private set; }

    /// <summary>注文作成時刻 (UTC)。</summary>
    public DateTime CreatedAt { get; }
    /// <summary>ブローカー送信完了時刻 (UTC、Submitted 後に設定)。</summary>
    public DateTime? SubmittedAt { get; private set; }
    /// <summary>終端遷移時刻 (UTC、Cancelled/Expired/Rejected で設定)。</summary>
    public DateTime? TerminatedAt { get; private set; }

    private readonly List<Execution> _executions = new();
    /// <summary>取り込まれた約定の読み取り専用一覧。</summary>
    public IReadOnlyList<Execution> Executions => _executions.AsReadOnly();

    /// <summary>
    /// 注文集約を生成する。状態は <see cref="OrderState.Created"/> から開始。
    /// </summary>
    /// <param name="id">アプリ内一意の Guid (ブローカー OrderId とは別)。</param>
    /// <param name="brokerCode">発注先ブローカー。</param>
    /// <param name="strategy">戦略名 (Manual の場合 "Manual")。</param>
    /// <param name="interval">足 (分)。Auto モードは 1 以上、Manual モードは 0。</param>
    /// <param name="tradeMode">自動 / 手動。</param>
    /// <param name="symbol">銘柄。</param>
    /// <param name="side">売買サイド。</param>
    /// <param name="tradeType">新規 / 返済。</param>
    /// <param name="orderType">注文タイプ。</param>
    /// <param name="timeInForce">有効期間条件。</param>
    /// <param name="requestedQuantity">発注枚数 (1 以上)。</param>
    /// <param name="limitPrice">指値価格。</param>
    /// <param name="stopPrice">逆指値トリガー価格。</param>
    /// <param name="targetExecutionId">返済対象建玉の ExecutionId (新規注文では null)。</param>
    /// <param name="createdAtUtc">作成時刻 (UTC)。</param>
    /// <exception cref="InvalidValueObjectException">
    /// 不変条件違反: Auto かつ interval &lt;= 0 / interval &lt; 0 / quantity &lt;= 0 / 返済で targetExecutionId が null。
    /// </exception>
    public Order(
        Guid id,
        BrokerCode brokerCode,
        StrategyName strategy,
        int interval,
        TradeMode tradeMode,
        SymbolCode symbol,
        Side side,
        TradeType tradeType,
        OrderType orderType,
        TimeInForce timeInForce,
        Quantity requestedQuantity,
        Price limitPrice,
        Price stopPrice,
        ExecutionId? targetExecutionId,
        DateTime createdAtUtc)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(brokerCode);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(symbol);

        // Interval は Auto モード時のみ必須 (戦略の足を識別するため)。Manual は 0 を許容する。
        if (tradeMode == TradeMode.Auto && interval <= 0)
            throw new InvalidValueObjectException($"Auto-mode order requires positive Interval. Got: {interval}");
        if (interval < 0)
            throw new InvalidValueObjectException($"Interval must be non-negative. Got: {interval}");
        if (!requestedQuantity.IsPositive)
            throw new InvalidValueObjectException("RequestedQuantity must be positive.");
        if (tradeType == TradeType.ExitOrder && targetExecutionId is null)
            throw new InvalidValueObjectException("ExitOrder must specify TargetExecutionId.");

        BrokerCode = brokerCode;
        Strategy = strategy;
        Interval = interval;
        TradeMode = tradeMode;
        Symbol = symbol;
        Side = side;
        TradeType = tradeType;
        OrderType = orderType;
        TimeInForce = timeInForce;
        RequestedQuantity = requestedQuantity;
        LimitPrice = limitPrice;
        StopPrice = stopPrice;
        TargetExecutionId = targetExecutionId;
        CreatedAt = createdAtUtc;
        State = OrderState.Created;
    }

    /// <summary>累計約定数量。</summary>
    public Quantity CumulativeExecutedQuantity =>
        _executions.Aggregate(Quantity.Zero, (sum, e) => sum + e.Quantity);

    /// <summary>残未約定数量 = RequestedQuantity - 累計約定。</summary>
    public Quantity RemainingQuantity => RequestedQuantity - CumulativeExecutedQuantity;

    /// <summary>終端状態か。</summary>
    public bool IsTerminal => State.IsTerminal();

    /// <summary>
    /// ブローカーへ送信完了し、OrderId が採番された。
    /// 状態は Created → Submitted へ遷移する。
    /// </summary>
    /// <param name="brokerOrderId">ブローカーが採番した注文 ID。</param>
    /// <param name="submittedAtUtc">送信完了時刻 (UTC)。</param>
    /// <exception cref="InvalidOperationException">現状態が Created 以外。</exception>
    public void MarkSubmitted(OrderId brokerOrderId, DateTime submittedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(brokerOrderId);
        if (State != OrderState.Created)
            throw new InvalidOperationException($"Cannot submit order in state {State}.");

        BrokerOrderId = brokerOrderId;
        SubmittedAt = submittedAtUtc;
        State = OrderState.Submitted;

        RaiseEvent(new OrderSubmittedEvent(Id, BrokerCode, brokerOrderId, submittedAtUtc));
    }

    /// <summary>
    /// 約定通知を取り込む。
    /// 部分約定の場合は PartiallyFilled、全量約定で Filled に遷移する。
    /// </summary>
    /// <param name="execution">取り込む約定。同一 ExecutionId を再度渡すと重複検知で例外。</param>
    /// <exception cref="InvalidOperationException">
    /// 現状態が Submitted / PartiallyFilled 以外 / 数量が残数量を超過 / ExecutionId 重複。
    /// </exception>
    public void ApplyExecution(Execution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        if (State != OrderState.Submitted && State != OrderState.PartiallyFilled)
            throw new InvalidOperationException($"Cannot apply execution in state {State}.");
        if (execution.Quantity > RemainingQuantity)
            throw new InvalidOperationException(
                $"Execution quantity {execution.Quantity} exceeds remaining {RemainingQuantity}.");
        if (_executions.Any(e => e.Id == execution.Id))
            throw new InvalidOperationException(
                $"Duplicate ExecutionId detected: {execution.Id}");

        _executions.Add(execution);

        var newState = RemainingQuantity.IsZero ? OrderState.Filled : OrderState.PartiallyFilled;
        State = newState;

        RaiseEvent(new OrderExecutedEvent(
            Id,
            execution.Id,
            execution.Quantity,
            execution.Price,
            IsFullyFilled: newState == OrderState.Filled,
            OccurredAt: execution.ExecutedAt));
    }

    /// <summary>
    /// 異常終了 (Cancelled / Expired / Rejected) として確定。
    /// </summary>
    /// <param name="terminalState">遷移先の終端状態。Cancelled / Expired / Rejected のいずれか。</param>
    /// <param name="reason">終端理由 (kabu の Message 等)。任意。</param>
    /// <param name="terminatedAtUtc">終端時刻 (UTC)。</param>
    /// <exception cref="ArgumentException">terminalState が異常終端 3 値以外。</exception>
    /// <exception cref="InvalidOperationException">既に終端状態。</exception>
    public void MarkTerminated(OrderState terminalState, string? reason, DateTime terminatedAtUtc)
    {
        if (terminalState != OrderState.Cancelled
            && terminalState != OrderState.Expired
            && terminalState != OrderState.Rejected)
        {
            throw new ArgumentException(
                $"MarkTerminated requires a terminal abnormal state. Got: {terminalState}",
                nameof(terminalState));
        }
        if (IsTerminal)
            throw new InvalidOperationException($"Order is already terminal in state {State}.");

        State = terminalState;
        TerminatedAt = terminatedAtUtc;

        RaiseEvent(new OrderTerminatedEvent(Id, terminalState, reason, terminatedAtUtc));
    }
}
