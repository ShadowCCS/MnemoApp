using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Mnemo.UI.Modules.Mindmap.ViewModels;

namespace Mnemo.UI.Modules.Mindmap.Views;

public partial class MindmapView : UserControl
{
    private const string MindmapGridColorKey = "MindmapGridColor";
    private double _gridSpacing = 40.0;
    private double _gridDotSize = 1.5;
    private double _gridOpacity = 0.2;
    private string _gridType = "Dotted";
    private string _modifierBehaviour = "Selecting";
    private string _minimapVisibilityMode = "Auto";

    private const double DotScaleExponent = 0.35;
    private const double BaseDotScreenPixels = 3.5;
    private const double MinDotSourceSize = 0.5;
    private const double MaxDotSourceSize = 10.0;

    private Matrix _minimapMatrix = Matrix.Identity;
    private Rect _viewportRectInMinimap;
    private enum MinimapDragMode { None, ViewportRect, Outside }
    private MinimapDragMode _minimapDragMode = MinimapDragMode.None;
    private Point _minimapPressContentPoint; // content point at press (for viewport drag delta)
    private Point _minimapLastMmPoint;       // last minimap position (for 1:1 pan)
    private bool _minimapHasMoved;
    private double _lastMinimapWidth;
    private double _lastMinimapHeight;
    private double _lastMainCanvasWidth;
    private double _minimapContentMinX, _minimapContentMaxX, _minimapContentMinY, _minimapContentMaxY;
    private bool _minimapContentBoundsInitialized;
    private double _lastMainCanvasHeight;
    private bool _isDragging;
    private bool _isPanning;
    private bool _isSelecting;
    private bool _addToSelectionOnBoxSelect;
    private bool _hasMovedSignificantly;
    private Point _selectionStart;
    private Point _selectionStartInCanvas; // content-space start, fixed at press so zoom doesn't break hit-test
    private Point _selectionCurrentInCanvas; // content-space current position, updated as mouse moves
    private Point _lastPointerPosition;
    private NodeViewModel? _draggedNode;
    private Matrix _transformMatrix = Matrix.Identity;
    private VisualBrush? _gridBrush;
    private ISettingsService? _settingsService;
    private Border? _selectionBox;

    private const double ClickDragThreshold = 5.0; // pixels
    private const double MinimapZoomThreshold = 0.6; // show minimap when scale <= 60%
    private const int MinimapEaseMs = 250;

    public MindmapView()
    {
        InitializeComponent();

        var canvas = this.FindControl<Panel>("MainCanvas");
        if (canvas != null)
        {
            canvas.PointerWheelChanged += OnCanvasPointerWheelChanged;
            canvas.SizeChanged += OnMainCanvasSizeChanged;
        }

        _selectionBox = this.FindControl<Border>("SelectionBox");

        DataContextChanged += OnDataContextChanged;

        Loaded += OnViewLoaded;

        // Try to get settings service from global locator or wait for DataContext
        if (Application.Current is Mnemo.UI.App mnemoApp && mnemoApp.Services != null)
        {
            _settingsService = (ISettingsService?)mnemoApp.Services.GetService(typeof(ISettingsService));
        }

        if (_settingsService != null)
        {
            _settingsService.SettingChanged += OnSettingChanged;
            _ = LoadSettingsAsync();
        }
    }

