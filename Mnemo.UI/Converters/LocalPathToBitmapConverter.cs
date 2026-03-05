using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Mnemo.UI.Converters;

/// <summary>
/// Converts a local file path to a Bitmap for preview. Returns null if the path is invalid or not an image.
/// </summary>
public class LocalPathToBitmapConverter : IValueConverter
{
    public static readonly LocalPathToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return null;
        if (!File.Exists(path))
            return null;
        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
