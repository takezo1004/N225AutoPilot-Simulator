namespace N225BrokerBridge.Domain.Common;

/// <summary>
/// ドメインルール違反を表す例外の基底クラス。
/// インフラ例外 (HTTP / IO 等) とは明確に区別する。
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }
    protected DomainException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// 値オブジェクトのコンストラクタで不変条件違反が起きた場合。
/// </summary>
public sealed class InvalidValueObjectException : DomainException
{
    public InvalidValueObjectException(string message) : base(message) { }
}
