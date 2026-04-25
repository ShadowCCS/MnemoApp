using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Mnemo.UI.Converters;

public sealed class IntToLeftIndentConverter : IValueConverter
{
    public static IntToLeftIndentConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var depth = value is int i ? i : 0;
        return new Thickness(Math.Max(0, depth) * 16, 0, 0, 0);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
