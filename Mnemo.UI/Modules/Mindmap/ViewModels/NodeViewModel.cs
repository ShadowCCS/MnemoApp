using System.Collections.Generic;
using Mnemo.Core.Models.Mindmap;
using Mnemo.UI.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Mnemo.UI.Modules.Mindmap.ViewModels;

public partial class NodeViewModel : ViewModelBase
{
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

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private string? _color; // When null, view uses theme MindmapNodeBorderBrush

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
        _width = layout.Width;
        _height = layout.Height;
        _text = (node.Content as TextNodeContent)?.Text ?? "";
        _color = node.Style.TryGetValue("color", out var color) ? color : null;
        _shape = node.Style.TryGetValue("shape", out var shape) && !string.IsNullOrEmpty(shape) ? shape : "pill";
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Width) or nameof(Height))
                NotifyShapeDependent();
        };
    }

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
    partial void OnWidthChanged(double? value) => _layout.Width = value;
    partial void OnHeightChanged(double? value) => _layout.Height = value;
}
