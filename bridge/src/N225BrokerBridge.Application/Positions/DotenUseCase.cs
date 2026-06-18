using N225BrokerBridge.Application.Common;
using N225BrokerBridge.Application.Orders;
using N225BrokerBridge.Application.Signals;
using N225BrokerBridge.Domain.Brokers;
using N225BrokerBridge.Domain.Orders;
using N225BrokerBridge.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace N225BrokerBridge.Application.Positions;

/// <summary>
/// ドテン (反対転換) ユースケース。
///
/// 旧建玉を全量返済 + 新方向の建玉を新規発注、を 1 シグナルで連続実行する。
/// 内部的には ClosePositionUseCase + PlaceNewOrderUseCase の組合せ。
///
/// 順序:
///   1. 旧建玉返済 (ExitOrderIntent 化 → ClosePositionUseCase)
///   2. 新規建て (NewOrderIntent 化 → PlaceNewOrderUseCase)
///
/// 注意:
///   - 1 の約定通知を待たずに 2 を発火するため、新建玉が先に約定する可能性あり。
///     この場合一時的に両建ての状態が発生し得るが、現 N225OrderBridge も同じ動作なので踏襲。
///   - 旧 N225OrderBridge の AutoDotenBuyOrder / AutoDotenSellOrder 相当。
/// </summary>
public sealed class DotenUseCase
{
    private readonly ClosePositionUseCase _close;
    private readonly PlaceNewOrderUseCase _place;
    private readonly ILogger<DotenUseCase> _logger;

    public DotenUseCase(
        ClosePositionUseCase close,
        PlaceNewOrderUseCase place,
        ILogger<DotenUseCase> logger)
    {
        _close = close;
        _place = place;
        _logger = logger;
    }

    public async Task<DotenResult> ExecuteAsync(DotenIntent intent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        _logger.LogInformation(
            "Doten: strategy={Strategy} exitQty={ExitQty} newQty={NewQty} originalSide={OriginalSide}",
            intent.Strategy, intent.ExitQuantity, intent.NewQuantity, intent.OriginalSide);

        // 1. 旧建玉返済
        var exitIntent = new ExitOrderIntent(
            intent.Strategy, intent.Interval, intent.TradeMode, intent.Symbol,
            intent.OriginalSide, intent.ExitQuantity, intent.OrderPrice);
        var exitResult = await _close.ExecuteAsync(exitIntent, ct);

        // 2. 新規建て (反対方向)
        var newIntent = new NewOrderIntent(
            intent.Strategy, intent.Interval, intent.TradeMode, intent.Symbol,
            Side: intent.OriginalSide.Opposite(),
            Quantity: intent.NewQuantity,
            OrderPrice: intent.OrderPrice);
        var newResult = await _place.ExecuteAsync(newIntent, ct);

        return new DotenResult(intent, exitResult, newResult);
    }
}

public sealed record DotenResult(
    DotenIntent Intent,
    ClosePositionResult ExitResult,
    PlaceNewOrderResult NewResult);
