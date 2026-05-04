using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Layout;

namespace Mnemo.UI.Converters;

/// <summary>Maps flashcard image alignment tokens (<c>left|center|right</c>) to <see cref="HorizontalAlignment"/>.</summary>
public sealed class FlashcardImageAlignToHorizontalAlignmentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = (value as string ?? string.Empty).Trim().ToLowerInvariant();
        return s switch
        {
            "left" => HorizontalAlignment.Left,
            "right" => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
