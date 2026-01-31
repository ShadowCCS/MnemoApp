using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Mnemo.UI.Converters;

/// <summary>
/// Converts a hex color string to IBrush. When value is null or empty, returns the brush from the theme resource key passed as ConverterParameter.
/// </summary>
public sealed class StringToBrushWithFallbackConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = parameter as string;
        IBrush? Fallback()
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (Application.Current?.Resources.TryGetResource(key, ThemeVariant.Default, out var res) != true)
                return null;
            return res as IBrush;
        }
        if (string.IsNullOrWhiteSpace(value as string))
            return Fallback() ?? Brushes.Gray;
        var hex = value!.ToString()!;
        if (Color.TryParse(hex, out var color))
            return new SolidColorBrush(color);
        return Fallback() ?? Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
