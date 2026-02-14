using System.Collections.Generic;
using System.Linq;
using Mnemo.Core.Models.Widgets;
using Mnemo.Core.Services;

namespace Mnemo.UI.Services;

/// <summary>
/// Implementation of the widget registry service.
/// Manages registration and lookup of dashboard widgets.
/// </summary>
public class WidgetRegistry : IWidgetRegistry
{
    private readonly Dictionary<string, IWidget> _widgets = new();

    /// <inheritdoc/>
    public void RegisterWidget(IWidget widget)
    {
        if (widget == null)
            throw new ArgumentNullException(nameof(widget));

        if (string.IsNullOrWhiteSpace(widget.Metadata.Id))
            throw new ArgumentException("Widget ID cannot be null or empty.", nameof(widget));

        if (_widgets.ContainsKey(widget.Metadata.Id))
            throw new InvalidOperationException($"Widget with ID '{widget.Metadata.Id}' is already registered.");

        _widgets[widget.Metadata.Id] = widget;
    }

    /// <inheritdoc/>
    public IEnumerable<IWidget> GetAllWidgets()
    {
        return _widgets.Values.ToList();
    }

    /// <inheritdoc/>
    public IEnumerable<IWidget> GetWidgetsByCategory(WidgetCategory category)
    {
        return _widgets.Values
            .Where(w => w.Metadata.Category == category)
            .ToList();
    }

    /// <inheritdoc/>
    public IWidget? GetWidgetById(string id)
    {
        return _widgets.TryGetValue(id, out var widget) ? widget : null;
    }
}
