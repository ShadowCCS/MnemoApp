using Mnemo.Core.Models.Widgets;

namespace Mnemo.Core.Services;

/// <summary>
/// Service for registering and querying available dashboard widgets.
/// </summary>
public interface IWidgetRegistry
{
    /// <summary>
    /// Registers a widget type with the registry.
    /// </summary>
    /// <param name="widget">The widget to register.</param>
    void RegisterWidget(IWidget widget);

    /// <summary>
    /// Gets all registered widgets.
    /// </summary>
    IEnumerable<IWidget> GetAllWidgets();

    /// <summary>
    /// Gets all widgets in a specific category.
    /// </summary>
    /// <param name="category">The category to filter by.</param>
    IEnumerable<IWidget> GetWidgetsByCategory(WidgetCategory category);

    /// <summary>
    /// Gets a widget by its unique identifier.
    /// </summary>
    /// <param name="id">The widget ID.</param>
    /// <returns>The widget, or null if not found.</returns>
    IWidget? GetWidgetById(string id);
}
