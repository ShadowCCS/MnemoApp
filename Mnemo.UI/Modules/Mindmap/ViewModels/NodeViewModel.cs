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
