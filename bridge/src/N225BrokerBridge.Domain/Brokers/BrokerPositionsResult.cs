namespace N225BrokerBridge.Domain.Brokers;

/// <summary>
/// 建玉一覧照会の結果。「読めて N 件 (0 件含む) だった」のか、
/// 「そもそも読めなかった (未認証・夜間セッション切替・メンテ窓・通信エラー)」のかを、
/// 呼び出し側が <b>必ず</b> 区別できるようにするための型。
///
/// 背景 (なぜ必要か):
///   kabu の <c>/positions</c> は未認証・夜間セッション切替・メンテ窓・トークン失効時に、
///   建玉配列でなく空応答やエラーエンベロープ ({"Code":...,"Message":...}) を返す。
///   これを単なる空リスト (= 建玉ゼロ) と取り違えて建玉リコンサイルで除去すると、
///   <b>生きている建玉を全消ししてしまう</b> (2026-06-23 夜間に実害発生:
///   約定済みの自動建玉と一週間保有の手動建玉を一括除去)。
///
/// 規約:
///   - <see cref="IsAvailable"/> が false の間は <see cref="Positions"/> を信用してはならない。
///     建玉の追加・除去・自動取引メタの prune を <b>一切</b> 行わないこと。
///   - <see cref="IsAvailable"/> が true のときの空リストは「確定的に建玉ゼロ」を意味する
///     (この場合のみ外部決済の追従として除去してよい)。
/// </summary>
public sealed record BrokerPositionsResult
{
    /// <summary>kabu から建玉一覧を確定的に取得できたか。false の間は照会結果を信用してはならない。</summary>
    public bool IsAvailable { get; }

    /// <summary>取得できた建玉一覧 (IsAvailable=false のときは空)。</summary>
    public IReadOnlyList<PositionSnapshot> Positions { get; }

    private BrokerPositionsResult(bool isAvailable, IReadOnlyList<PositionSnapshot> positions)
    {
        IsAvailable = isAvailable;
        Positions = positions;
    }

    /// <summary>照会成功。0 件も含む「確定的な」結果。</summary>
    public static BrokerPositionsResult Available(IReadOnlyList<PositionSnapshot> positions) =>
        new(true, positions ?? Array.Empty<PositionSnapshot>());

    /// <summary>照会不能 (未認証・セッション外・メンテ・通信エラー)。結果を信用してはならない。</summary>
    public static BrokerPositionsResult Unavailable() =>
        new(false, Array.Empty<PositionSnapshot>());
}
