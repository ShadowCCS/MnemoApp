using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Mnemo.Core.Models;

namespace Mnemo.UI.Converters;

public class StatusToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AITaskStatus status && parameter is string targetStatus)
        {
            return status.ToString().Equals(targetStatus, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}



