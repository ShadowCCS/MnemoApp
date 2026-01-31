using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Mnemo.UI.Modules.Mindmap.ViewModels;

namespace Mnemo.UI.Modules.Mindmap.Views;

public partial class MindmapView : UserControl
{
    private const string MindmapGridColorKey = "MindmapGridColor";
    private double _gridSpacing = 40.0;
    private double _gridDotSize = 1.5;
    private double _gridOpacity = 0.2;
    private string _gridType = "Dotted";

    private const double DotScaleExponent = 0.35;
    private const double BaseDotScreenPixels = 3.5;
    private const double MinDotSourceSize = 0.5;
    private const double MaxDotSourceSize = 10.0;

    private bool _isDragging;
    private bool _isPanning;
    private Point _lastPointerPosition;
    private NodeViewModel? _draggedNode;
    private Matrix _transformMatrix = Matrix.Identity;
    private VisualBrush? _gridBrush;
    private ISettingsService? _settingsService;

    public MindmapView()
    {
        InitializeComponent();

        var canvas = this.FindControl<Panel>("MainCanvas");
        if (canvas != null)
        {
            canvas.PointerWheelChanged += OnCanvasPointerWheelChanged;
            canvas.SizeChanged += OnMainCanvasSizeChanged;
        }

        DataContextChanged += OnDataContextChanged;
        
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
    }

    private async Task LoadSettingsAsync()
    {
        if (_settingsService == null) return;
        
        _gridType = await _settingsService.GetAsync("Mindmap.GridType", "Dotted");
        var sizeStr = await _settingsService.GetAsync("Mindmap.GridSize", "40");
        var dotSizeStr = await _settingsService.GetAsync("Mindmap.GridDotSize", "1.5");
        var opacityStr = await _settingsService.GetAsync("Mindmap.GridOpacity", "0.2");

        if (double.TryParse(sizeStr, System.Globalization.CultureInfo.InvariantCulture, out var size)) _gridSpacing = size;
        if (double.TryParse(dotSizeStr, System.Globalization.CultureInfo.InvariantCulture, out var dotSize)) _gridDotSize = dotSize;
        if (double.TryParse(opacityStr, System.Globalization.CultureInfo.InvariantCulture, out var opacity)) _gridOpacity = opacity;
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
        // Auto-recenter after nodes are loaded, with a delay to allow layout
        if (DataContext is MindmapViewModel vm && vm.Nodes.Any())
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await Task.Delay(50); // Allow layout to complete
                RecenterView();
            });
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
            UpdateTransform();
        }
    }

    public void RecenterView()
    {
        if (DataContext is not MindmapViewModel vm || !vm.Nodes.Any()) return;

        var nodes = vm.Nodes;

        // Calculate bounding box of all nodes
        double minX = nodes.Min(n => n.X);
        double maxX = nodes.Max(n => n.X + (n.Width ?? 120));
        double minY = nodes.Min(n => n.Y);
        double maxY = nodes.Max(n => n.Y + (n.Height ?? 40));

        double centerX = (minX + maxX) / 2;
        double centerY = (minY + maxY) / 2;
        double width = maxX - minX;
        double height = maxY - minY;

        // Get viewport size
        var mainCanvas = this.FindControl<Panel>("MainCanvas");
        if (mainCanvas == null) return;

        double viewportWidth = mainCanvas.Bounds.Width;
        double viewportHeight = mainCanvas.Bounds.Height;

        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        // Calculate scale to fit with padding
        double padding = 50;
        double scaleX = (viewportWidth - padding * 2) / Math.Max(width, 1);
        double scaleY = (viewportHeight - padding * 2) / Math.Max(height, 1);
        double scale = Math.Min(scaleX, scaleY);
        scale = Math.Clamp(scale, MinScale, MaxScale);

        // Reset transform
        _transformMatrix = Matrix.Identity;

        // Center the content
        double offsetX = viewportWidth / 2 - centerX * scale;
        double offsetY = viewportHeight / 2 - centerY * scale;

        _transformMatrix = Matrix.CreateTranslation(offsetX, offsetY) * Matrix.CreateScale(scale, scale);
        UpdateTransform();
    }

    private const double MinScale = 0.1;
    private const double MaxScale = 5.0;

    private void OnCanvasPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var zoomDelta = e.Delta.Y > 0 ? 1.1 : 0.9;
        var pointerPos = e.GetPosition(this);

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
        var offset = pointerPos - screenPointAfter;
        _transformMatrix = Matrix.CreateTranslation(offset.X, offset.Y) * _transformMatrix;

        UpdateTransform();
        e.Handled = true;
    }

    private void UpdateTransform()
    {
        var canvas = this.FindControl<Panel>("TransformCanvas");
        if (canvas != null)
        {
            canvas.RenderTransform = new MatrixTransform(_transformMatrix);
        }

        UpdateGrid();
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
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // Only deselect if we didn't click a node (handled by e.Handled in OnNodePointerPressed)
            if (DataContext is MindmapViewModel vm)
            {
                foreach (var node in vm.Nodes) node.IsSelected = false;
            }
            
            _isPanning = true;
            _lastPointerPosition = e.GetPosition(this);
        }
    }

    private void OnNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && control.DataContext is NodeViewModel node)
        {
            if (DataContext is MindmapViewModel vm)
            {
                // Multi-select with Ctrl, otherwise single select
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
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
            double scale = Math.Sqrt(_transformMatrix.M11 * _transformMatrix.M11 + _transformMatrix.M12 * _transformMatrix.M12);
            
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
            e.Handled = true;
        }
        else if (_isPanning)
        {
            var currentPosition = e.GetPosition(this);
            var delta = currentPosition - _lastPointerPosition;
            
            _transformMatrix = _transformMatrix * Matrix.CreateTranslation(delta.X, delta.Y);
            UpdateTransform();
            
            _lastPointerPosition = currentPosition;
            e.Handled = true;
        }
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
        _isPanning = false;
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
        if (sender is Control control && control.DataContext is NodeViewModel node)
        {
            // Update node dimensions to match actual rendered size
            // This ensures edge calculations use the correct bounds
            node.Width = e.NewSize.Width;
            node.Height = e.NewSize.Height;
        }
    }
}
