using N225BrokerBridge.Application.Common;

namespace N225BrokerBridge.Application.Tests.TestDoubles;

/// <summary>
/// テスト用: 固定時刻を返す。
/// </summary>
public sealed class FixedDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = new(2026, 5, 17, 9, 0, 0, DateTimeKind.Utc);
}
