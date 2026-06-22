using System.Collections.Concurrent;
using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using N225BrokerBridge.Application.Common;
using N225BrokerBridge.Application.Sync;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Infrastructure.Brokers.Mock;

/// <summary>
/// シミュレータモード (--simulator) 用の Mock ブローカー実装。
/// 詳細仕様は docs/simulator-mode.md。
///
/// 外部通信を一切行わず in-memory で建玉・注文を管理する。
/// 発注は 50ms 後に即時約定 (ExecutionEvent 発火) するため
/// 「Webhook 受信 → 発注 → 約定 → 建玉計上 → 返済」の一連フローが完結する。
///
/// 価格ティック (PriceStream) は内部 Timer で 1 秒ごとに 55,600 ± 50 円のランダム値を発火。
///
/// IOrderSnapshotNotifier / IOrderInitialFetcher / IPriceUpdateNotifier も併せて実装し
/// UI への注文・価格 push 経路を本物の kabu アダプタと等価に提供する。
/// </summary>
public sealed class MockBrokerAdapter
    : IBrokerAdapter,
      IOrderSnapshotNotifier,
      IOrderInitialFetcher,
      IPriceUpdateNotifier,
      IDisposable
{
    private static readonly SymbolCode MockSymbol = new("MOCK-NK225-202606");
    private const decimal PriceCenter = 55_600m;
    private const int PriceJitter = 50;
    private const int ExecutionDelayMs = 50;

    private readonly ILogger<MockBrokerAdapter> _logger;
    private readonly Subject<ExecutionEvent> _executionStream = new();
    private readonly Subject<PriceTick> _priceStream = new();

    private readonly object _stateLock = new();
    private readonly Dictionary<OrderId, OrderSnapshot> _orders = new();
    private readonly Dictionary<ExecutionId, MutablePosition> _positions = new();
    private int _orderIdSequence;
    private int _executionIdSequence;

    private readonly Timer _priceTimer;
    private readonly Random _random = new();
    private decimal _lastPrice = PriceCenter;

    /// <inheritdoc/>
    public BrokerCode BrokerCode => BrokerCode.Mock;
    /// <inheritdoc/>
    public bool IsConnected => true;
    /// <inheritdoc/>
    public IObservable<ExecutionEvent> ExecutionStream => _executionStream;
    /// <inheritdoc/>
    public IObservable<PriceTick> PriceStream => _priceStream;

    /// <inheritdoc/>
    public IReadOnlyList<OrderSnapshot> LatestSnapshots
    {
        get
        {
            lock (_stateLock) return _orders.Values.ToList();
        }
    }

    /// <inheritdoc/>
    public event EventHandler<OrderSnapshotsEventArgs>? SnapshotsUpdated;
    /// <inheritdoc/>
    public event EventHandler<PriceTick>? PriceUpdated;

    public MockBrokerAdapter(ILogger<MockBrokerAdapter> logger)
    {
        _logger = logger;
        _priceTimer = new Timer(OnPriceTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        _logger.LogInformation(
            "MockBrokerAdapter 起動 (中心値={Center} 円, 揺らぎ=±{Jitter} 円, 約定遅延={Delay}ms)",
            PriceCenter, PriceJitter, ExecutionDelayMs);
    }

    /// <inheritdoc/>
    public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var orderId = NextOrderId();
        var fillPrice = ResolveFillPrice(request.OrderType, request.LimitPrice, request.Side);
        OrderSnapshot snapshot;
        lock (_stateLock)
        {
            snapshot = new OrderSnapshot(
                BrokerCode: BrokerCode.Mock,
                BrokerOrderId: orderId,
                State: OrderState.Submitted,
                Symbol: request.Symbol,
                Side: request.Side,
                TradeType: TradeType.NewOrder,
                RequestedQuantity: request.Quantity,
                ExecutedQuantity: Quantity.Zero,
                Price: fillPrice,
                CreatedAt: DateTime.UtcNow);
            _orders[orderId] = snapshot;
        }
        RaiseSnapshotsUpdated();

        _ = ScheduleNewOrderFillAsync(request, orderId, fillPrice, ct);

        _logger.LogInformation(
            "MockBrokerAdapter.PlaceOrder accepted corr={Corr} OrderId={OrderId} side={Side} qty={Qty}",
            request.CorrelationId, orderId, request.Side, request.Quantity);

        return Task.FromResult(new OrderResult(
            request.CorrelationId,
            OrderResultStatus.Accepted,
            orderId,
            ErrorCode: null,
            ErrorMessage: null,
            ReceivedAt: DateTime.UtcNow));
    }

    /// <inheritdoc/>
    public Task<OrderResult> ClosePositionAsync(ClosePositionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        MutablePosition? target;
        lock (_stateLock)
        {
            _positions.TryGetValue(request.TargetExecutionId, out target);
        }
        if (target is null || target.LeaveQuantity < request.Quantity)
        {
            _logger.LogWarning(
                "MockBrokerAdapter.ClosePosition rejected (建玉不存在 or 残量不足) corr={Corr} target={Target} req={Qty}",
                request.CorrelationId, request.TargetExecutionId, request.Quantity);
            return Task.FromResult(new OrderResult(
                request.CorrelationId,
                OrderResultStatus.Rejected,
                BrokerOrderId: null,
                ErrorCode: "MOCK_NO_POSITION",
                ErrorMessage: "対象建玉が存在しないか残量が不足しています (Mock)",
                ReceivedAt: DateTime.UtcNow));
        }

        var orderId = NextOrderId();
        var exitSide = request.OriginalSide.Opposite();
        var fillPrice = ResolveFillPrice(request.OrderType, request.LimitPrice, exitSide);

        OrderSnapshot snapshot;
        lock (_stateLock)
        {
            snapshot = new OrderSnapshot(
                BrokerCode: BrokerCode.Mock,
                BrokerOrderId: orderId,
                State: OrderState.Submitted,
                Symbol: request.Symbol,
                Side: exitSide,
                TradeType: TradeType.ExitOrder,
                RequestedQuantity: request.Quantity,
                ExecutedQuantity: Quantity.Zero,
                Price: fillPrice,
                CreatedAt: DateTime.UtcNow);
            _orders[orderId] = snapshot;
        }
        RaiseSnapshotsUpdated();

        _ = ScheduleExitFillAsync(request, orderId, fillPrice, ct);

        _logger.LogInformation(
            "MockBrokerAdapter.ClosePosition accepted corr={Corr} OrderId={OrderId} target={Target} qty={Qty}",
            request.CorrelationId, orderId, request.TargetExecutionId, request.Quantity);

        return Task.FromResult(new OrderResult(
            request.CorrelationId,
            OrderResultStatus.Accepted,
            orderId,
            ErrorCode: null,
            ErrorMessage: null,
            ReceivedAt: DateTime.UtcNow));
    }

    /// <inheritdoc/>
    public Task<OrderResult> CancelOrderAsync(OrderId brokerOrderId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(brokerOrderId);
        lock (_stateLock)
        {
            if (_orders.TryGetValue(brokerOrderId, out var snap))
            {
                _orders[brokerOrderId] = snap with { State = OrderState.Cancelled };
            }
        }
        RaiseSnapshotsUpdated();
        _logger.LogInformation("MockBrokerAdapter.CancelOrder OrderId={OrderId}", brokerOrderId);
        return Task.FromResult(new OrderResult(
            CorrelationId: Guid.Empty,
            OrderResultStatus.Accepted,
            brokerOrderId,
            ErrorCode: null,
            ErrorMessage: null,
            ReceivedAt: DateTime.UtcNow));
    }

    /// <inheritdoc/>
    public Task<BrokerPositionsResult> GetPositionsAsync(CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            IReadOnlyList<PositionSnapshot> list = _positions.Values
                .Where(p => p.LeaveQuantity.Value > 0)
                .Select(p => new PositionSnapshot(
                    BrokerCode: BrokerCode.Mock,
                    PositionId: p.Id,
                    Symbol: p.Symbol,
                    Side: p.Side,
                    LeaveQuantity: p.LeaveQuantity,
                    HoldQuantity: p.HoldQuantity,
                    EntryPrice: p.EntryPrice,
                    OpenedAt: p.OpenedAt))
                .ToList();
            // モックは常に確定的に読める (in-memory)。
            return Task.FromResult(BrokerPositionsResult.Available(list));
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<OrderSnapshot>> GetOrdersAsync(CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            IReadOnlyList<OrderSnapshot> list = _orders.Values.ToList();
            return Task.FromResult(list);
        }
    }

    /// <inheritdoc/>
    public Task<QuoteSnapshot> GetQuoteAsync(SymbolCode symbol, CancellationToken ct = default)
    {
        var (last, bid, ask) = SampleQuote();
        return Task.FromResult(new QuoteSnapshot(
            BrokerCode: BrokerCode.Mock,
            Symbol: symbol,
            LastPrice: new Price(last),
            BidPrice: new Price(bid),
            AskPrice: new Price(ask),
            BidQuantity: new Quantity(10),
            AskQuantity: new Quantity(10),
            At: DateTime.UtcNow));
    }

    /// <inheritdoc/>
    public Task SubscribePriceAsync(SymbolCode symbol, CancellationToken ct = default) => Task.CompletedTask;
    /// <inheritdoc/>
    public Task SubscribePricesAsync(IEnumerable<SymbolCode> symbols, CancellationToken ct = default) => Task.CompletedTask;
    /// <inheritdoc/>
    public Task UnsubscribePriceAsync(SymbolCode symbol, CancellationToken ct = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<ResolvedSymbol?> ResolveFutureSymbolAsync(
        string futureCode, int derivMonth = 0, CancellationToken ct = default)
    {
        return Task.FromResult<ResolvedSymbol?>(new ResolvedSymbol(
            Symbol: MockSymbol,
            DisplayName: "日経225Micro Mock (Simulator)",
            ContractMonthLabel: "2026年6月限"));
    }

    /// <inheritdoc/>
    public async Task<int> InitialFetchOrdersAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask;
        RaiseSnapshotsUpdated();
        lock (_stateLock) return _orders.Count;
    }

    public void Dispose()
    {
        _priceTimer.Dispose();
        _executionStream.OnCompleted();
        _executionStream.Dispose();
        _priceStream.OnCompleted();
        _priceStream.Dispose();
    }

    private async Task ScheduleNewOrderFillAsync(
        OrderRequest request, OrderId orderId, Price fillPrice, CancellationToken ct)
    {
        try
        {
            await Task.Delay(ExecutionDelayMs, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var execId = NextExecutionId();
        var position = new MutablePosition(
            Id: execId,
            Symbol: request.Symbol,
            Side: request.Side,
            EntryPrice: fillPrice,
            LeaveQuantity: request.Quantity,
            HoldQuantity: Quantity.Zero,
            OpenedAt: DateTime.UtcNow);

        // ★ 重要: 処理順を厳守する (race condition 防止)
        // 1) ExecutionEvent を先に発火 → ExecutionApplier が Order 集約に約定を反映
        // 2) snapshot を Filled に更新 → SnapshotsUpdated が OrderTerminationSubscriber に到達した時点で
        //    Order 集約は既に Filled で unfilled=0 → ApplyTerminationAsync は no-op (冪等)
        //
        // 逆順だと OrderTerminationSubscriber が「Filled なのに unfilled>0 = 部分約定後のキャンセル」
        // と誤判定して Order を Cancelled マークし、後続の ExecutionEvent が
        // "Cannot apply execution in state Cancelled" で失敗する (2026-05-27 修正)。
        lock (_stateLock)
        {
            _positions[execId] = position;
        }

        _executionStream.OnNext(new ExecutionEvent(
            BrokerCode: BrokerCode.Mock,
            BrokerOrderId: orderId,
            ExecutionId: execId,
            TradeType: TradeType.NewOrder,
            Side: request.Side,
            Symbol: request.Symbol,
            Quantity: request.Quantity,
            Price: fillPrice,
            ExecutedAt: DateTime.UtcNow,
            TargetPositionId: null));

        // ExecutionApplier は async void で実行されるため、少し待ってから snapshot を更新する。
        // 100ms で十分 (in-memory リポジトリの更新 + イベント発火程度)。
        await Task.Delay(100, ct);

        lock (_stateLock)
        {
            if (_orders.TryGetValue(orderId, out var snap))
            {
                _orders[orderId] = snap with
                {
                    State = OrderState.Filled,
                    ExecutedQuantity = request.Quantity,
                    Price = fillPrice
                };
            }
        }
        RaiseSnapshotsUpdated();

        _logger.LogInformation(
            "MockBrokerAdapter NewFill OrderId={OrderId} ExecId={ExecId} price={Price}",
            orderId, execId, fillPrice);
    }

    private async Task ScheduleExitFillAsync(
        ClosePositionRequest request, OrderId orderId, Price fillPrice, CancellationToken ct)
    {
        try
        {
            await Task.Delay(ExecutionDelayMs, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var execId = NextExecutionId();
        var exitSide = request.OriginalSide.Opposite();

        // ★ ScheduleNewOrderFillAsync と同じ理由で処理順を厳守 (詳細はそちらのコメント参照)。
        // 1) Position の残量を減らす (in-memory)
        // 2) ExecutionEvent 発火 → ExecutionApplier が Order 集約に約定を反映
        // 3) 100ms 待機
        // 4) snapshot を Filled にして RaiseSnapshotsUpdated
        lock (_stateLock)
        {
            if (_positions.TryGetValue(request.TargetExecutionId, out var pos))
            {
                var remaining = pos.LeaveQuantity - request.Quantity;
                if (remaining.IsZero)
                {
                    _positions.Remove(request.TargetExecutionId);
                }
                else
                {
                    _positions[request.TargetExecutionId] = pos with { LeaveQuantity = remaining };
                }
            }
        }

        _executionStream.OnNext(new ExecutionEvent(
            BrokerCode: BrokerCode.Mock,
            BrokerOrderId: orderId,
            ExecutionId: execId,
            TradeType: TradeType.ExitOrder,
            Side: exitSide,
            Symbol: request.Symbol,
            Quantity: request.Quantity,
            Price: fillPrice,
            ExecutedAt: DateTime.UtcNow,
            TargetPositionId: request.TargetExecutionId));

        await Task.Delay(100, ct);

        lock (_stateLock)
        {
            if (_orders.TryGetValue(orderId, out var snap))
            {
                _orders[orderId] = snap with
                {
                    State = OrderState.Filled,
                    ExecutedQuantity = request.Quantity,
                    Price = fillPrice
                };
            }
        }
        RaiseSnapshotsUpdated();

        _logger.LogInformation(
            "MockBrokerAdapter ExitFill OrderId={OrderId} ExecId={ExecId} target={Target} price={Price}",
            orderId, execId, request.TargetExecutionId, fillPrice);
    }

    private void OnPriceTick(object? state)
    {
        try
        {
            var (last, bid, ask) = SampleQuote();
            _lastPrice = last;
            var tick = new PriceTick(
                BrokerCode: BrokerCode.Mock,
                Symbol: MockSymbol,
                LastPrice: new Price(last),
                BidPrice: new Price(bid),
                AskPrice: new Price(ask),
                At: DateTime.UtcNow);
            _priceStream.OnNext(tick);
            PriceUpdated?.Invoke(this, tick);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MockBrokerAdapter price tick failure");
        }
    }

    private (decimal last, decimal bid, decimal ask) SampleQuote()
    {
        var offset = _random.Next(-PriceJitter, PriceJitter + 1);
        var last = PriceCenter + offset;
        // kabu の命名規約踏襲 (BidPrice = 売り板最良 = 通常用語 ASK)
        var bidPrice = last + 5m;
        var askPrice = last - 5m;
        return (last, bidPrice, askPrice);
    }

    private Price ResolveFillPrice(OrderType type, Price limit, Side side)
    {
        // すべての発注は即時約定モデル: 成行は現在値、指値は指値価格、その他も即時約定。
        if (type == OrderType.Limit && !limit.IsZero) return limit;
        if (type == OrderType.Stop && !limit.IsZero) return limit;
        return new Price(_lastPrice);
    }

    private OrderId NextOrderId()
    {
        var n = Interlocked.Increment(ref _orderIdSequence);
        return new OrderId($"MOCK-{n:D8}");
    }

    private ExecutionId NextExecutionId()
    {
        var n = Interlocked.Increment(ref _executionIdSequence);
        return new ExecutionId($"MOCK-EXEC-{n:D8}");
    }

    private void RaiseSnapshotsUpdated()
    {
        IReadOnlyList<OrderSnapshot> snapshot;
        lock (_stateLock) snapshot = _orders.Values.ToList();
        SnapshotsUpdated?.Invoke(this, new OrderSnapshotsEventArgs(snapshot, DateTime.UtcNow));
    }

    /// <summary>
    /// Mock 内部で建玉を追跡するための可変表現 (record の with 構文を活用)。
    /// </summary>
    private sealed record MutablePosition(
        ExecutionId Id,
        SymbolCode Symbol,
        Side Side,
        Price EntryPrice,
        Quantity LeaveQuantity,
        Quantity HoldQuantity,
        DateTime OpenedAt);
}
