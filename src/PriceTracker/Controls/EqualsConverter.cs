using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace PriceTracker.Controls;

/// value == parameter → true. Two-way: checking a radio button writes its parameter back.
public sealed class EqualsConverter : IValueConverter
{
    public static readonly EqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : BindingOperations.DoNothing;
}
