namespace Mnemo.Core.Models.Widgets;

/// <summary>
/// Defines the category of a dashboard widget.
/// </summary>
public enum WidgetCategory
{
    /// <summary>
    /// Statistical information and metrics (e.g., flashcard count, study hours).
    /// </summary>
    Statistics,

    /// <summary>
    /// Recent activity and usage information (e.g., recent decks, today's goals).
    /// </summary>
    Activity,

    /// <summary>
    /// Insights and analytics (e.g., weak areas, performance trends).
    /// </summary>
    Insights,

    /// <summary>
    /// Quick action buttons and shortcuts (e.g., start session, create deck).
    /// </summary>
    QuickActions
}
