using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Mnemo.UI.Converters;

/// <summary>Converts double? to double: null -> NaN (layout auto), value -> value.</summary>
public class NullableDoubleToNaNConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d) return d;
        return double.NaN;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d && !double.IsNaN(d)) return d;
        return null;
    }
}
