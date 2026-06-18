using System.Collections;
using System.Globalization;
using System.Windows.Data;
using N225BrokerBridge.UI.ViewModels;

namespace N225BrokerBridge.UI.Services;

/// <summary>
/// CollectionViewGroup.Items を受け取り、ConverterParameter に応じて集計値を返す。
///
/// 旧 N225OrderBridge の TradeViewModel.ApplyHeaderAggregate と同じ集計:
///   - sumLeaveQty:       合計残数量
///   - sumHoldQty:        合計拘束数量
///   - weightedAvgPrice:  数量加重平均価格 (qtySum==0 → 0)
///   - sumProfit:         合計損益
///   - count:             件数
///
/// XAML 利用例:
///   <TextBlock Text="{Binding Items,
///       Converter={StaticResource GroupAgg}, ConverterParameter=sumLeaveQty}"/>
/// </summary>
public sealed class GroupAggregateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IEnumerable enumerable) return string.Empty;

        var positions = enumerable.OfType<PositionRow>().ToList();
        if (positions.Count == 0) return string.Empty;

        var op = parameter as string ?? "count";

        return op switch
        {
            "count" => positions.Count.ToString(culture),
            "sumLeaveQty" => positions.Sum(p => p.LeaveQty).ToString(culture),
            "sumHoldQty" => positions.Sum(p => p.HoldQty).ToString(culture),
            "weightedAvgPrice" => ComputeWeightedAvg(positions).ToString("N0", culture),
            "sumProfit" => positions.Sum(p => p.Profit).ToString("N0", culture),
            _ => string.Empty
        };
    }

    private static decimal ComputeWeightedAvg(IReadOnlyList<PositionRow> positions)
    {
        int totalQty = positions.Sum(p => p.LeaveQty);
        if (totalQty == 0) return 0m;
        decimal weighted = positions.Sum(p => p.Price * p.LeaveQty);
        return Math.Round(weighted / totalQty);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
