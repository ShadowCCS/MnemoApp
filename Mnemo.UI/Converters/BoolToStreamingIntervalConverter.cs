using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Mnemo.UI.Converters;

/// <summary>
/// Converts bool to int for MarkdownView.StreamingUpdateIntervalMs: true -> 150 (live updates), false -> 0 (immediate).
/// </summary>
public class BoolToStreamingIntervalConverter : IValueConverter
{
    public static readonly BoolToStreamingIntervalConverter Instance = new();

    public int StreamingIntervalMs { get; set; } = 150;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return StreamingIntervalMs;
        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
