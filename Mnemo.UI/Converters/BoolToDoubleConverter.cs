using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Mnemo.UI.Converters;

/// <summary>Converts bool to double. Parameter "TrueValue,FalseValue" e.g. "1,0.3".</summary>
public class BoolToDoubleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isTrue = value is bool b && b;
        if (parameter is not string param)
            throw new InvalidOperationException(
                $"BoolToDoubleConverter requires ConverterParameter as string (e.g. \"1|0.65\"). Received: {parameter?.GetType().FullName ?? "null"} = {parameter}.");
        param = param.Trim().Trim('\'', '"');
        var parts = param.Split(new[] { '|', ';', ',' }, 2, StringSplitOptions.None);
        if (parts.Length < 2 || !double.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var trueVal)
            || !double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var falseVal))
            throw new InvalidOperationException(
                $"BoolToDoubleConverter parameter must be \"TrueValue,FalseValue\" or \"TrueValue|FalseValue\". Received: \"{param}\".");
        return isTrue ? trueVal : falseVal;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
