using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Mnemo.Core.Services;
using Mnemo.UI;

namespace Mnemo.UI.Converters;

/// <summary>
/// Converts assistant mode id ("General", "Explainer") to localized display label using Chat namespace.
/// </summary>
public class AssistantModeToLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string mode)
            return value;

        var loc = (Application.Current as App)?.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
        var key = mode switch
        {
            "Explainer" => "AssistantModeExplainer",
            _ => "AssistantModeGeneral"
        };
        return loc?.T(key, "Chat") ?? mode;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
