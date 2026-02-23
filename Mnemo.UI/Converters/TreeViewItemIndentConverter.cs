using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Mnemo.UI.Converters;

/// <summary>
/// Converts TreeViewItem Level and Indent to a left margin Thickness for hierarchical indentation.
/// </summary>
public class TreeViewItemIndentConverter : IMultiValueConverter
{
    public static TreeViewItemIndentConverter Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = values.Count > 0 && values[0] is int l ? l : 0;
        var indent = values.Count > 1 && values[1] is double d ? d : 16.0;
        return new Avalonia.Thickness(level * indent, 0, 0, 0);
    }
}
