using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Mnemo.Core.Models;
using Mnemo.Core.Services;
using Mnemo.UI;

namespace Mnemo.UI.Converters;

/// <summary>Converts model routing mode id (Auto, Simple, Reasoning) to a localized Chat label.</summary>
public class ModelRoutingModeToLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string mode)
            return value;

        var loc = (Application.Current as App)?.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
        var normalized = ChatModelRouting.NormalizeModelRoutingMode(mode);
        var key = normalized switch
        {
            ChatModelRouting.Simple => "ModelRoutingSimple",
            ChatModelRouting.Reasoning => "ModelRoutingReasoning",
            _ => "ModelRoutingAuto"
        };
        return loc?.T(key, "Chat") ?? mode;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
