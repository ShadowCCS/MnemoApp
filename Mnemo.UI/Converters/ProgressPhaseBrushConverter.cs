using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Mnemo.UI.Converters;

public sealed class ProgressPhaseBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double progress;
        try
        {
            progress = value is IConvertible c
                ? System.Convert.ToDouble(c, CultureInfo.InvariantCulture)
                : 0d;
        }
        catch
        {
            progress = 0d;
        }

        // Defaults assume 0-100 range
        double lowThreshold = 33d;
        double highThreshold = 66d;

        if (parameter is string p && !string.IsNullOrWhiteSpace(p))
        {
            // Accept formats like "30|70" or "30,70"
            var parts = p.Split(new[] { '|', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var low) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var high))
            {
                lowThreshold = low;
                highThreshold = high;
            }
        }

        string brushKey = progress < lowThreshold
            ? "ProgressBarPhaseHighBrush"
            : (progress < highThreshold ? "ProgressBarPhaseMediumBrush" : "ProgressBarPhaseLowBrush");

        string colorKey = progress < lowThreshold
            ? "ProgressBarPhaseHigh"
            : (progress < highThreshold ? "ProgressBarPhaseMedium" : "ProgressBarPhaseLow");

        if (Application.Current?.Resources.TryGetResource(brushKey, ThemeVariant.Default, out var brushRes) == true)
        {
            if (brushRes is IBrush b) return b;
            if (brushRes is Color bc) return new SolidColorBrush(bc);
        }

        if (Application.Current?.Resources.TryGetResource(colorKey, ThemeVariant.Default, out var colorRes) == true)
        {
            if (colorRes is Color c) return new SolidColorBrush(c);
            if (colorRes is IBrush bb) return bb;
        }

        // Final fallback to visible defaults
        return progress < lowThreshold
            ? new SolidColorBrush(Colors.IndianRed)
            : (progress < highThreshold ? new SolidColorBrush(Colors.Goldenrod) : new SolidColorBrush(Colors.ForestGreen));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return BindingOperations.DoNothing;
    }
}



