using N225BrokerBridge.Domain.Common;

namespace N225BrokerBridge.Application.Signals;

/// <summary>
/// TradingView 戦略の市場ポジション状態 ("flat" / "long" / "short")。
/// </summary>
public enum MarketPositionState
{
    Flat,
    Long,
    Short
}

public static class MarketPositionStateExtensions
{
    /// <summary>"flat"/"long"/"short" 文字列から enum に解釈する (大文字小文字無視)。</summary>
    public static MarketPositionState Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidValueObjectException("MarketPosition must not be empty.");

        return raw.Trim().ToLowerInvariant() switch
        {
            "flat" => MarketPositionState.Flat,
            "long" => MarketPositionState.Long,
            "short" => MarketPositionState.Short,
            _ => throw new InvalidValueObjectException($"Unknown MarketPosition: '{raw}'")
        };
    }
}
