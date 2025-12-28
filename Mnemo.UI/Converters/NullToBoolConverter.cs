using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Mnemo.UI.Converters
{
    public class NullToBoolConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var isNullOrEmpty = value is null || (value is string s && string.IsNullOrEmpty(s));
            var result = !isNullOrEmpty;
            return Invert ? !result : result;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}



