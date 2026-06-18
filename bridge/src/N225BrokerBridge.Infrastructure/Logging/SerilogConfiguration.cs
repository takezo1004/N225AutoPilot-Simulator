using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace N225BrokerBridge.Infrastructure.Logging;

/// <summary>
/// Serilog の標準設定。
/// - コンソール sink (開発時の即時確認)
/// - ファイル sink (logs/n225brokerbridge-YYYY-MM-DD.log、日次ローテーション、7 日保持)
/// - 構造化ログ (パラメータが {Property} 構文で別フィールドに展開される)
/// </summary>
public static class SerilogConfiguration
{
    public static ILoggerFactory CreateLoggerFactory(string logDirectory = "logs")
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(logDirectory, "n225brokerbridge-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}")
            .CreateLogger();

        Log.Logger = logger;

        return new SerilogLoggerFactory(logger, dispose: true);
    }
}
