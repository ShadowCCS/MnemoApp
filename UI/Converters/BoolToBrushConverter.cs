using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MnemoApp.UI.Converters
{
    public class BoolToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var isTrue = value is bool b && b;
            if (isTrue)
            {
                if (parameter is IBrush brushParam) return brushParam;
                if (parameter is string s)
                {
                    try
                    {
                        return Brush.Parse(s);
                    }
                    catch
                    {
                        // Fall through to default brush
                    }
                }
                return new SolidColorBrush(Color.FromArgb(0x22, 0x88, 0x88, 0x88));
            }
            return Brushes.Transparent;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}


