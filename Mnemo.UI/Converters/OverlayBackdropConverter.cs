using System;
using System.Globalization;
using System.Collections.Generic;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Mnemo.UI.Converters
{
    public class OverlayBackdropConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            // values: [Brush?, Color, double]
            if (values.Count >= 1 && values[0] is IBrush brush)
            {
                return brush;
            }
            var color = Colors.Black;
            if (values.Count >= 2)
            {
                if (values[1] is Color c) color = c;
                else if (values[1] is string s && Color.TryParse(s, out var parsed)) color = parsed;
            }
            var opacity = values.Count >= 3 && values[2] is double d ? d : 0.45;
            return new SolidColorBrush(color, opacity);
        }
    }
}



