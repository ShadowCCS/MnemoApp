using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Mnemo.Core.Services;
using Mnemo.UI;
using Mnemo.UI.Services;

namespace Mnemo.UI.Converters;

/// <summary>
/// Converts response-length mode id (Short, Normal, Detailed) to localized display label using Chat namespace.
/// </summary>
public class AssistantModeToLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string mode)
            return value;

        var loc = (Application.Current as App)?.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
        var normalized = ChatStreamingHelper.NormalizeAssistantMode(mode);
        var key = normalized switch
        {
            "Short" => "AssistantModeShort",
            "Detailed" => "AssistantModeDetailed",
            _ => "AssistantModeNormal"
        };
        return loc?.T(key, "Chat") ?? mode;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
