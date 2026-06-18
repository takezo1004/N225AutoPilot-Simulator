namespace N225BrokerBridge.Application.Signals;

/// <summary>
/// 自動売買のグローバル ON/OFF ゲート。
/// UI のトグル (MainViewModel.IsAutoTradeEnabled) と SignalHandler を疎結合に接続する。
/// SignalHandler は OFF の間、認証通過前に即 AutoTradeDisabled_ を返す (発注経路を完全停止)。
/// デフォルトは false (起動直後はトグルを ON にするまで全シグナル無視 = 安全側)。
/// </summary>
public interface IAutoTradeGate
{
    bool IsEnabled { get; set; }
}

public sealed class AutoTradeGate : IAutoTradeGate
{
    private volatile bool _enabled;
    public bool IsEnabled { get => _enabled; set => _enabled = value; }
}
