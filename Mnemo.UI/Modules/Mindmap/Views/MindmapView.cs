using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Collections;
using Mnemo.Core.Enums;
using Mnemo.Core.Models.Mindmap;
using Mnemo.Core.Services;
using MindmapModel = Mnemo.Core.Models.Mindmap.Mindmap;
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
    private double _lastMainCanvasHeight;
    private bool _isDragging;
    private bool _isPanning;
    private bool _isSelecting;
    private bool _addToSelectionOnBoxSelect;
    private bool _hasMovedSignificantly;
    private bool _hasMovedNodeDuringDrag;
    private Point _selectionStart;
    private Point _selectionStartInCanvas; // content-space start, fixed at press so zoom doesn't break hit-test
    private Point _selectionCurrentInCanvas; // content-space current position, updated as mouse moves
    private Point _lastPointerPosition;
    private NodeViewModel? _draggedNode;
    private MindmapModel? _moveBeforeSnapshot; // captured at drag start for correct undo
    private Matrix _transformMatrix = Matrix.Identity;
    private VisualBrush? _gridBrush;
    private ISettingsService? _settingsService;
    private Border? _selectionBox;

    private const double ClickDragThreshold = 5.0; // pixels
    private const double MinimapZoomThreshold = 0.6; // show minimap when scale <= 60%
    private const int MinimapEaseMs = 250;
    private const double EdgeDoubleClickMs = 400;
    private const double EdgeDoubleClickDist = 15;
    private const double EdgeHitThreshold = 20;
    private const int EdgeHoverThrottleMs = 32; // ~30fps max for expensive GetDistanceToCurve loop

    private DateTime _lastEdgeClickTime = DateTime.MinValue;
    private Point? _lastEdgeClickContentPoint;
    private string? _lastEdgeClickEdgeId;
    private DispatcherTimer? _easeTimer;
    private long _lastEdgeHoverUpdateTicks = 0;

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
        Unloaded += OnViewUnloaded;

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

    private void OnViewUnloaded(object? sender, RoutedEventArgs e)
    {
        _easeTimer?.Stop();
        _easeTimer = null;
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
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (DataContext is MindmapViewModel vm)
                    await vm.RefreshGlobalMinimapSettingAsync();
                UpdateMinimapVisibility();
            });
        }
    }

    private async Task LoadSettingsAsync()
    {
        if (_settingsService == null) return;
        
        _gridType = await _settingsService.GetAsync("Mindmap.GridType", "Dotted");
        _modifierBehaviour = await _settingsService.GetAsync("Mindmap.ModifierBehaviour", "Selecting");
        var sizeStr = await _settingsService.GetAsync("Mindmap.GridSize", "40");
        var dotSizeStr = await _settingsService.GetAsync("Mindmap.GridDotSize", "1.5");
        var opacityStr = await _settingsService.GetAsync("Mindmap.GridOpacity", "0.2");

        if (double.TryParse(sizeStr, System.Globalization.CultureInfo.InvariantCulture, out var size)) _gridSpacing = size;
        if (double.TryParse(dotSizeStr, System.Globalization.CultureInfo.InvariantCulture, out var dotSize)) _gridDotSize = dotSize;
        if (double.TryParse(opacityStr, System.Globalization.CultureInfo.InvariantCulture, out var opacity)) _gridOpacity = opacity;
    }

    private MindmapViewModel? _boundMindmapVm;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundMindmapVm != null)
        {
            _boundMindmapVm.RecenterRequested -= OnRecenterRequested;
            _boundMindmapVm.ExportRequested -= OnExportRequested;
            _boundMindmapVm.Nodes.CollectionChanged -= OnNodesCollectionChanged;
            _boundMindmapVm.PropertyChanged -= OnMindmapViewModelPropertyChanged;
            _boundMindmapVm = null;
        }
        if (DataContext is MindmapViewModel vm)
        {
            _boundMindmapVm = vm;
            vm.RecenterRequested += OnRecenterRequested;
            vm.ExportRequested += OnExportRequested;
            vm.Nodes.CollectionChanged += OnNodesCollectionChanged;
            vm.PropertyChanged += OnMindmapViewModelPropertyChanged;
            vm.ZoomLevel = GetScaleFromMatrix(_transformMatrix);
        }
    }

    private void OnMindmapViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MindmapViewModel.ZoomLevel) && DataContext is MindmapViewModel vm)
        {
            ApplyZoomFromVm(vm.ZoomLevel);
        }
        else if (e.PropertyName == nameof(MindmapViewModel.MinimapVisibilityMode))
        {
            UpdateMinimapVisibility();
        }
    }

    private void ApplyZoomFromVm(double scale)
    {
        scale = Math.Clamp(scale, MinScale, MaxScale);
        var mainCanvas = this.FindControl<Panel>("MainCanvas");
        if (mainCanvas == null || mainCanvas.Bounds.Width <= 0 || mainCanvas.Bounds.Height <= 0)
        {
            double tx = _transformMatrix.M31;
            double ty = _transformMatrix.M32;
            _transformMatrix = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(tx, ty);
        }
        else
        {
            var viewportCenter = new Point(mainCanvas.Bounds.Width / 2, mainCanvas.Bounds.Height / 2);
            var inverseMatrix = _transformMatrix.Invert();
            var contentPointAtCenter = viewportCenter * inverseMatrix;
            double tx = viewportCenter.X - contentPointAtCenter.X * scale;
            double ty = viewportCenter.Y - contentPointAtCenter.Y * scale;
            _transformMatrix = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(tx, ty);
        }
        UpdateTransform();
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

        // Auto-recenter after nodes are loaded, but not when restoring state (undo/redo) so node positions update without camera snap
        if (DataContext is MindmapViewModel vm && vm.Nodes.Any())
        {
            if (vm.SuppressRecenterOnNextCollectionChange)
            {
                vm.SuppressRecenterOnNextCollectionChange = false;
                UpdateMinimap();
            }
            else
            {
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await Task.Delay(50); // Allow layout to complete
                    RecenterView();
                    UpdateMinimap();
                });
            }
        }
    }

    private void OnNodePropertyChangedForMinimap(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NodeViewModel.X) or nameof(NodeViewModel.Y)
                            or nameof(NodeViewModel.Width) or nameof(NodeViewModel.Height)
                            or nameof(NodeViewModel.ActualWidth) or nameof(NodeViewModel.ActualHeight))
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

        double centerX = (nodes.Min(n => n.X) + nodes.Max(n => n.X + n.ActualWidth)) / 2;
        double centerY = (nodes.Min(n => n.Y) + nodes.Max(n => n.Y + n.ActualHeight)) / 2;

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

        if (DataContext is MindmapViewModel vm)
            vm.ZoomLevel = GetScaleFromMatrix(_transformMatrix);

        if (_isSelecting)
        {
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

        string mode = DataContext is MindmapViewModel vm ? vm.MinimapVisibilityMode : "Auto";
        bool byZoom = GetScaleFromMatrix(_transformMatrix) <= MinimapZoomThreshold;
        bool visible = mode switch
        {
            "Off" => false,
            "On" => true,
            _ => byZoom
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
        if (DataContext is MindmapViewModel vm && !vm.IsEditingEnabled)
        {
            doPan = true;
            doSelect = false;
        }

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
                if (DataContext is MindmapViewModel viewModel) viewModel.SelectedEdge = null;
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
        if (DataContext is MindmapViewModel editVm && !editVm.IsEditingEnabled)
            return;
        if (sender is Control control && control.DataContext is NodeViewModel node)
        {
            if (DataContext is MindmapViewModel vm)
            {
                vm.SelectedEdge = null; // Node selection context; edge panel greyed out
                // Multi-select with Ctrl, otherwise single select
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    foreach (var n in vm.Nodes) n.IsSelected = false;
                }
                node.IsSelected = true;
                _moveBeforeSnapshot = vm.CaptureMoveSnapshot();
            }

            _isDragging = true;
            _hasMovedNodeDuringDrag = false;
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

            if ((Math.Abs(dx) > 1e-6 || Math.Abs(dy) > 1e-6) && DataContext is MindmapViewModel vm)
            {
                _hasMovedNodeDuringDrag = true;
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
            if (_hasMovedNodeDuringDrag)
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
        else
        {
            UpdateEdgeHoverAndCursor(e);
        }
    }

    private void UpdateEdgeHoverAndCursor(PointerEventArgs e)
    {
        var mainCanvas = this.FindControl<Panel>("MainCanvas");
        if (mainCanvas == null || DataContext is not MindmapViewModel vm) return;
        var pos = e.GetPosition(mainCanvas);
        var contentPoint = pos * _transformMatrix.Invert();

        foreach (var node in vm.Nodes)
        {
            if (contentPoint.X >= node.X && contentPoint.X <= node.X + node.ActualWidth &&
                contentPoint.Y >= node.Y && contentPoint.Y <= node.Y + node.ActualHeight)
            {
                mainCanvas.Cursor = null;
                vm.SetHoveredEdge(null);
                return;
            }
        }

        long now = Environment.TickCount64;
        if (now - _lastEdgeHoverUpdateTicks < EdgeHoverThrottleMs)
            return;
        _lastEdgeHoverUpdateTicks = now;

        const double labelHalfW = 30;
        const double labelHalfH = 11;
        if (vm.IsEditingEnabled)
        {
            foreach (var edge in vm.Edges)
            {
                var d = edge.GetDistanceToCurve(contentPoint);
                if (d < EdgeHitThreshold)
                {
                    mainCanvas.Cursor = new Cursor(StandardCursorType.Hand);
                    vm.SetHoveredEdge(edge.Id);
                    return;
                }
                if (edge.Label != null)
                {
                    var cx = edge.CenterPoint.X;
                    var cy = edge.CenterPoint.Y;
                    if (contentPoint.X >= cx - labelHalfW && contentPoint.X <= cx + labelHalfW
                        && contentPoint.Y >= cy - labelHalfH && contentPoint.Y <= cy + labelHalfH)
                    {
                        mainCanvas.Cursor = new Cursor(StandardCursorType.Hand);
                        vm.SetHoveredEdge(edge.Id);
                        return;
                    }
                }
            }
        }

        mainCanvas.Cursor = null;
        vm.SetHoveredEdge(null);
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
            var nodeRect = new Rect(node.X, node.Y, node.ActualWidth, node.ActualHeight);
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
        try
        {
        if (_isDragging && _draggedNode != null && DataContext is MindmapViewModel vm)
        {
            if (_hasMovedNodeDuringDrag)
            {
                if (_moveBeforeSnapshot != null)
                {
                    var selectedNodes = vm.Nodes.Where(n => n.IsSelected).ToList();
                    var moves = selectedNodes.Contains(_draggedNode)
                        ? selectedNodes.Select(n => (n, n.X, n.Y)).ToList()
                        : new List<(NodeViewModel node, double x, double y)> { (_draggedNode, _draggedNode.X, _draggedNode.Y) };
                    await vm.UpdateNodesPositionAsync(_moveBeforeSnapshot, moves);
                }
                else
                {
                    var selectedNodes = vm.Nodes.Where(n => n.IsSelected).ToList();
                    if (selectedNodes.Contains(_draggedNode))
                    {
                        foreach (var node in selectedNodes)
                            await vm.UpdateNodePositionAsync(node, node.X, node.Y);
                    }
                    else
                        await vm.UpdateNodePositionAsync(_draggedNode, _draggedNode.X, _draggedNode.Y);
                }
            }
            _moveBeforeSnapshot = null;
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
            // If we're panning and didn't move significantly, it was a click on empty space
            if (!_hasMovedSignificantly && DataContext is MindmapViewModel viewModel)
            {
                foreach (var node in viewModel.Nodes) node.IsSelected = false;
                viewModel.SelectedEdge = null; // Clear edge selection; sidebar shows no selection
                if (!viewModel.IsEditingEnabled)
                {
                    _lastEdgeClickEdgeId = null;
                    _lastEdgeClickContentPoint = null;
                }
                else
                {
                    // Nodes are on top so edge hit Path never gets the click; hit-test edges in code
                    var mainCanvas = this.FindControl<Panel>("MainCanvas");
                    if (mainCanvas != null)
                    {
                        var pos = e.GetPosition(mainCanvas);
                        var contentPoint = pos * _transformMatrix.Invert();
                        EdgeViewModel? nearest = null;
                        double nearestDist = EdgeHitThreshold;
                        foreach (var edge in viewModel.Edges)
                        {
                            var d = edge.GetDistanceToCurve(contentPoint);
                            if (d < nearestDist) { nearestDist = d; nearest = edge; }
                        }
                        if (nearest != null)
                        {
                            var now = DateTime.UtcNow;
                            var prev = _lastEdgeClickContentPoint;
                            bool isDoubleClick = _lastEdgeClickEdgeId == nearest.Id
                                && prev is { } p
                                && (contentPoint.X - p.X) * (contentPoint.X - p.X) + (contentPoint.Y - p.Y) * (contentPoint.Y - p.Y) < EdgeDoubleClickDist * EdgeDoubleClickDist
                                && (now - _lastEdgeClickTime).TotalMilliseconds < EdgeDoubleClickMs;
                            if (isDoubleClick)
                            {
                                viewModel.EdgeClicked(nearest);
                                FocusEdgeLabelBox(nearest);
                                _lastEdgeClickEdgeId = null;
                                _lastEdgeClickContentPoint = null;
                            }
                            else
                            {
                                _lastEdgeClickTime = now;
                                _lastEdgeClickContentPoint = contentPoint;
                                _lastEdgeClickEdgeId = nearest.Id;
                            }
                        }
                        else
                        {
                            _lastEdgeClickEdgeId = null;
                            _lastEdgeClickContentPoint = null;
                        }
                    }
                }
            }
            _isPanning = false;
            e.Handled = true;
        }
        }
        catch (Exception ex)
        {
            var logger = (Application.Current as App)?.Services?.GetService(typeof(ILoggerService)) as ILoggerService;
            logger?.Error(nameof(MindmapView), "Error in pointer released handler", ex);
        }
    }

    private void FocusEdgeLabelBox(EdgeViewModel edge)
    {
        var layer = this.FindControl<ItemsControl>("EdgeHitLayer");
        if (layer == null) return;
        var container = layer.GetVisualDescendants().FirstOrDefault(v => v is Control c && c.DataContext == edge) as Control;
        if (container != null)
        {
            var box = container.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            if (box != null)
                Dispatcher.UIThread.Post(() =>
                {
                    box.Focus();
                    box.CaretIndex = box.Text?.Length ?? 0;
                }, DispatcherPriority.Loaded);
        }
    }

    private async void OnNodeTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is TextBox textBox && textBox.DataContext is NodeViewModel node && DataContext is MindmapViewModel vm)
            {
                await vm.UpdateNodeTextAsync(node, textBox.Text ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            var logger = (Application.Current as App)?.Services?.GetService(typeof(ILoggerService)) as ILoggerService;
            logger?.Error(nameof(MindmapView), "Failed to save node text", ex);
        }
    }

    private void OnEdgePointerEnter(object? sender, PointerEventArgs e)
    {
        if (sender is Control c && c.DataContext is EdgeViewModel edge && DataContext is MindmapViewModel vm)
            vm.SetHoveredEdge(edge.Id);
    }

    private void OnEdgePointerLeave(object? sender, PointerEventArgs e)
    {
        if (DataContext is MindmapViewModel vm)
            vm.SetHoveredEdge(null);
    }

    private void OnCanvasPointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is MindmapViewModel vm)
            vm.ClearHoverState();
    }

    private async void OnMindmapKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MindmapViewModel vm || !vm.IsEditingEnabled) return;

        if (e.Key == Key.Z && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            e.Handled = true;
            await vm.UndoAsync();
            return;
        }
        if (e.Key == Key.Y && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            e.Handled = true;
            await vm.RedoAsync();
            return;
        }

        if (vm.SelectedEdge == null) return;
        if (e.Key != Key.F2 && e.Key != Key.Enter) return;
        e.Handled = true;
        vm.EdgeClicked(vm.SelectedEdge);
        FocusEdgeLabelBox(vm.SelectedEdge);
    }

    private void OnNodePointerEnter(object? sender, PointerEventArgs e)
    {
        if (sender is Control c && c.DataContext is NodeViewModel node && DataContext is MindmapViewModel vm)
            vm.SetHoveredNode(node.Id, true);
    }

    private void OnNodePointerLeave(object? sender, PointerEventArgs e)
    {
        if (sender is Control c && c.DataContext is NodeViewModel node && DataContext is MindmapViewModel vm)
            vm.SetHoveredNode(node.Id, false);
    }

    private void OnEdgeHitPathPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control c || c.DataContext is not EdgeViewModel edge || DataContext is not MindmapViewModel vm)
            return;
        if (!vm.IsEditingEnabled) return;
        e.Handled = true; // Consume so canvas doesn't start pan; single-click selects, double-click edits
        if (e.ClickCount == 2)
        {
            vm.EdgeClicked(edge);
            FocusEdgeLabelBox(edge);
        }
        else if (e.ClickCount == 1)
            vm.SelectedEdge = edge;
    }

    private void OnEdgeLabelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not EdgeViewModel edge ||
            DataContext is not MindmapViewModel vm)
            return;
        if (!vm.IsEditingEnabled) return;
        e.Handled = true; // Prevents pan starting on label; single-click selects, double-click enters edit
        if (e.ClickCount == 2)
        {
            vm.EdgeClicked(edge);
            if (sender is TextBox box)
                Dispatcher.UIThread.Post(() =>
                {
                    box.Focus();
                    box.CaretIndex = box.Text?.Length ?? 0;
                }, DispatcherPriority.Loaded);
            else
                FocusEdgeLabelBox(edge);
        }
        else if (e.ClickCount == 1)
            vm.SelectedEdge = edge;
    }

    private void OnEdgeLabelLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control c || c.DataContext is not EdgeViewModel edge || DataContext is not MindmapViewModel vm)
            return;
        vm.CommitEdgeLabel(edge);
    }

    private void OnNodeSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not NodeViewModel node)
            return;
        HandleNodeSizeChanged(control, node, e.NewSize.Width, e.NewSize.Height);
    }

    private void HandleNodeSizeChanged(Control _, NodeViewModel node, double w, double h)
    {
        if (node.Shape == "circle")
        {
            double side = Math.Max(w, h);
            // Enforce equal width and height via the ViewModel so the binding keeps it square.
            // Guard against feedback: only write if the stored value differs meaningfully.
            if (Math.Abs((node.Width ?? 0) - side) > 0.5 || Math.Abs((node.Height ?? 0) - side) > 0.5)
            {
                node.Width = side;
                node.Height = side;
                // SizeChanged will fire again once the binding applies the new size.
                return;
            }
            w = side;
            h = side;
        }

        if (Math.Abs(node.ActualWidth - w) > 0.5 || Math.Abs(node.ActualHeight - h) > 0.5)
        {
            node.ActualWidth = w;
            node.ActualHeight = h;
        }
    }

    private void OnNodeTextBoxTextChanged(object? sender, TextChangedEventArgs e)
    {
        // When text changes on a circle node, release the fixed size so it can re-measure and grow,
        // then OnNodeSizeChanged will re-apply the square constraint at the new content size.
        if (sender is not Control textBox || textBox.DataContext is not NodeViewModel node || node.Shape != "circle")
            return;
        node.Width = null;
        node.Height = null;
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
            return;
        }

        minimapViewportBox.IsVisible = true;

        double minimapWidth = minimapPanel.Bounds.Width > 0 ? minimapPanel.Bounds.Width : _lastMinimapWidth;
        double minimapHeight = minimapPanel.Bounds.Height > 0 ? minimapPanel.Bounds.Height : _lastMinimapHeight;
        if (minimapWidth <= 0 || minimapHeight <= 0) return;

        _lastMinimapWidth = minimapWidth;
        _lastMinimapHeight = minimapHeight;

        // --- Fit all nodes into the minimap and center on them ---
        var nodes = vm.Nodes;
        double minX = nodes.Min(n => n.X);
        double maxX = nodes.Max(n => n.X + n.ActualWidth);
        double minY = nodes.Min(n => n.Y);
        double maxY = nodes.Max(n => n.Y + n.ActualHeight);
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


        const double viewportBoxScale = 1;
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

        // Stop any in-progress ease animation before starting a new one
        _easeTimer?.Stop();
        _easeTimer = null;

        var startMatrix = _transformMatrix;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _easeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        void Tick(object? s, EventArgs args)
        {
            double elapsed = sw.ElapsedMilliseconds / (double)MinimapEaseMs;
            if (elapsed >= 1)
            {
                _transformMatrix = targetMatrix;
                UpdateTransform();
                _easeTimer?.Stop();
                _easeTimer = null;
                return;
            }
            double invElapsed = 1 - elapsed;
            double eased = 1 - (invElapsed * invElapsed * invElapsed); // ease-out cubic
            double m31 = startMatrix.M31 + (targetMatrix.M31 - startMatrix.M31) * eased;
            double m32 = startMatrix.M32 + (targetMatrix.M32 - startMatrix.M32) * eased;
            _transformMatrix = new Matrix(scale, 0, 0, scale, m31, m32);
            UpdateTransform();
        }
        _easeTimer.Tick += Tick;
        _easeTimer.Start();
    }

    private const double ExportScale = 2.0; // PNG at 2× for sharpness

    private async void OnExportRequested(object? sender, EventArgs e)
    {
        try
        {
            if (DataContext is not MindmapViewModel vm || vm.Nodes.Count == 0) return;

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider == null) return;

            var selectedIds = vm.HasSelectedNodes
                ? vm.Nodes.Where(n => n.IsSelected).Select(n => n.Id).ToHashSet()
                : vm.Nodes.Select(n => n.Id).ToHashSet();
            var inScopeNodes = vm.Nodes.Where(n => selectedIds.Contains(n.Id)).ToList();
            if (inScopeNodes.Count == 0) return;

            string suggestedName = (vm.Title ?? "mindmap").Trim();
            if (string.IsNullOrEmpty(suggestedName)) suggestedName = "mindmap";
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                suggestedName = suggestedName.Replace(c, '_');

            var loc = (Application.Current as App)?.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
            var dialogTitle = loc?.T("ExportAsPngDialogTitle", "Mindmap") ?? "Export as PNG";

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = dialogTitle,
                SuggestedFileName = suggestedName + ".png",
                DefaultExtension = "png",
                FileTypeChoices = new[] { new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } } }
            }).ConfigureAwait(true);

            if (file == null) return;

            var selectedForCapture = vm.HasSelectedNodes ? selectedIds : null;
            var pngBytes = CaptureCurrentViewport(vm.ExportPngTransparentBackground, selectedForCapture);
            if (pngBytes == null || pngBytes.Length == 0)
            {
                var msgLoc = (Application.Current as App)?.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
                var msg = msgLoc?.T("ExportFailedNothingCaptured", "Mindmap") ?? "Nothing was captured.";
                ShowExportError(msg);
                return;
            }
            await using var stream = await file.OpenWriteAsync().ConfigureAwait(false);
            await stream.WriteAsync(pngBytes).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var logger = (Application.Current as App)?.Services?.GetService(typeof(ILoggerService)) as ILoggerService;
            logger?.Log(LogLevel.Warning, nameof(MindmapView), "PNG export failed", ex);
            ShowExportError(ex.Message);
        }
    }

    private void ShowExportError(string message)
    {
        var app = Application.Current as App;
        var overlay = app?.Services?.GetService(typeof(IOverlayService)) as IOverlayService;
        var loc = app?.Services?.GetService(typeof(ILocalizationService)) as ILocalizationService;
        var title = loc?.T("ExportFailedTitle", "Mindmap") ?? "Export failed";
        if (overlay != null)
            _ = overlay.CreateDialogAsync(title, message);
    }

    /// <summary>Resolves hex color or null to IBrush; null/empty uses theme resource fallback.</summary>
    private IBrush GetExportBrush(string? hex, string fallbackResourceKey)
    {
        if (!string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out var color))
            return new SolidColorBrush(color);
        return this.FindResource(fallbackResourceKey) as IBrush ?? Brushes.Gray;
    }

    private static AvaloniaList<double>? GetExportStrokeDashArray(string? type)
    {
        return type switch
        {
            EdgeTypes.Dashed => new AvaloniaList<double> { 6, 4 },
            EdgeTypes.Dotted => new AvaloniaList<double> { 2, 3 },
            _ => null
        };
    }

    /// <summary>Measures node label text with same font as export (ExportFontSize Medium) for sizing when Width/Height are null.</summary>
    private static (double Width, double Height) MeasureNodeText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return (0, 0);
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Medium),
            ExportFontSize,
            Brushes.Black);
        return (ft.Width, ft.Height);
    }

    private const double ExportSelectionPadding = 24;
    private const double ExportNodeWidthPadding = 24;
    private const double ExportNodeHeightPadding = 12;
    private const double ExportStrokeThickness = 1.5;
    private const double ExportFontSize = 14;

    /// <summary>Screen-capture the mindmap (grid + nodes + edges), or only the selected subgraph when selectedNodeIds is set.</summary>
    /// <param name="transparentBackground">When true, background is transparent (PNG alpha); otherwise uses workspace background.</param>
    /// <param name="selectedNodeIds">When non-null, only these nodes and edges between them are exported; image is sized to their bounding box.</param>
    private byte[]? CaptureCurrentViewport(bool transparentBackground, IReadOnlySet<string>? selectedNodeIds = null)
    {
        if (DataContext is not MindmapViewModel vm) return null;
        var contentOnly = this.FindControl<Panel>("MindmapContentOnly");
        if (contentOnly == null) return null;

        double viewportW = contentOnly.Bounds.Width > 0 ? contentOnly.Bounds.Width : _lastMainCanvasWidth;
        double viewportH = contentOnly.Bounds.Height > 0 ? contentOnly.Bounds.Height : _lastMainCanvasHeight;
        if (viewportW <= 0 || viewportH <= 0) return null;

        var matrix = _transformMatrix;
        var nodesInScope = selectedNodeIds != null
            ? vm.Nodes.Where(n => selectedNodeIds.Contains(n.Id)).ToList()
            : vm.Nodes.ToList();
        var edgesInScope = selectedNodeIds != null
            ? vm.Edges.Where(e => selectedNodeIds.Contains(e.From.Id) && selectedNodeIds.Contains(e.To.Id)).ToList()
            : vm.Edges.ToList();

        double w;
        double h;
        double offsetX;
        double offsetY;

        if (selectedNodeIds != null && nodesInScope.Count > 0)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var node in nodesInScope)
            {
                var (mw, mh) = MeasureNodeText(node.Text);
                double nw = node.Width ?? Math.Max(NodeViewModel.DefaultWidth, mw + ExportNodeWidthPadding);
                double nh = node.Height ?? Math.Max(NodeViewModel.DefaultHeight, mh + ExportNodeHeightPadding);
                var tl = matrix.Transform(new Point(node.X, node.Y));
                var br = matrix.Transform(new Point(node.X + nw, node.Y + nh));
                minX = Math.Min(minX, tl.X);
                minY = Math.Min(minY, tl.Y);
                maxX = Math.Max(maxX, br.X);
                maxY = Math.Max(maxY, br.Y);
            }
            foreach (var edge in edgesInScope)
            {
                var p1 = matrix.Transform(edge.StartPoint);
                var p4 = matrix.Transform(edge.EndPoint);
                minX = Math.Min(minX, Math.Min(p1.X, p4.X));
                minY = Math.Min(minY, Math.Min(p1.Y, p4.Y));
                maxX = Math.Max(maxX, Math.Max(p1.X, p4.X));
                maxY = Math.Max(maxY, Math.Max(p1.Y, p4.Y));
            }
            offsetX = -minX + ExportSelectionPadding;
            offsetY = -minY + ExportSelectionPadding;
            w = maxX - minX + 2 * ExportSelectionPadding;
            h = maxY - minY + 2 * ExportSelectionPadding;
            if (w <= 0 || h <= 0) return null;
        }
        else
        {
            offsetX = 0;
            offsetY = 0;
            w = viewportW;
            h = viewportH;
        }

        var bgBrush = transparentBackground
            ? Brushes.Transparent
            : (this.FindResource("WorkspaceBackgroundBrush") as IBrush ?? Brushes.White);
        var nodeBgBrush = this.FindResource("CardBackgroundSecondaryBrush") as IBrush ?? Brushes.White;
        var textBrush = this.FindResource("TextPrimaryBrush") as IBrush ?? Brushes.Black;

        var exportPanel = new Canvas
        {
            Width = w,
            Height = h,
            Background = bgBrush,
            IsHitTestVisible = false
        };

        Point ToPanel(Point screen) => new(screen.X + offsetX, screen.Y + offsetY);

        // Edges (under nodes)
        foreach (var edge in edgesInScope)
        {
            var strokeBrush = GetExportBrush(edge.Color, "MindmapEdgeStrokeBrush");
            var dashArray = GetExportStrokeDashArray(edge.Type);

            var p1 = ToPanel(matrix.Transform(edge.StartPoint));
            var p2 = ToPanel(matrix.Transform(edge.ControlPoint1));
            var p3 = ToPanel(matrix.Transform(edge.ControlPoint2));
            var p4 = ToPanel(matrix.Transform(edge.EndPoint));
            var figure = new PathFigure { StartPoint = p1, IsClosed = false };
            figure.Segments!.Add(new BezierSegment { Point1 = p2, Point2 = p3, Point3 = p4 });
            var pathGeometry = new PathGeometry();
            pathGeometry.Figures!.Add(figure);
            var path = new Avalonia.Controls.Shapes.Path
            {
                Data = pathGeometry,
                Stroke = strokeBrush,
                StrokeThickness = ExportStrokeThickness,
                StrokeDashArray = dashArray,
                IsHitTestVisible = false
            };
            exportPanel.Children.Add(path);

            if (edge.Type == EdgeTypes.Double)
            {
                var o1 = ToPanel(matrix.Transform(edge.OffsetStartPoint));
                var o2 = ToPanel(matrix.Transform(edge.OffsetControlPoint1));
                var o3 = ToPanel(matrix.Transform(edge.OffsetControlPoint2));
                var o4 = ToPanel(matrix.Transform(edge.OffsetEndPoint));
                var ofig = new PathFigure { StartPoint = o1, IsClosed = false };
                ofig.Segments!.Add(new BezierSegment { Point1 = o2, Point2 = o3, Point3 = o4 });
                var ogeom = new PathGeometry();
                ogeom.Figures!.Add(ofig);
                exportPanel.Children.Add(new Avalonia.Controls.Shapes.Path
                {
                    Data = ogeom,
                    Stroke = strokeBrush,
                    StrokeThickness = ExportStrokeThickness,
                    StrokeDashArray = dashArray,
                    IsHitTestVisible = false
                });
            }

            if (edge.Type == EdgeTypes.Bidirect && edge.ArrowStartPoints.Count >= 3)
            {
                var startPts = edge.ArrowStartPoints.Select(p => ToPanel(matrix.Transform(p))).ToList();
                exportPanel.Children.Add(new Avalonia.Controls.Shapes.Polygon
                {
                    Points = new Points(startPts),
                    Fill = strokeBrush,
                    IsHitTestVisible = false
                });
            }
            if ((edge.Type == EdgeTypes.Arrow || edge.Type == EdgeTypes.Bidirect) && edge.ArrowEndPoints.Count >= 3)
            {
                var endPts = edge.ArrowEndPoints.Select(p => ToPanel(matrix.Transform(p))).ToList();
                exportPanel.Children.Add(new Avalonia.Controls.Shapes.Polygon
                {
                    Points = new Points(endPts),
                    Fill = strokeBrush,
                    IsHitTestVisible = false
                });
            }
        }

        foreach (var node in nodesInScope)
        {
            var (mw, mh) = MeasureNodeText(node.Text);
            double nw = node.Width ?? Math.Max(NodeViewModel.DefaultWidth, mw + ExportNodeWidthPadding);
            double nh = node.Height ?? Math.Max(NodeViewModel.DefaultHeight, mh + ExportNodeHeightPadding);
            var topLeft = ToPanel(matrix.Transform(new Point(node.X, node.Y)));
            var bottomRight = ToPanel(matrix.Transform(new Point(node.X + nw, node.Y + nh)));
            double screenW = Math.Max(1, bottomRight.X - topLeft.X);
            double screenH = Math.Max(1, bottomRight.Y - topLeft.Y);
            var borderBrush = GetExportBrush(node.Color, "MindmapToolbarNodeColorSwatchOneBrush");
            var radius = node.CornerRadius;
            var border = new Border
            {
                [Canvas.LeftProperty] = topLeft.X,
                [Canvas.TopProperty] = topLeft.Y,
                Width = screenW,
                Height = screenH,
                Background = nodeBgBrush,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(ExportStrokeThickness),
                CornerRadius = new CornerRadius(radius > 100 ? screenH / 2 : Math.Min(radius, screenH / 2)),
                Padding = new Thickness(12, 6),
                Child = new TextBlock
                {
                    Text = node.Text ?? "",
                    Foreground = textBrush,
                    FontSize = ExportFontSize,
                    FontWeight = FontWeight.Medium,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap
                },
                IsHitTestVisible = false
            };
            exportPanel.Children.Add(border);
        }

        var mainCanvas = this.FindControl<Panel>("MainCanvas");
        if (mainCanvas == null) return null;

        try
        {
            mainCanvas.Children.Add(exportPanel);
            Canvas.SetLeft(exportPanel, -w - 100);
            Canvas.SetTop(exportPanel, -h - 100);
            exportPanel.Measure(new Size(w, h));
            exportPanel.Arrange(new Rect(0, 0, w, h));

            int pw = (int)Math.Ceiling(w * ExportScale);
            int ph = (int)Math.Ceiling(h * ExportScale);
            var dpi = new Vector(96 * ExportScale, 96 * ExportScale);
            using var bmp = new RenderTargetBitmap(new PixelSize(pw, ph), dpi);
            exportPanel.IsVisible = true;
            bmp.Render(exportPanel);

            using var mem = new MemoryStream();
            bmp.Save(mem);
            return mem.ToArray();
        }
        finally
        {
            mainCanvas.Children.Remove(exportPanel);
        }
    }
}
