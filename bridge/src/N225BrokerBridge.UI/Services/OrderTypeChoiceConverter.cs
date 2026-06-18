using System.Globalization;
using System.Windows.Data;
using N225BrokerBridge.UI.ViewModels;

namespace N225BrokerBridge.UI.Services;

/// <summary>
/// OrderTypeChoice enum と RadioButton.IsChecked のバインディング用コンバーター。
/// XAML: IsChecked="{Binding OrderType, Converter=..., ConverterParameter=BestMarket}"
/// </summary>
public sealed class OrderTypeChoiceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not OrderTypeChoice current || parameter is not string s) return false;
        return Enum.TryParse<OrderTypeChoice>(s, out var target) && current == target;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string s && Enum.TryParse<OrderTypeChoice>(s, out var target))
            return target;
        return Binding.DoNothing;
    }
}
