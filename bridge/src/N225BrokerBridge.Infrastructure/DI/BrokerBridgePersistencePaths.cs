namespace N225BrokerBridge.Infrastructure.DI;

/// <summary>
/// 永続化ファイルのパス決定を 1 箇所に集約するヘルパー。
/// 本番モードと --simulator モードでファイル名を切替える (詳細: docs/simulator-mode.md §9-3 A 案)。
///
/// 本番モード: strategies.json / auto-positions.json / orders-metadata.json / appsettings.Local.json
/// シミュレータ: strategies.simulator.json / auto-positions.simulator.json / orders-metadata.simulator.json / appsettings.Local.simulator.json
/// </summary>
public static class BrokerBridgePersistencePaths
{
    private const string RootFolder = "N225BrokerBridge";

    private static string RootDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        RootFolder);

    private static string Suffix(bool simulator) => simulator ? ".simulator" : string.Empty;

    public static string Strategies(bool simulator) =>
        Path.Combine(RootDir, $"strategies{Suffix(simulator)}.json");

    public static string AutoPositions(bool simulator) =>
        Path.Combine(RootDir, $"auto-positions{Suffix(simulator)}.json");

    public static string OrdersMetadata(bool simulator) =>
        Path.Combine(RootDir, $"orders-metadata{Suffix(simulator)}.json");

    public static string PositionHistory(bool simulator) =>
        Path.Combine(RootDir, $"position-history{Suffix(simulator)}.json");

    public static string LocalSettings(bool simulator) =>
        Path.Combine(RootDir, $"appsettings.Local{Suffix(simulator)}.json");
}