    private void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        UpdateMinimap();
        UpdateMinimapVisibility();
        Loaded -= OnViewLoaded;
    }

    private void OnSettingChanged(object? sender, string key)
    {
        if (key.StartsWith("Mindmap.Grid"))
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () => {
                await LoadSettingsAsync();
                _gridBrush = null; // Force recreation
                UpdateGrid();
            });
        }
        else if (key == "Mindmap.ModifierBehaviour")
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(LoadSettingsAsync);
        }
        else if (key == "Mindmap.MinimapVisibility")
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () => {
                await LoadSettingsAsync();
                UpdateMinimapVisibility();
            });
        }
    }

    private async Task LoadSettingsAsync()
    {
        if (_settingsService == null) return;
        
        _gridType = await _settingsService.GetAsync("Mindmap.GridType", "Dotted");
        _modifierBehaviour = await _settingsService.GetAsync("Mindmap.ModifierBehaviour", "Selecting");
        _minimapVisibilityMode = await _settingsService.GetAsync("Mindmap.MinimapVisibility", "Auto") ?? "Auto";
        var sizeStr = await _settingsService.GetAsync("Mindmap.GridSize", "40");
        var dotSizeStr = await _settingsService.GetAsync("Mindmap.GridDotSize", "1.5");
        var opacityStr = await _settingsService.GetAsync("Mindmap.GridOpacity", "0.2");

        if (double.TryParse(sizeStr, System.Globalization.CultureInfo.InvariantCulture, out var size)) _gridSpacing = size;
        if (double.TryParse(dotSizeStr, System.Globalization.CultureInfo.InvariantCulture, out var dotSize)) _gridDotSize = dotSize;
        if (double.TryParse(opacityStr, System.Globalization.CultureInfo.InvariantCulture, out var opacity)) _gridOpacity = opacity;

        UpdateMinimapVisibility();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MindmapViewModel vm)
        {
            vm.RecenterRequested += OnRecenterRequested;
            vm.Nodes.CollectionChanged += OnNodesCollectionChanged;
        }
    }

    private void OnNodesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // Subscribe/unsubscribe node property changes so minimap updates when nodes move
        if (e.NewItems != null)
        {
            foreach (NodeViewModel node in e.NewItems)
                node.PropertyChanged += OnNodePropertyChangedForMinimap;
        }
        if (e.OldItems != null)
        {
            foreach (NodeViewModel node in e.OldItems)
                node.PropertyChanged -= OnNodePropertyChangedForMinimap;
        }

        // Auto-recenter after nodes are loaded, with a delay to allow layout
        if (DataContext is MindmapViewModel vm && vm.Nodes.Any())
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await Task.Delay(50); // Allow layout to complete
                RecenterView();
                UpdateMinimap();
            });
        }
    }

    private void OnNodePropertyChangedForMinimap(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NodeViewModel.X) or nameof(NodeViewModel.Y)
                            or nameof(NodeViewModel.Width) or nameof(NodeViewModel.Height))
        {
            UpdateMinimap();
        }
    }

    private void OnRecenterRequested(object? sender, EventArgs e)
    {
        RecenterView();
    }

    private void OnMainCanvasSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            _lastMainCanvasWidth = e.NewSize.Width;
            _lastMainCanvasHeight = e.NewSize.Height;
            UpdateTransform();
        }
    }

    public void RecenterView()
    {
        if (DataContext is not MindmapViewModel vm || !vm.Nodes.Any()) return;

        var nodes = vm.Nodes;

        double centerX = (nodes.Min(n => n.X) + nodes.Max(n => n.X + (n.Width ?? 120))) / 2;
        double centerY = (nodes.Min(n => n.Y) + nodes.Max(n => n.Y + (n.Height ?? 40))) / 2;

        var mainCanvas = this.FindControl<Panel>("MainCanvas");
        if (mainCanvas == null) return;

        double viewportWidth = mainCanvas.Bounds.Width;
        double viewportHeight = mainCanvas.Bounds.Height;
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        // Keep current zoom; only pan so content center is at viewport center
        double scale = GetScaleFromMatrix(_transformMatrix);
        double offsetX = viewportWidth / 2 - centerX * scale;
        double offsetY = viewportHeight / 2 - centerY * scale;

        _transformMatrix = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY);
        UpdateTransform();
    }

    private const double MinScale = 0.1;
    private const double MaxScale = 5.0;

    private void OnCanvasPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var zoomDelta = e.Delta.Y > 0 ? 1.1 : 0.9;
        var mainCanvas = this.FindControl<Panel>("MainCanvas");
        // Use MainCanvas coordinates consistently with selection/panning
        var pointerPos = mainCanvas != null ? e.GetPosition(mainCanvas) : e.GetPosition(this);

        double det = _transformMatrix.M11 * _transformMatrix.M22 - _transformMatrix.M12 * _transformMatrix.M21;
        if (Math.Abs(det) < 1e-10)
            _transformMatrix = Matrix.Identity;

        double currentScale = GetScaleFromMatrix(_transformMatrix);
        double newScale = currentScale * zoomDelta;
        newScale = Math.Clamp(newScale, MinScale, MaxScale);
        zoomDelta = newScale / currentScale;
        if (Math.Abs(zoomDelta - 1.0) < 1e-6) return; // No change

        // Get the point in canvas coordinates before zoom
        var inverseMatrix = _transformMatrix.Invert();
        var canvasPointBefore = pointerPos * inverseMatrix;

        // Apply the scale
        _transformMatrix = _transformMatrix * Matrix.CreateScale(zoomDelta, zoomDelta);

        // Get the same canvas point after zoom in screen coordinates
        var screenPointAfter = canvasPointBefore * _transformMatrix;

        // Calculate the offset and adjust translation to keep the point under the cursor
        // Translation on right = screen-space offset (applied after existing transform)
        var offset = pointerPos - screenPointAfter;
        _transformMatrix = _transformMatrix * Matrix.CreateTranslation(offset.X, offset.Y);

        UpdateTransform();
        
        // Update selection box visual position if we're currently selecting
        if (_isSelecting)
        {
            // Update current selection position to match where the cursor now is in content-space
            // This keeps the selection's "current" corner tracking the mouse during zoom
            var inv = _transformMatrix.Invert();
            _selectionCurrentInCanvas = pointerPos * inv;
            UpdateSelectionBoxFromContentSpace();
        }
        
        e.Handled = true;
    }

    private void UpdateTransform()
    {
        var canvas = this.FindControl<Panel>("TransformCanvas");
        if (canvas != null)
        {
            // Assign new instance so Avalonia sees a change and applies zoom/pan
            canvas.RenderTransform = new MatrixTransform(_transformMatrix);
            canvas.InvalidateArrange();
            canvas.InvalidateVisual();
        }

        UpdateGrid();
        UpdateMinimap();
        UpdateMinimapVisibility();
        // Run again after layout so viewport box uses definitive Bounds
        Avalonia.Threading.Dispatcher.UIThread.Post(UpdateMinimap, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void UpdateMinimapVisibility()
    {
        var minimapBorder = this.FindControl<Border>("MinimapBorder");
        if (minimapBorder == null) return;

        bool byZoom = GetScaleFromMatrix(_transformMatrix) <= MinimapZoomThreshold;
        bool visible = _minimapVisibilityMode switch
        {
            "Off" => false,
            "On" => true,
            _ => byZoom // "Auto" or any other
        };

        minimapBorder.Opacity = visible ? 1 : 0;
        minimapBorder.IsHitTestVisible = visible;
    }

    private Color? GetGridColorFromTheme()
    {
        return this.TryFindResource(MindmapGridColorKey, out var value) && value is Color color ? color : null;
    }

    private VisualBrush CreateGridBrush(Color gridColor)
    {
        if (_gridType == "None") return new VisualBrush();

        var stroke = new SolidColorBrush(gridColor) { Opacity = _gridOpacity };
        var gridCanvas = new Canvas
        {
            Width = _gridSpacing,
            Height = _gridSpacing,
            Background = Brushes.Transparent
        };

        if (_gridType == "Dotted")
        {
            gridCanvas.Children.Add(new Avalonia.Controls.Shapes.Ellipse
            {
                Width = _gridDotSize,
                Height = _gridDotSize,
                Fill = stroke,
                [Canvas.LeftProperty] = 0,
                [Canvas.TopProperty] = 0
            });
        }
        else if (_gridType == "Lines")
        {
            gridCanvas.Children.Add(new Avalonia.Controls.Shapes.Line
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(_gridSpacing, 0),
                Stroke = stroke,
                StrokeThickness = _gridDotSize
            });
            gridCanvas.Children.Add(new Avalonia.Controls.Shapes.Line
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, _gridSpacing),
                Stroke = stroke,
                StrokeThickness = _gridDotSize
            });
        }

        return new VisualBrush
        {
            Visual = gridCanvas,
            TileMode = TileMode.Tile,
            SourceRect = new RelativeRect(0, 0, _gridSpacing, _gridSpacing, RelativeUnit.Absolute),
            DestinationRect = new RelativeRect(0, 0, _gridSpacing, _gridSpacing, RelativeUnit.Absolute)
        };
    }

    private void UpdateGrid()
    {
        var gridCanvas = this.FindControl<Canvas>("GridCanvas");
        if (gridCanvas == null) return;

        if (_gridType == "None")
        {
            gridCanvas.Background = null;
            return;
        }

        var gridColor = GetGridColorFromTheme();
        if (gridColor == null)
        {
            gridCanvas.Background = null;
            return;
        }

        if (_gridBrush == null)
        {
            _gridBrush = CreateGridBrush(gridColor.Value);
        }

        double scale = GetScaleFromMatrix(_transformMatrix);
        
        // Adjust dot size based on zoom if it's dotted
        if (_gridType == "Dotted")
        {
            double dotScreenSize = _gridDotSize * scale * Math.Pow(scale, DotScaleExponent - 1);
            double dotSizeSource = Math.Clamp(dotScreenSize / scale, MinDotSourceSize, MaxDotSourceSize);

            if (_gridBrush.Visual is Canvas visualCanvas && visualCanvas.Children.FirstOrDefault() is Avalonia.Controls.Shapes.Ellipse ellipse)
            {
                ellipse.Width = dotSizeSource;
                ellipse.Height = dotSizeSource;
            }
        }

        double scaledSpacing = _gridSpacing * scale;
        double offsetX = _transformMatrix.M31 % scaledSpacing;
        double offsetY = _transformMatrix.M32 % scaledSpacing;
        if (offsetX < 0) offsetX += scaledSpacing;
        if (offsetY < 0) offsetY += scaledSpacing;

        _gridBrush.DestinationRect = new RelativeRect(offsetX, offsetY, scaledSpacing, scaledSpacing, RelativeUnit.Absolute);
        gridCanvas.Background = _gridBrush;
    }

    private static double GetScaleFromMatrix(Matrix matrix)
    {
        double scale = Math.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12);
        return scale <= 0 ? 1.0 : scale;
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed) return;

        bool isShiftPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        // When ModifierBehaviour is "Selecting": Shift + drag = select, no modifier = pan. When "Panning": opposite.
        bool modifierMeansSelect = _modifierBehaviour == "Selecting";
        bool doPan = modifierMeansSelect ? !isShiftPressed : isShiftPressed;
        bool doSelect = !doPan;

        var mainCanvas = this.FindControl<Panel>("MainCanvas");

        // Only when click was on empty space (node handler sets e.Handled when clicking a node)
        if (!e.Handled)
        {
            _hasMovedSignificantly = false; // Reset movement tracking

            // Move focus to canvas so the node TextBox loses focus (deselects visually)
            if (sender is Control focusTarget)
                focusTarget.Focus();

            if (doPan)
            {
                _isPanning = true;
                _isSelecting = false;
                // Don't deselect here - wait for release to see if it was a click or drag
            }
            else
            {
                _isPanning = false;
                _isSelecting = true;
                _addToSelectionOnBoxSelect = e.KeyModifiers.HasFlag(KeyModifiers.Control);
                _selectionStart = mainCanvas != null ? e.GetPosition(mainCanvas) : e.GetPosition(this);
                var inv = _transformMatrix.Invert();
                _selectionStartInCanvas = _selectionStart * inv;
                _selectionCurrentInCanvas = _selectionStartInCanvas; // Initialize to start position
                UpdateSelectionBox(_selectionStart, _selectionStart);
                if (_selectionBox != null) _selectionBox.IsVisible = true;
            }
        }
        else
        {
            _isPanning = false;
            _isSelecting = false;
        }

        _lastPointerPosition = e.GetPosition(this);
    }

    private void UpdateSelectionBox(Point start, Point end)
    {
        if (_selectionBox == null) return;

        // Calculate box bounds - one corner is at start (where mouse was pressed)
        // and the opposite corner follows the current mouse position
        double x = Math.Min(start.X, end.X);
        double y = Math.Min(start.Y, end.Y);
        double width = Math.Abs(end.X - start.X);
        double height = Math.Abs(end.Y - start.Y);

        // Position the box using margin instead of transform for cleaner positioning
        _selectionBox.Margin = new Thickness(x, y, 0, 0);
        _selectionBox.Width = width;
        _selectionBox.Height = height;
    }

    private void OnNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is NodeViewModel node)
        {
            if (DataContext is MindmapViewModel vm)
            {
                // Multi-select with Ctrl, otherwise single select
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    foreach (var n in vm.Nodes) n.IsSelected = false;
                }
                node.IsSelected = true;
            }

            _isDragging = true;
            _isPanning = false;
            _draggedNode = node;
            _lastPointerPosition = e.GetPosition(this);
            e.Handled = true;
        }
    }

    private void OnNodePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isDragging && _draggedNode != null)
        {
            var currentPosition = e.GetPosition(this);
            var delta = currentPosition - _lastPointerPosition;
            
            // Adjust delta by current zoom
            double scale = GetScaleFromMatrix(_transformMatrix);
            
            var dx = delta.X / scale;
            var dy = delta.Y / scale;

            if (DataContext is MindmapViewModel vm)
            {
                var selectedNodes = vm.Nodes.Where(n => n.IsSelected).ToList();
                if (selectedNodes.Contains(_draggedNode))
                {
                    foreach (var node in selectedNodes)
                    {
                        node.X += dx;
                        node.Y += dy;
                    }
                }
                else
                {
                    _draggedNode.X += dx;
                    _draggedNode.Y += dy;
                }
            }
            
            _lastPointerPosition = currentPosition;
            UpdateMinimap();
            e.Handled = true;
        }
        else if (_isPanning)
        {
            var currentPosition = e.GetPosition(this);
            var delta = currentPosition - _lastPointerPosition;
            
            // Track if we've moved significantly
            if (Math.Abs(delta.X) > ClickDragThreshold || Math.Abs(delta.Y) > ClickDragThreshold)
            {
                _hasMovedSignificantly = true;
            }
            
            _transformMatrix = _transformMatrix * Matrix.CreateTranslation(delta.X, delta.Y);
            UpdateTransform();
            
            _lastPointerPosition = currentPosition;
            e.Handled = true;
        }
        else if (_isSelecting)
        {
            var mainCanvas = this.FindControl<Panel>("MainCanvas");
            var currentPosition = mainCanvas != null ? e.GetPosition(mainCanvas) : e.GetPosition(this);
            
            // Track if we've moved significantly
            var delta = currentPosition - _selectionStart;
            if (Math.Abs(delta.X) > ClickDragThreshold || Math.Abs(delta.Y) > ClickDragThreshold)
            {
                _hasMovedSignificantly = true;
            }
            
            if (DataContext is MindmapViewModel vm)
            {
                var inv = _transformMatrix.Invert();
                _selectionCurrentInCanvas = currentPosition * inv;

                UpdateNodeSelection(vm);
            }
            e.Handled = true;
        }
    }

    private void UpdateNodeSelection(MindmapViewModel vm)
    {
        var minX = Math.Min(_selectionStartInCanvas.X, _selectionCurrentInCanvas.X);
        var maxX = Math.Max(_selectionStartInCanvas.X, _selectionCurrentInCanvas.X);
        var minY = Math.Min(_selectionStartInCanvas.Y, _selectionCurrentInCanvas.Y);
        var maxY = Math.Max(_selectionStartInCanvas.Y, _selectionCurrentInCanvas.Y);

        var contentRect = new Rect(minX, minY, maxX - minX, maxY - minY);
        
        // Convert content rect back to screen space for visual selection box
        var topLeft = new Point(contentRect.X, contentRect.Y) * _transformMatrix;
        var bottomRight = new Point(contentRect.Right, contentRect.Bottom) * _transformMatrix;
        UpdateSelectionBox(topLeft, bottomRight);
        
        foreach (var node in vm.Nodes)
        {
            var nodeRect = new Rect(node.X, node.Y, node.Width ?? 0, node.Height ?? 0);
            bool inBox = contentRect.Intersects(nodeRect);
            node.IsSelected = _addToSelectionOnBoxSelect ? (node.IsSelected || inBox) : inBox;
        }
    }

    private void UpdateSelectionBoxFromContentSpace()
    {
        if (DataContext is not MindmapViewModel vm) return;
        
        // Re-compute visual box from stored content-space coordinates
        // Both start and current are already in content space and don't change during zoom
        UpdateNodeSelection(vm);
    }

    private async void OnNodePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging && _draggedNode != null && DataContext is MindmapViewModel vm)
        {
            var selectedNodes = vm.Nodes.Where(n => n.IsSelected).ToList();
            if (selectedNodes.Contains(_draggedNode))
            {
                foreach (var node in selectedNodes)
                {
                    await vm.UpdateNodePositionAsync(node, node.X, node.Y);
                }
            }
            else
            {
                await vm.UpdateNodePositionAsync(_draggedNode, _draggedNode.X, _draggedNode.Y);
            }
            
            _isDragging = false;
            _draggedNode = null;
            e.Handled = true;
        }
        
        if (_isSelecting)
        {
            _isSelecting = false;
            if (_selectionBox != null) _selectionBox.IsVisible = false;
            e.Handled = true;
        }
        
        if (_isPanning)
        {
            // If we're panning and didn't move significantly, it was a click - deselect all nodes
            if (!_hasMovedSignificantly && DataContext is MindmapViewModel viewModel)
            {
                foreach (var node in viewModel.Nodes) node.IsSelected = false;
            }
            
            _isPanning = false;
            e.Handled = true;
        }
    }

    private async void OnNodeTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is NodeViewModel node && DataContext is MindmapViewModel vm)
        {
            await vm.UpdateNodeTextAsync(node, textBox.Text ?? string.Empty);
        }
    }

    private void OnNodeSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not NodeViewModel node)
            return;

        double w = e.NewSize.Width;
        double h = e.NewSize.Height;
        if (node.Shape == "circle")
        {
            double side = Math.Max(w, h);
            control.Width = side;
            control.Height = side;
            w = side;
            h = side;
        }
        node.Width = w;
        node.Height = h;
    }

    private void OnNodeTextBoxTextChanged(object? sender, TextChangedEventArgs e)
    {
        // When text changes, clear circle node's fixed size so it can re-measure and grow
        if (sender is not Control textBox || textBox.DataContext is not NodeViewModel node || node.Shape != "circle")
            return;
        var contentControl = textBox.Parent?.Parent as Control;
        if (contentControl != null)
        {
            contentControl.Width = double.NaN;
            contentControl.Height = double.NaN;
        }
    }

    private void UpdateMinimap()
    {
        var minimapPanel = this.FindControl<Panel>("MinimapPanel");
        var minimapTransformPanel = this.FindControl<Panel>("MinimapTransformPanel");
        var minimapViewportBox = this.FindControl<Border>("MinimapViewportBox");
        var mainCanvas = this.FindControl<Panel>("MainCanvas");

        if (minimapPanel == null || minimapTransformPanel == null || minimapViewportBox == null || mainCanvas == null) return;

        if (DataContext is not MindmapViewModel vm || !vm.Nodes.Any())
        {
            minimapViewportBox.IsVisible = false;
            _minimapContentBoundsInitialized = false;
            return;
        }

        minimapViewportBox.IsVisible = true;

        double minimapWidth = minimapPanel.Bounds.Width > 0 ? minimapPanel.Bounds.Width : _lastMinimapWidth;
        double minimapHeight = minimapPanel.Bounds.Height > 0 ? minimapPanel.Bounds.Height : _lastMinimapHeight;
        if (minimapWidth <= 0 || minimapHeight <= 0) return;

        _lastMinimapWidth = minimapWidth;
        _lastMinimapHeight = minimapHeight;

        // --- Fit all nodes into the minimap (allow shrink, never grow so dragging a node out doesn't expand minimap) ---
        var nodes = vm.Nodes;
        double curMinX = nodes.Min(n => n.X);
        double curMaxX = nodes.Max(n => n.X + (n.Width ?? 120));
        double curMinY = nodes.Min(n => n.Y);
        double curMaxY = nodes.Max(n => n.Y + (n.Height ?? 40));

        if (!_minimapContentBoundsInitialized)
        {
            _minimapContentMinX = curMinX;
            _minimapContentMaxX = curMaxX;
            _minimapContentMinY = curMinY;
            _minimapContentMaxY = curMaxY;
            _minimapContentBoundsInitialized = true;
        }
        else
        {
            // Only shrink the tracked bounds when content is fully inside (never grow)
            if (curMinX >= _minimapContentMinX && curMaxX <= _minimapContentMaxX &&
                curMinY >= _minimapContentMinY && curMaxY <= _minimapContentMaxY)
            {
                _minimapContentMinX = curMinX;
                _minimapContentMaxX = curMaxX;
                _minimapContentMinY = curMinY;
                _minimapContentMaxY = curMaxY;
            }
        }

        double minX = _minimapContentMinX;
        double maxX = _minimapContentMaxX;
        double minY = _minimapContentMinY;
        double maxY = _minimapContentMaxY;
        double contentWidth  = maxX - minX;
        double contentHeight = maxY - minY;

        const double padding = 10;
        double scaleX = (minimapWidth  - padding * 2) / Math.Max(contentWidth,  1);
        double scaleY = (minimapHeight - padding * 2) / Math.Max(contentHeight, 1);
        double mmScaleFit = Math.Min(scaleX, scaleY);
        double viewportScale = GetScaleFromMatrix(_transformMatrix);
        double mmScale = mmScaleFit * viewportScale;

        // Offset so node content is centered in the minimap
        double mmOffsetX = minimapWidth  / 2.0 - (minX + contentWidth  / 2.0) * mmScale;
        double mmOffsetY = minimapHeight / 2.0 - (minY + contentHeight / 2.0) * mmScale;

        var minimapMatrix = Matrix.CreateScale(mmScale, mmScale)
                          * Matrix.CreateTranslation(mmOffsetX, mmOffsetY);

        if (minimapTransformPanel.RenderTransform is MatrixTransform mmt)
            mmt.Matrix = minimapMatrix;
        else
            minimapTransformPanel.RenderTransform = new MatrixTransform(minimapMatrix);

        _minimapMatrix = minimapMatrix;

        // Size transform panel to fill minimap (Canvas does not size children)
        minimapTransformPanel.Width = minimapWidth;
        minimapTransformPanel.Height = minimapHeight;

        minimapTransformPanel.InvalidateVisual();

        // --- Map the main viewport into minimap space ---
        double vpWidth  = mainCanvas.Bounds.Width  > 0 ? mainCanvas.Bounds.Width  : _lastMainCanvasWidth;
        double vpHeight = mainCanvas.Bounds.Height > 0 ? mainCanvas.Bounds.Height : _lastMainCanvasHeight;
        if (vpWidth <= 0 || vpHeight <= 0) return;

        _lastMainCanvasWidth  = vpWidth;
        _lastMainCanvasHeight = vpHeight;

        double det = _transformMatrix.M11 * _transformMatrix.M22
                   - _transformMatrix.M12 * _transformMatrix.M21;
        if (Math.Abs(det) < 1e-10) return;

        // _transformMatrix: content → screen.  Invert to get screen → content.
        var invMain = _transformMatrix.Invert();

        // Viewport corners in screen space → content space → minimap space
        var tlContent = new Point(0,       0)       * invMain;
        var brContent = new Point(vpWidth, vpHeight) * invMain;

        var tlMm = tlContent * minimapMatrix;
        var brMm = brContent * minimapMatrix;

        double boxX = Math.Min(tlMm.X, brMm.X);
        double boxY = Math.Min(tlMm.Y, brMm.Y);
        double boxW = Math.Abs(brMm.X - tlMm.X);
        double boxH = Math.Abs(brMm.Y - tlMm.Y);

        _viewportRectInMinimap = new Rect(boxX, boxY, boxW, boxH);


        const double viewportBoxScale = 0.90;
        double displayW = Math.Max(boxW * viewportBoxScale, 2);
        double displayH = Math.Max(boxH * viewportBoxScale, 2);
        double displayX = boxX + (boxW - displayW) / 2;
        double displayY = boxY + (boxH - displayH) / 2;

        minimapViewportBox.Width  = displayW;
        minimapViewportBox.Height = displayH;

        if (minimapViewportBox.RenderTransform is TranslateTransform tt)
        {
            tt.X = displayX;
            tt.Y = displayY;
        }
        else
            minimapViewportBox.RenderTransform = new TranslateTransform(displayX, displayY);

        minimapViewportBox.InvalidateVisual();
    }

    private void OnMinimapSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            _lastMinimapWidth = e.NewSize.Width;
            _lastMinimapHeight = e.NewSize.Height;
        }
        UpdateMinimap();
    }

    private void OnMinimapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(this).Properties;
        if (!properties.IsLeftButtonPressed) return;

        var minimapPanel = this.FindControl<Panel>("MinimapPanel");
        if (minimapPanel == null) return;

        var ptMm = e.GetPosition(minimapPanel);
        _minimapHasMoved = false;
        bool onViewport = _viewportRectInMinimap.Contains(ptMm);

        if (onViewport)
        {
            _minimapDragMode = MinimapDragMode.ViewportRect;
            _minimapLastMmPoint = ptMm;
        }
        else
        {
            _minimapDragMode = MinimapDragMode.Outside;
            _minimapLastMmPoint = ptMm;
            var invMinimap = _minimapMatrix.Invert();
            _minimapPressContentPoint = ptMm * invMinimap;
            // Snap viewport to cursor position (instant), then subsequent move will pan
            CenterViewportOnContentPoint(_minimapPressContentPoint);
        }
        e.Handled = true;
    }

    private void OnMinimapPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_minimapDragMode == MinimapDragMode.None) return;

        var minimapPanel = this.FindControl<Panel>("MinimapPanel");
        var mainCanvas = this.FindControl<Panel>("MainCanvas");
        if (minimapPanel == null || mainCanvas == null) return;

        var ptMm = e.GetPosition(minimapPanel);
        double mmScale = Math.Sqrt(_minimapMatrix.M11 * _minimapMatrix.M11 + _minimapMatrix.M12 * _minimapMatrix.M12);
        if (mmScale < 1e-10) return;

        if (Math.Abs(ptMm.X - _minimapLastMmPoint.X) > ClickDragThreshold ||
            Math.Abs(ptMm.Y - _minimapLastMmPoint.Y) > ClickDragThreshold)
            _minimapHasMoved = true;

        if (_minimapDragMode == MinimapDragMode.ViewportRect)
        {
            // 1:1 pan: delta in minimap space → same delta in content space → screen delta = contentDelta * scale
            double scale = GetScaleFromMatrix(_transformMatrix);
            double contentDx = (ptMm.X - _minimapLastMmPoint.X) / mmScale;
            double contentDy = (ptMm.Y - _minimapLastMmPoint.Y) / mmScale;
            double screenDx = contentDx * scale;
            double screenDy = contentDy * scale;
            _transformMatrix = _transformMatrix * Matrix.CreateTranslation(screenDx, screenDy);
            UpdateTransform();
        }
        else if (_minimapDragMode == MinimapDragMode.Outside)
        {
            // Pan so that content under cursor stays under cursor (same as snap then drag)
            var invMinimap = _minimapMatrix.Invert();
            var contentPoint = ptMm * invMinimap;
            CenterViewportOnContentPoint(contentPoint);
        }

        _minimapLastMmPoint = ptMm;
        e.Handled = true;
    }

    private void OnMinimapPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_minimapDragMode == MinimapDragMode.Outside && !_minimapHasMoved)
        {
            // Click (no drag): center on point with short ease animation
            CenterViewportOnContentPointWithEase(_minimapPressContentPoint);
        }
        _minimapDragMode = MinimapDragMode.None;
        e.Handled = true;
    }

    private void CenterViewportOnContentPoint(Point contentPoint)
    {
        var mainCanvas = this.FindControl<Panel>("MainCanvas");
        if (mainCanvas == null) return;
        double vpW = mainCanvas.Bounds.Width > 0 ? mainCanvas.Bounds.Width : _lastMainCanvasWidth;
        double vpH = mainCanvas.Bounds.Height > 0 ? mainCanvas.Bounds.Height : _lastMainCanvasHeight;
        if (vpW <= 0 || vpH <= 0) return;
        double scale = GetScaleFromMatrix(_transformMatrix);
        double offsetX = vpW / 2.0 - contentPoint.X * scale;
        double offsetY = vpH / 2.0 - contentPoint.Y * scale;
        _transformMatrix = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY);
        UpdateTransform();
    }

    private void CenterViewportOnContentPointWithEase(Point contentPoint)
    {
        var mainCanvas = this.FindControl<Panel>("MainCanvas");
        if (mainCanvas == null) return;
        double vpW = mainCanvas.Bounds.Width > 0 ? mainCanvas.Bounds.Width : _lastMainCanvasWidth;
        double vpH = mainCanvas.Bounds.Height > 0 ? mainCanvas.Bounds.Height : _lastMainCanvasHeight;
        if (vpW <= 0 || vpH <= 0) return;
        double scale = GetScaleFromMatrix(_transformMatrix);
        double targetOffsetX = vpW / 2.0 - contentPoint.X * scale;
        double targetOffsetY = vpH / 2.0 - contentPoint.Y * scale;
        var targetMatrix = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(targetOffsetX, targetOffsetY);

        var startMatrix = _transformMatrix;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        void Tick(object? s, EventArgs args)
        {
            double t = sw.ElapsedMilliseconds / (double)MinimapEaseMs;
            if (t >= 1)
            {
                _transformMatrix = targetMatrix;
                UpdateTransform();
                timer.Stop();
                timer.Tick -= Tick;
                return;
            }
            double u = 1 - t;
            double e = 1 - (u * u * u); // ease-out cubic
            double m31 = startMatrix.M31 + (targetMatrix.M31 - startMatrix.M31) * e;
            double m32 = startMatrix.M32 + (targetMatrix.M32 - startMatrix.M32) * e;
            _transformMatrix = new Matrix(scale, 0, 0, scale, m31, m32);
            UpdateTransform();
        }
        timer.Tick += Tick;
        timer.Start();
    }
}
