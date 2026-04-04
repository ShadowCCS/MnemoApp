using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Mnemo.UI.Controls;

namespace Mnemo.UI.Converters;

/// <summary>
/// Resolves shortcut text: <see cref="MenuItemGestureHint.GestureHint"/> if set, otherwise <see cref="MenuItem.InputGesture"/> string.
/// </summary>
public sealed class MenuItemShortcutDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MenuItem mi) return null;
        var hint = MenuItemGestureHint.GetGestureHint(mi);
        if (!string.IsNullOrEmpty(hint)) return hint;
        return mi.InputGesture?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
