namespace Mnemo.Core.Models.Widgets;

/// <summary>
/// Contains metadata about a widget type for display in the widget gallery.
/// </summary>
public class WidgetMetadata
{
    /// <summary>
    /// Gets the unique identifier for this widget type.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the display title of the widget.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the description of what this widget displays.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the category this widget belongs to.
    /// </summary>
    public WidgetCategory Category { get; }

    /// <summary>
    /// Gets the icon resource key or URI for this widget.
    /// </summary>
    public string Icon { get; }

    /// <summary>
    /// Gets the default size for this widget.
    /// </summary>
    public WidgetSize DefaultSize { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WidgetMetadata"/> class.
    /// </summary>
    public WidgetMetadata(
        string id,
        string title,
        string description,
        WidgetCategory category,
        string icon,
        WidgetSize defaultSize)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        Icon = icon;
        DefaultSize = defaultSize;
    }
}
