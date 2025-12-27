using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Mnemo.UI.Converters
{
    public class BoolToBrushConverter : AvaloniaObject, IValueConverter
    {
        public static readonly StyledProperty<IBrush?> TrueBrushProperty =
            AvaloniaProperty.Register<BoolToBrushConverter, IBrush?>(nameof(TrueBrush));

        public static readonly StyledProperty<IBrush?> FalseBrushProperty =
            AvaloniaProperty.Register<BoolToBrushConverter, IBrush?>(nameof(FalseBrush));

        public IBrush? TrueBrush
        {
            get => GetValue(TrueBrushProperty);
            set => SetValue(TrueBrushProperty, value);
        }

        public IBrush? FalseBrush
        {
            get => GetValue(FalseBrushProperty);
            set => SetValue(FalseBrushProperty, value);
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var isTrue = value is bool b && b;
            
            if (isTrue)
            {
                return TrueBrush ?? new SolidColorBrush(Color.FromArgb(0xFF, 0x20, 0x79, 0xFF));
            }
            
            return FalseBrush ?? new SolidColorBrush(Color.FromArgb(0xFF, 0x55, 0x55, 0x55));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}



