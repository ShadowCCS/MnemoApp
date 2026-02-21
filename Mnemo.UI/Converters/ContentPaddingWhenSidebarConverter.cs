using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Mnemo.UI.Converters;

/// <summary>
/// Converts right-sidebar collapsed state to content area padding.
/// When sidebar is collapsed: normal right padding (40). When open: no right padding so content is not blocked.
/// </summary>
public class ContentPaddingWhenSidebarConverter : IValueConverter
{
    private static readonly Thickness PaddingSidebarClosed = new(40, 24, 40, 24);
    private static readonly Thickness PaddingSidebarOpen = new(40, 24, 0, 24);

    public static ContentPaddingWhenSidebarConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // IsCollapsed true = sidebar closed → use full padding. False = sidebar open → no right padding.
        var isCollapsed = value is bool b && b;
        return isCollapsed ? PaddingSidebarClosed : PaddingSidebarOpen;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
