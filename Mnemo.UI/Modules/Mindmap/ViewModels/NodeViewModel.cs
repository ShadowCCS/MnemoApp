using System.Collections.Generic;
using Mnemo.Core.Models.Mindmap;
using Mnemo.UI.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Mnemo.UI.Modules.Mindmap.ViewModels;

public partial class NodeViewModel : ViewModelBase
{
    /// <summary>Max outer width when the node auto-sizes from text (word wrap).</summary>
    public const double DefaultMaxOuterWidth = 280;

    /// <summary>Fallback width used when actual layout width is not yet known.</summary>
    public const double DefaultWidth = 120;

    /// <summary>Fallback height used when actual layout height is not yet known.</summary>
    public const double DefaultHeight = 40;

    private readonly MindmapNode _node;
    private readonly NodeLayout _layout;

    [ObservableProperty]
    private string _id;

    [ObservableProperty]
    private string _nodeType;

    [ObservableProperty]
    private IMindmapNodeContent _content;

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private double? _width;

    [ObservableProperty]
    private double? _height;

    /// <summary>Last measured rendered width (updated from SizeChanged in view). Used for edge attach points and minimap; never persisted.</summary>
    [ObservableProperty]
    private double _actualWidth = DefaultWidth;

    /// <summary>Last measured rendered height (updated from SizeChanged in view). Used for edge attach points and minimap; never persisted.</summary>
    [ObservableProperty]
    private double _actualHeight = DefaultHeight;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>When true, this node's descendant subtree is hidden. Persisted in Style["collapsed"].</summary>
    [ObservableProperty]
    private bool _isCollapsed;

    /// <summary>Set by the ViewModel when a collapsed ancestor hides this node. Not persisted.</summary>
    [ObservableProperty]
    private bool _isHidden;

    /// <summary>True when this node has at least one outgoing hierarchy edge. Set by the ViewModel after graph build. Not persisted.</summary>
    [ObservableProperty]
    private bool _hasChildren;

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private string? _color; // When null, view uses MindmapToolbarNodeColorSwatchOneBrush (default gray)

    /// <summary>One of: rectangle, pill, circle. Default pill.</summary>
    [ObservableProperty]
    private string _shape = "pill";

    public NodeViewModel(MindmapNode node, NodeLayout layout)
    {
        _node = node;
        _layout = layout;
        _id = node.Id;
        _nodeType = node.NodeType;
        _content = node.Content;
        _x = layout.X;
        _y = layout.Y;
        // Only restore a saved size for circle nodes (where width == height enforces the square).
        // Non-circle shapes auto-size from content, so persisted sizes are discarded on load
        // to avoid pinning the ContentControl at a stale size that won't grow with text.
        bool isCircle = (node.Style.TryGetValue("shape", out var shapeForSize) && shapeForSize == "circle");
        _width = isCircle ? layout.Width : null;
        _height = isCircle ? layout.Height : null;
        _actualWidth = layout.Width ?? DefaultWidth;
        _actualHeight = layout.Height ?? DefaultHeight;
        _text = (node.Content as TextNodeContent)?.Text ?? "";
        _color = node.Style.TryGetValue("color", out var color) ? color : null;
        _shape = node.Style.TryGetValue("shape", out var shape) && !string.IsNullOrEmpty(shape) ? shape : "pill";
        _isCollapsed = node.Style.TryGetValue("collapsed", out var collapsed) && collapsed == "true";
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Width) or nameof(Height) or nameof(ActualWidth) or nameof(ActualHeight))
                NotifyShapeDependent();
        };
    }

    /// <summary>Outer max width: fixed <see cref="Width"/> when set, otherwise <see cref="DefaultMaxOuterWidth"/> for wrap.</summary>
    public double NodeMaxOuterWidth => Width ?? DefaultMaxOuterWidth;

    /// <summary>Corner radius for the node border. Rectangle=0, pill/circle=999.</summary>
    public double CornerRadius => Shape switch { "rectangle" => 0, "circle" => 999, "pill" => 999, _ => 20 };

    /// <summary>When shape is circle, fixed min so the node can shrink with content; otherwise 0.</summary>
    public double EffectiveMinSize => Shape == "circle" ? 40 : 0;

    partial void OnShapeChanged(string value) => NotifyShapeDependent();

    private void NotifyShapeDependent()
    {
        OnPropertyChanged(nameof(CornerRadius));
        OnPropertyChanged(nameof(EffectiveMinSize));
    }

    partial void OnTextChanged(string value)
    {
        if (_node.Content is TextNodeContent textContent)
        {
            textContent.Text = value;
        }
    }

    partial void OnXChanged(double value) => _layout.X = value;
    partial void OnYChanged(double value) => _layout.Y = value;
    partial void OnWidthChanged(double? value)
    {
        _layout.Width = value;
        OnPropertyChanged(nameof(NodeMaxOuterWidth));
    }
    partial void OnHeightChanged(double? value) => _layout.Height = value;

    partial void OnIsCollapsedChanged(bool value)
    {
        if (value)
            _node.Style["collapsed"] = "true";
        else
            _node.Style.Remove("collapsed");
    }
}
