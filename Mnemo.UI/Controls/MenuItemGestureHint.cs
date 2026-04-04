using Avalonia;
using Avalonia.Controls;

namespace Mnemo.UI.Controls;

/// <summary>
/// Display-only shortcut label for <see cref="MenuItem"/>. Use instead of <see cref="MenuItem.InputGesture"/>
/// when the text must not be parsed as a <see cref="Avalonia.Input.KeyGesture"/> (e.g. ⌘N, ⏎, ⌫).
/// </summary>
public static class MenuItemGestureHint
{
    public static readonly AttachedProperty<string?> GestureHintProperty =
        AvaloniaProperty.RegisterAttached<MenuItem, string?>("GestureHint", typeof(MenuItemGestureHint));

    public static void SetGestureHint(MenuItem element, string? value) => element.SetValue(GestureHintProperty, value);

    public static string? GetGestureHint(MenuItem element) => element.GetValue(GestureHintProperty) as string;
}
