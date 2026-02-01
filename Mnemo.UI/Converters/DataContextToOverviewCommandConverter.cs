using System;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Data.Converters;
using Mnemo.UI.Modules.Mindmap.ViewModels;

namespace Mnemo.UI.Converters;

/// <summary>
/// Returns OpenMindmapCommand or DeleteMindmapCommand from MindmapOverviewViewModel.
/// Handles null DataContext so item-template bindings do not throw.
/// </summary>
public class DataContextToOverviewCommandConverter : IValueConverter
{
    public bool ReturnDeleteCommand { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MindmapOverviewViewModel vm)
            return null;
        return ReturnDeleteCommand ? (object?)vm.DeleteMindmapCommand : vm.OpenMindmapCommand;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
