using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Mnemo.UI.Converters;

public class FolderIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isFolder)
        {
            return isFolder 
                ? "avares://Mnemo.UI/Icons/Common/book.svg" 
                : "avares://Mnemo.UI/Icons/Common/file-text.svg";
        }
        return "avares://Mnemo.UI/Icons/Common/file-text.svg";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}