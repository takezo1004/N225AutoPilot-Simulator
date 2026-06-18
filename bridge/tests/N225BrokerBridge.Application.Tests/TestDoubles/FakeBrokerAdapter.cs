using System.Reactive.Subjects;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Application.Tests.TestDoubles;

/// <summary>
/// テスト用 IBrokerAdapter スタブ。応答を事前に仕込み、呼び出し履歴を記録する。
/// </summary>
public sealed class FakeBrokerAdapter : IBrokerAdapter
{
    private readonly Subject<ExecutionEvent> _exec = new();
    private readonly Subject<PriceTick> _price = new();
    private int _orderIdCounter = 1000;

    public BrokerCode BrokerCode { get; init; } = BrokerCode.Kabu;
    public bool IsConnected => true;

    /// <summary>記録: PlaceOrderAsync の引数</summary>
    public List<OrderRequest> PlaceOrderCalls { get; } = new();

    /// <summary>記録: ClosePositionAsync の引数</summary>
    public List<ClosePositionRequest> ClosePositionCalls { get; } = new();

    /// <summary>記録: CancelOrderAsync の引数</summary>
    public List<OrderId> CancelOrderCalls { get; } = new();

    /// <summary>応答スタブ: PlaceOrder 用</summary>
    public Func<OrderRequest, OrderResult>? PlaceOrderResponder { get; set; }

    /// <summary>応答スタブ: ClosePosition 用</summary>
    public Func<ClosePositionRequest, OrderResult>? ClosePositionResponder { get; set; }

    public Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct = default)
    {
        PlaceOrderCalls.Add(request);
        var result = PlaceOrderResponder?.Invoke(request)
            ?? new OrderResult(
                request.CorrelationId,
                OrderResultStatus.Accepted,
                new OrderId($"FAKE-{_orderIdCounter++}"),
                null, null, DateTime.UtcNow);
        return Task.FromResult(result);
    }

    public Task<OrderResult> ClosePositionAsync(ClosePositionRequest request, CancellationToken ct = default)
    {
        ClosePositionCalls.Add(request);
        var result = ClosePositionResponder?.Invoke(request)
            ?? new OrderResult(
                request.CorrelationId,
                OrderResultStatus.Accepted,
                new OrderId($"FAKE-EXIT-{_orderIdCounter++}"),
                null, null, DateTime.UtcNow);
        return Task.FromResult(result);
    }

    public Task<OrderResult> CancelOrderAsync(OrderId brokerOrderId, CancellationToken ct = default)
    {
        CancelOrderCalls.Add(brokerOrderId);
        return Task.FromResult(new OrderResult(
            Guid.Empty, OrderResultStatus.Accepted, brokerOrderId, null, null, DateTime.UtcNow));
    }

    public Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PositionSnapshot>>(Array.Empty<PositionSnapshot>());

    public Task<IReadOnlyList<OrderSnapshot>> GetOrdersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OrderSnapshot>>(Array.Empty<OrderSnapshot>());

    public Task<QuoteSnapshot> GetQuoteAsync(SymbolCode symbol, CancellationToken ct = default)
        => Task.FromResult(new QuoteSnapshot(
            BrokerCode, symbol, Price.Zero, Price.Zero, Price.Zero,
            Quantity.Zero, Quantity.Zero, DateTime.UtcNow));

    public IObservable<ExecutionEvent> ExecutionStream => _exec;
    public IObservable<PriceTick> PriceStream => _price;

    public Task SubscribePriceAsync(SymbolCode symbol, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SubscribePricesAsync(IEnumerable<SymbolCode> symbols, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task UnsubscribePriceAsync(SymbolCode symbol, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<ResolvedSymbol?> ResolveFutureSymbolAsync(
        string futureCode, int derivMonth = 0, CancellationToken ct = default)
        => Task.FromResult<ResolvedSymbol?>(
            new ResolvedSymbol(new SymbolCode(futureCode), $"{futureCode} (test)", "テスト限月"));

    /// <summary>テスト用: 約定イベントを手動で発火する</summary>
    public void EmitExecution(ExecutionEvent ev) => _exec.OnNext(ev);
}
