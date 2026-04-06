using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    private ScrollViewer? _scrollAncestor;
    private bool _viewportEntranceComplete;
    private DispatcherTimer? _viewportEntranceFallbackTimer;

    public static readonly StyledProperty<bool> IsEditModeProperty =
        AvaloniaProperty.Register<WidgetContainer, bool>(nameof(IsEditMode));

    public static readonly StyledProperty<DashboardWidgetViewModel?> WidgetProperty =
        AvaloniaProperty.Register<WidgetContainer, DashboardWidgetViewModel?>(nameof(Widget));

    public static readonly StyledProperty<string> WidgetHeaderIconPathMarkupProperty =
        AvaloniaProperty.Register<WidgetContainer, string>(nameof(WidgetHeaderIconPathMarkup), string.Empty);

    public static readonly StyledProperty<string> WidgetHeaderTitleProperty =
        AvaloniaProperty.Register<WidgetContainer, string>(nameof(WidgetHeaderTitle), string.Empty);

    public static readonly StyledProperty<string> WidgetHeaderTranslationNamespaceProperty =
        AvaloniaProperty.Register<WidgetContainer, string>(nameof(WidgetHeaderTranslationNamespace), string.Empty);

    static WidgetContainer()
    {
        WidgetProperty.Changed.AddClassHandler<WidgetContainer>((c, _) => c.SyncHeaderFromWidget());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scrollAncestor = this.FindAncestorOfType<ScrollViewer>();
        if (_scrollAncestor == null)
        {
            _viewportEntranceComplete = true;
            return;
        }

        Opacity = 0;
        _scrollAncestor.ScrollChanged += OnScrollAncestorChanged;
        LayoutUpdated += OnLayoutUpdatedForViewportEntrance;
        Dispatcher.UIThread.Post(TryCompleteViewportEntrance, DispatcherPriority.Loaded);

        _viewportEntranceFallbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _viewportEntranceFallbackTimer.Tick += OnViewportEntranceFallbackTick;
        _viewportEntranceFallbackTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopViewportEntranceFallbackTimer();
        if (_scrollAncestor != null)
        {
            _scrollAncestor.ScrollChanged -= OnScrollAncestorChanged;
            _scrollAncestor = null;
        }
        LayoutUpdated -= OnLayoutUpdatedForViewportEntrance;
        base.OnDetachedFromVisualTree(e);
    }

    private void StopViewportEntranceFallbackTimer()
    {
        if (_viewportEntranceFallbackTimer == null) return;
        _viewportEntranceFallbackTimer.Stop();
        _viewportEntranceFallbackTimer.Tick -= OnViewportEntranceFallbackTick;
        _viewportEntranceFallbackTimer = null;
    }

    private void OnViewportEntranceFallbackTick(object? sender, EventArgs e)
    {
        StopViewportEntranceFallbackTimer();
        if (_viewportEntranceComplete) return;
        UnhookViewportEntrance();
        Transitions = null;
        Opacity = 1;
    }

    private void UnhookViewportEntrance()
    {
        _viewportEntranceComplete = true;
        if (_scrollAncestor != null)
            _scrollAncestor.ScrollChanged -= OnScrollAncestorChanged;
        LayoutUpdated -= OnLayoutUpdatedForViewportEntrance;
    }

    private void OnScrollAncestorChanged(object? sender, ScrollChangedEventArgs e) => TryCompleteViewportEntrance();

    private void OnLayoutUpdatedForViewportEntrance(object? sender, EventArgs e) => TryCompleteViewportEntrance();

    private void TryCompleteViewportEntrance()
    {
        if (_viewportEntranceComplete || _scrollAncestor == null) return;
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

        if (!IntersectsScrollViewport(_scrollAncestor))
            return;

        StopViewportEntranceFallbackTimer();
        UnhookViewportEntrance();

        Transitions ??= new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(420),
                Easing = new CubicEaseOut(),
            },
        };
        Opacity = 1;
    }

    /// <summary>
    /// True when this control's bounds overlap the scroll viewer's visible viewport (coordinates from <c>TranslatePoint</c> into the scroll viewer).
    /// </summary>
    private bool IntersectsScrollViewport(ScrollViewer scroll)
    {
        var topLeft = this.TranslatePoint(new Point(0, 0), scroll);
        if (!topLeft.HasValue) return false;

        double w = scroll.Viewport.Width;
        double h = scroll.Viewport.Height;
        if (w <= 0 || h <= 0) return true;

        var r = new Rect(topLeft.Value, Bounds.Size);
        var viewport = new Rect(0, 0, w, h);
        return r.Intersects(viewport);
    }

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

    public string WidgetHeaderIconPathMarkup
    {
        get => GetValue(WidgetHeaderIconPathMarkupProperty);
        set => SetValue(WidgetHeaderIconPathMarkupProperty, value);
    }

    public string WidgetHeaderTitle
    {
        get => GetValue(WidgetHeaderTitleProperty);
        set => SetValue(WidgetHeaderTitleProperty, value);
    }

    public string WidgetHeaderTranslationNamespace
    {
        get => GetValue(WidgetHeaderTranslationNamespaceProperty);
        set => SetValue(WidgetHeaderTranslationNamespaceProperty, value);
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

    private void SyncHeaderFromWidget()
    {
        var metadata = Widget?.Metadata;
        var icon = metadata?.Icon;
        SetValue(
            WidgetHeaderIconPathMarkupProperty,
            string.IsNullOrEmpty(icon) ? string.Empty : icon);
        SetValue(WidgetHeaderTitleProperty, metadata?.Title ?? string.Empty);
        SetValue(WidgetHeaderTranslationNamespaceProperty, metadata?.TranslationNamespace ?? string.Empty);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        SyncHeaderFromWidget();

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
