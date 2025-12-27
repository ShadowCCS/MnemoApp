using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Mnemo.UI.Converters
{
    public class SelectedBrushConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 2) return Brushes.Transparent;
            var selected = values[0]?.ToString();
            var name = values[1]?.ToString();
            var color = values.Count > 2 ? values[2]?.ToString() : null;
            var isSelected = !string.IsNullOrEmpty(selected) && string.Equals(selected, name, StringComparison.OrdinalIgnoreCase);
            if (!isSelected) return Brushes.Transparent;
            if (!string.IsNullOrWhiteSpace(color))
            {
                try
                {
                    return Brush.Parse(color);
                }
                catch
                {
                    // Fall through to default brush if parsing fails
                }
            }
            return new SolidColorBrush(Color.FromArgb(0x22, 0x88, 0x88, 0x88));
        }
    }
}



