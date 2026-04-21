using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Mnemo.UI.Converters;

/// <summary>True when the bound expanded id equals the card id.</summary>
public sealed class FlashcardRowIsExpandedMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
            return false;
        var expanded = values[0] as string;
        var id = values[1] as string;
        return !string.IsNullOrEmpty(expanded) && expanded == id;
    }
}

/// <summary>True when the row is not in expanded editor mode (inverse of <see cref="FlashcardRowIsExpandedMultiConverter"/>).</summary>
public sealed class FlashcardRowIsCollapsedMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
            return true;
        var expanded = values[0] as string;
        var id = values[1] as string;
        return string.IsNullOrEmpty(expanded) || expanded != id;
    }
}
