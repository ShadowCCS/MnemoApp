using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Mnemo.Core.Models.Mindmap;
using Mnemo.Core.Services;
using Mnemo.UI;
using Mnemo.UI.Modules.Mindmap.ViewModels;
using LayoutAlgorithms = Mnemo.Core.Models.Mindmap.LayoutAlgorithms;

namespace Mnemo.UI.Modules.Mindmap;

public class MindmapContentToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TextNodeContent textContent) return textContent.Text;
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class NodeToCenterPointConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is NodeViewModel node)
        {
            // Approximate center based on current position and typical node size
            // Real implementation would use actual bounds if available
            double width = node.Width ?? 120;
            double height = node.Height ?? 40;
            return new Point(node.X + width / 2, node.Y + height / 2);
        }
        return new Point(0, 0);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class EdgeKindToStrokeDashArrayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MindmapEdgeKind kind && kind == MindmapEdgeKind.Link)
        {
            return new AvaloniaList<double> { 4, 4 };
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>Converts edge type string to StrokeDashArray for path rendering.</summary>
public class EdgeTypeToStrokeDashArrayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var type = value as string;
        return type switch
        {
            EdgeTypes.Dashed => new AvaloniaList<double> { 6, 4 },
            EdgeTypes.Dotted => new AvaloniaList<double> { 2, 3 },
            _ => null // solid, double, bidirect, arrow: solid line
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>Converts edge type string to opacity (all full opacity).</summary>
public class EdgeTypeToOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return 1.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>Returns true when value equals parameter (string). Used to show double line / bidirect arrows by type.</summary>
public class EdgeTypeEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value?.ToString();
        var p = parameter?.ToString();
        return string.Equals(s, p, StringComparison.OrdinalIgnoreCase);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>Returns true when value equals one of the semicolon-separated parameter strings (e.g. "bidirect;arrow").</summary>
public class EdgeTypeInConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value?.ToString();
        var p = parameter?.ToString();
        if (string.IsNullOrEmpty(p)) return false;
        foreach (var part in p.Split(';'))
            if (string.Equals(s, part.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>Returns true when value is not null (for edge label box visibility).</summary>
public class NotNullToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value != null;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>When value (SelectedEdge) is null returns localized "Select an edge to change its type"; otherwise null (no tooltip).</summary>
public class SelectedEdgeToTooltipConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value != null) return null;
        var loc = (Application.Current as App)?.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
        return loc?.T("SelectEdgeToChangeType", "Mindmap") ?? "Select an edge to change its type";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>Converts double to Avalonia CornerRadius (uniform).</summary>
public class DoubleToCornerRadiusConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var d = value is double v ? v : 0;
        return new CornerRadius(d);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>When value (EffectiveMinSize) &gt; 0 returns value; else returns parameter parsed as double (e.g. 80 or 40).</summary>
public class MindmapMinSizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d && d > 0) return d;
        if (parameter != null && double.TryParse(parameter.ToString(), NumberStyles.Any, culture, out var fallback))
            return fallback;
        return 80.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>Converts layout algorithm ID to display name for the layout dropdown using Mindmap namespace.</summary>
public class LayoutAlgorithmToDisplayNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string id) return value?.ToString();
        var loc = (Application.Current as App)?.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
        var key = id switch
        {
            LayoutAlgorithms.TreeVertical => "LayoutTreeVertical",
            LayoutAlgorithms.TreeHorizontal => "LayoutTreeHorizontal",
            LayoutAlgorithms.Radial => "LayoutRadial",
            _ => null
        };
        return key != null ? loc?.T(key, "Mindmap") ?? id : id;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>Converts MindmapEdgeKind to localized display name for toolbar.</summary>
public class EdgeKindToDisplayNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MindmapEdgeKind kind) return value?.ToString();
        var key = kind == MindmapEdgeKind.Link ? "EdgeDashed" : "EdgeSolid";
        var loc = (Application.Current as App)?.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
        return loc?.T(key, "Mindmap") ?? key;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>Multi-value converter: returns true when all values are true (for edge label visibility).</summary>
public class BoolAndConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count < 2) return false;
        return values[0] is true && values[1] is true;
    }
}

/// <summary>Converts minimap visibility mode string to localized display name.</summary>
public class MinimapVisibilityToDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s) return value?.ToString();
        var key = s switch { "On" => "MinimapOn", "Off" => "MinimapOff", _ => "MinimapAuto" };
        var loc = (Application.Current as App)?.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
        return loc?.T(key, "Mindmap") ?? s;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
