using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Mnemo.UI.Modules.Overview.ViewModels;

namespace Mnemo.UI.Modules.Overview.Controls;

/// <summary>
/// Container control for dashboard widgets.
/// Handles drag behavior and visual states.
/// </summary>
public class WidgetContainer : ContentControl
{
    private Point? _dragStartPoint;
    private Point? _initialMousePosition;
    private bool _isDragging;
    private Button? _removeButton;

    public static readonly StyledProperty<bool> IsEditModeProperty =
        AvaloniaProperty.Register<WidgetContainer, bool>(nameof(IsEditMode));

    public static readonly StyledProperty<DashboardWidgetViewModel?> WidgetProperty =
        AvaloniaProperty.Register<WidgetContainer, DashboardWidgetViewModel?>(nameof(Widget));

    public bool IsEditMode
    {
        get => GetValue(IsEditModeProperty);
        set => SetValue(IsEditModeProperty, value);
    }

    public DashboardWidgetViewModel? Widget
    {
        get => GetValue(WidgetProperty);
        set => SetValue(WidgetProperty, value);
    }

    public static readonly RoutedEvent<VectorEventArgs> DragStartedEvent =
        RoutedEvent.Register<WidgetContainer, VectorEventArgs>(nameof(DragStarted), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<VectorEventArgs> DragDeltaEvent =
        RoutedEvent.Register<WidgetContainer, VectorEventArgs>(nameof(DragDelta), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<VectorEventArgs> DragCompletedEvent =
        RoutedEvent.Register<WidgetContainer, VectorEventArgs>(nameof(DragCompleted), RoutingStrategies.Bubble);

    public event EventHandler<VectorEventArgs> DragStarted
    {
        add => AddHandler(DragStartedEvent, value);
        remove => RemoveHandler(DragStartedEvent, value);
    }

    public event EventHandler<VectorEventArgs> DragDelta
    {
        add => AddHandler(DragDeltaEvent, value);
        remove => RemoveHandler(DragDeltaEvent, value);
    }

    public event EventHandler<VectorEventArgs> DragCompleted
    {
        add => AddHandler(DragCompletedEvent, value);
        remove => RemoveHandler(DragCompletedEvent, value);
    }

    public static readonly RoutedEvent<RoutedEventArgs> RemoveRequestedEvent =
        RoutedEvent.Register<WidgetContainer, RoutedEventArgs>(nameof(RemoveRequested), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs> RemoveRequested
    {
        add => AddHandler(RemoveRequestedEvent, value);
        remove => RemoveHandler(RemoveRequestedEvent, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_removeButton != null)
        {
            _removeButton.Click -= OnRemoveButtonClick;
        }

        _removeButton = e.NameScope.Find<Button>("PART_RemoveButton");
        if (_removeButton != null)
        {
            _removeButton.Click += OnRemoveButtonClick;
        }
    }

    private void OnRemoveButtonClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RaiseEvent(new RoutedEventArgs(RemoveRequestedEvent));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Don't start drag when clicking the remove button
        if (_removeButton != null && _removeButton.IsPointerOver)
            return;

        if (IsEditMode && Widget != null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(this);
            _initialMousePosition = e.GetPosition(Parent as Visual);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_dragStartPoint.HasValue && Widget != null && IsEditMode)
        {
            var currentPosition = e.GetPosition(Parent as Visual);
            
            if (!_isDragging)
            {
                // Check if we've moved enough to start dragging (threshold to prevent accidental drags)
                var diff = currentPosition - _initialMousePosition!.Value;
                if (Math.Abs(diff.X) > 5 || Math.Abs(diff.Y) > 5)
                {
                    _isDragging = true;
                    Classes.Add("dragging");
                    Widget.StartDrag();
                    e.Pointer.Capture(this);
                    RaiseEvent(new VectorEventArgs { RoutedEvent = DragStartedEvent, Vector = new Vector(0, 0) });
                }
            }

            if (_isDragging)
            {
                // Calculate the new position based on drag
                var vector = currentPosition - _initialMousePosition!.Value;
                RaiseEvent(new VectorEventArgs { RoutedEvent = DragDeltaEvent, Vector = vector });
                e.Handled = true;
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isDragging && Widget != null)
        {
            Classes.Remove("dragging");
            // Commit or cancel the drag based on whether the position is valid
            // This will be handled by the parent view's logic
            // Widget.CommitDrag(); // Let the parent decide
            var currentPosition = e.GetPosition(Parent as Visual);
            var vector = currentPosition - _initialMousePosition!.Value;
            RaiseEvent(new VectorEventArgs { RoutedEvent = DragCompletedEvent, Vector = vector });
            
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        _dragStartPoint = null;
        _initialMousePosition = null;
        _isDragging = false;
        Classes.Remove("dragging");
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        if (_isDragging && Widget != null)
        {
            Widget.CancelDrag();
            Classes.Remove("dragging");
        }

        _dragStartPoint = null;
        _initialMousePosition = null;
        _isDragging = false;
    }
}
