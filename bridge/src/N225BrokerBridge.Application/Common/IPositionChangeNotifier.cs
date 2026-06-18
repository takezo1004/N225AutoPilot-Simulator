using N225BrokerBridge.Domain.Positions;
using N225BrokerBridge.Domain.ValueObjects;

namespace N225BrokerBridge.Application.Common;

/// <summary>
/// IPositionRepository への変更を購読するための通知抽象。
/// UI (MainViewModel) が建玉一覧を最新状態に保つために使う。
/// 実装は IPositionRepository 実装側 (例: InMemoryPositionRepository) が同居する。
/// </summary>
public interface IPositionChangeNotifier
{
    event EventHandler<PositionChangedEventArgs>? Changed;
}

public enum PositionChangeKind
{
    Added,
    Updated,
    Removed
}

public sealed class PositionChangedEventArgs : EventArgs
{
    public PositionChangeKind Kind { get; }

    /// <summary>Added / Updated 時は当該 Position、Removed 時は null。</summary>
    public Position? Position { get; }

    /// <summary>Removed 時の対象 ExecutionId (Position が null になる代わり)。</summary>
    public ExecutionId? RemovedId { get; }

    private PositionChangedEventArgs(PositionChangeKind kind, Position? position, ExecutionId? removedId)
    {
        Kind = kind;
        Position = position;
        RemovedId = removedId;
    }

    public static PositionChangedEventArgs Added(Position p) =>
        new(PositionChangeKind.Added, p, null);

    public static PositionChangedEventArgs Updated(Position p) =>
        new(PositionChangeKind.Updated, p, null);

    public static PositionChangedEventArgs Removed(ExecutionId id) =>
        new(PositionChangeKind.Removed, null, id);
}
