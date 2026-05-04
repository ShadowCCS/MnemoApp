using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Mnemo.UI.Converters;

public class BoolToObjectConverter : AvaloniaObject, IValueConverter
{
    public static readonly StyledProperty<object?> TrueValueProperty =
        AvaloniaProperty.Register<BoolToObjectConverter, object?>(nameof(TrueValue));

    public static readonly StyledProperty<object?> FalseValueProperty =
        AvaloniaProperty.Register<BoolToObjectConverter, object?>(nameof(FalseValue));

    public object? TrueValue
    {
        get => GetValue(TrueValueProperty);
        set => SetValue(TrueValueProperty, value);
    }

    public object? FalseValue
    {
        get => GetValue(FalseValueProperty);
        set => SetValue(FalseValueProperty, value);
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isTrue = value is bool b && b;
        return isTrue ? TrueValue : FalseValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
