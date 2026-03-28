using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Mnemo.UI.Converters;

/// <summary>
/// Converts a bool to one of two strings specified via <see cref="TrueText"/> / <see cref="FalseText"/>,
/// or through a static singleton for common cases.
/// </summary>
public class BoolToTextConverter : IValueConverter
{
    public static readonly BoolToTextConverter HideShow = new() { TrueText = "Hide", FalseText = "Show" };

    public string TrueText { get; set; } = string.Empty;
    public string FalseText { get; set; } = string.Empty;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TrueText : FalseText;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
