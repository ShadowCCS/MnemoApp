using CommunityToolkit.Mvvm.ComponentModel;
using Mnemo.Core.Models.Widgets;
using Mnemo.Core.Services;

namespace Mnemo.UI.Modules.Overview.ViewModels;

/// <summary>
/// Host ViewModel for a dashboard widget tile.
/// Manages position, size, and wraps the actual widget content ViewModel.
/// </summary>
public partial class DashboardWidgetViewModel : ObservableObject
{
    /// <summary>
    /// Gets the widget ID for persistence.
    /// </summary>
    public string WidgetId { get; }

    /// <summary>
    /// Gets the actual widget content ViewModel.
    /// </summary>
    public IWidgetViewModel Content { get; }

    /// <summary>
    /// Gets or sets the current position in the grid.
    /// </summary>
    [ObservableProperty]
    private WidgetPosition _position;

    /// <summary>
    /// Gets the size of this widget (from metadata).
    /// </summary>
    public WidgetSize Size { get; }

    /// <summary>
    /// Gets or sets whether this widget is currently being dragged.
    /// </summary>
    [ObservableProperty]
    private bool _isDragging;

    /// <summary>
    /// Gets or sets the temporary drag position (used during drag operation).
    /// </summary>
    [ObservableProperty]
    private WidgetPosition _dragPosition;

    /// <summary>
    /// Gets or sets whether the dashboard is in edit mode (synced from OverviewViewModel).
    /// </summary>
    [ObservableProperty]
    private bool _isEditMode;

    /// <summary>
    /// Gets the metadata for this widget.
    /// </summary>
    public WidgetMetadata Metadata { get; }

    public DashboardWidgetViewModel(
        string widgetId,
        WidgetMetadata metadata,
        IWidgetViewModel content,
        WidgetPosition position,
        WidgetSize size)
    {
        WidgetId = widgetId;
        Metadata = metadata;
        Content = content;
        Position = position;
        Size = size;
        DragPosition = position;
    }

    /// <summary>
    /// Starts a drag operation.
    /// </summary>
    public void StartDrag()
    {
        IsDragging = true;
        DragPosition = Position;
    }

    /// <summary>
    /// Updates the drag position during a drag operation.
    /// </summary>
    public void UpdateDragPosition(WidgetPosition newPosition)
    {
        if (IsDragging)
        {
            DragPosition = newPosition;
        }
    }

    /// <summary>
    /// Commits the drag operation, updating the actual position.
    /// </summary>
    public void CommitDrag()
    {
        if (IsDragging)
        {
            Position = DragPosition;
            IsDragging = false;
        }
    }

    /// <summary>
    /// Cancels the drag operation, reverting to the original position.
    /// </summary>
    public void CancelDrag()
    {
        IsDragging = false;
        DragPosition = Position;
    }
}
