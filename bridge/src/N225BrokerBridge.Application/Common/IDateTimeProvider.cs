namespace N225BrokerBridge.Application.Common;

/// <summary>
/// 現在時刻を取得する抽象。テストで時刻を固定できるようアプリ層で DI 注入する。
/// </summary>
public interface IDateTimeProvider
{
    /// <summary>現在 UTC 時刻。</summary>
    DateTime UtcNow { get; }
}

/// <summary>
/// 本番実装。<see cref="DateTime.UtcNow"/> を返す。
/// </summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
