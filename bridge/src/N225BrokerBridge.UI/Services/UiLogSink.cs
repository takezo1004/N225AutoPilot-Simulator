using System.Collections.ObjectModel;
using System.Windows;
using Serilog.Core;
using Serilog.Events;

namespace N225BrokerBridge.UI.Services;

/// <summary>
/// Serilog の出力を UI 表示用 ObservableCollection に流すカスタム sink。
/// MainViewModel が購読し、ListBox 等にバインドして表示する。
/// </summary>
public sealed class UiLogSink : ILogEventSink
{
    public ObservableCollection<UiLogEntry> Entries { get; } = new();

    /// <summary>保持する最大件数 (これを超えたら古い順に削除)。</summary>
    public int MaxEntries { get; set; } = 1000;

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Information) return;

        var entry = new UiLogEntry(
            Timestamp: logEvent.Timestamp.LocalDateTime,
            Level: logEvent.Level.ToString(),
            Message: logEvent.RenderMessage(),
            Exception: logEvent.Exception?.Message);

        // UI スレッドでコレクション更新
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
        {
            dispatcher.BeginInvoke(() => AppendEntry(entry));
        }
        else
        {
            AppendEntry(entry);
        }
    }

    private void AppendEntry(UiLogEntry entry)
    {
        // 最新が一番上に来るよう先頭挿入。古いログは末尾から削除。
        Entries.Insert(0, entry);
        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }
    }
}

public sealed record UiLogEntry(DateTime Timestamp, string Level, string Message, string? Exception);
