using Microsoft.Extensions.DependencyInjection;
using N225BrokerBridge.Application.Common;
using N225BrokerBridge.Application.Orders;
using N225BrokerBridge.Application.Positions;
using N225BrokerBridge.Application.Signals;

namespace N225BrokerBridge.Application.DI;

/// <summary>
/// Application 層のサービス登録拡張メソッド。
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddBrokerBridgeApplication(
        this IServiceCollection services,
        string? passphrase = null)
    {
        // Common
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        // 損益倍率レジストリ (UI が銘柄解決時に Set、ExecutionApplier が決済記録時に Resolve)。
        // 具象を Singleton 登録 → IContractMultiplierResolver を forward (UI は具象で Set を呼ぶ)。
        services.AddSingleton<ContractMultiplierRegistry>();
        services.AddSingleton<IContractMultiplierResolver>(
            sp => sp.GetRequiredService<ContractMultiplierRegistry>());

        // Signals
        services.AddSingleton<ISignalAuthenticator>(_ => new ConfiguredSignalAuthenticator(passphrase));
        services.AddSingleton<IAutoTradeGate, AutoTradeGate>();
        services.AddSingleton<IAutoTradeInstrumentProvider, AutoTradeInstrumentProvider>();

        // Use cases (Transient: 1 シグナル = 1 ユースケース呼び出し)
        services.AddTransient<PlaceNewOrderUseCase>();
        services.AddTransient<ClosePositionUseCase>();
        services.AddTransient<ManualClosePositionUseCase>();
        services.AddTransient<DotenUseCase>();
        services.AddTransient<ExecutionApplier>();
        services.AddTransient<SignalHandler>();

        return services;
    }
}
