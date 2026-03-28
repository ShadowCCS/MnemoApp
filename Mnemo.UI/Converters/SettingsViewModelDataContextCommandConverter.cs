using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Mnemo.UI.Modules.Settings.ViewModels;

namespace Mnemo.UI.Converters;

public sealed class SettingsViewModelDataContextCommandConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SettingsViewModel vm)
            return null;

        return (parameter as string) switch
        {
            "SelectCategory" => vm.SelectCategoryCommand,
            _ => null
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
