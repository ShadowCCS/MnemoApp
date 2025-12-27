using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Mnemo.UI.Converters
{
    public class IsNotLastItemConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // For now, let's always show the separator
            // The proper solution would require access to the current item index
            // which is complex to achieve with the current binding context
            // This is a limitation of the current approach
            return true;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}

