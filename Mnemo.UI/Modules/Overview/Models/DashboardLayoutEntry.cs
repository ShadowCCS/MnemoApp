namespace Mnemo.UI.Modules.Overview.Models;

/// <summary>
/// Persisted entry for a single widget on the dashboard (position and size).
/// </summary>
public record DashboardLayoutEntry(
    string WidgetId,
    int Column,
    int Row,
    int ColSpan,
    int RowSpan);
