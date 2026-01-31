using System;
using Avalonia;
using Mnemo.Core.Models.Mindmap;
using Mnemo.UI.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Mnemo.UI.Modules.Mindmap.ViewModels;

public partial class EdgeViewModel : ViewModelBase, IDisposable
{
    private readonly MindmapEdge _edge;
    private readonly NodeViewModel _from;
    private readonly NodeViewModel _to;

    [ObservableProperty]
    private string _id;

    [ObservableProperty]
    private MindmapEdgeKind _kind;

    [ObservableProperty]
    private string? _label;

    [ObservableProperty]
    private string? _color; // When null, view uses theme MindmapEdgeStrokeBrush

    [ObservableProperty]
    private Point _startPoint;

    [ObservableProperty]
    private Point _endPoint;

    [ObservableProperty]
    private Point _controlPoint1;

    [ObservableProperty]
    private Point _controlPoint2;

    public NodeViewModel From => _from;
    public NodeViewModel To => _to;

    public EdgeViewModel(MindmapEdge edge, NodeViewModel from, NodeViewModel to)
    {
        _edge = edge;
        _from = from;
        _to = to;
        _id = edge.Id;
        _kind = edge.Kind;
        _label = edge.Label;
        _color = to.Color; // null when node has no style color → theme fallback in view

        // Subscribe to position changes on both nodes
        _from.PropertyChanged += OnNodePropertyChanged;
        _to.PropertyChanged += OnNodePropertyChanged;

        UpdatePoints();
    }

    private void OnNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodeViewModel.X) || 
            e.PropertyName == nameof(NodeViewModel.Y) ||
            e.PropertyName == nameof(NodeViewModel.Width) ||
            e.PropertyName == nameof(NodeViewModel.Height))
        {
            UpdatePoints();
        }
    }

    public void Dispose()
    {
        _from.PropertyChanged -= OnNodePropertyChanged;
        _to.PropertyChanged -= OnNodePropertyChanged;
        GC.SuppressFinalize(this);
    }

    private void UpdatePoints()
    {
        double fromWidth = _from.Width ?? 120;
        double fromHeight = _from.Height ?? 40;
        double toWidth = _to.Width ?? 120;
        double toHeight = _to.Height ?? 40;

        // For hierarchy, we usually go from right side of parent to left side of child
        // if the child is to the right. 
        bool isToRight = _to.X > _from.X + fromWidth / 2;

        if (Kind == MindmapEdgeKind.Hierarchy)
        {
            if (isToRight)
            {
                StartPoint = new Point(_from.X + fromWidth, _from.Y + fromHeight / 2);
                EndPoint = new Point(_to.X, _to.Y + toHeight / 2);
            }
            else
            {
                StartPoint = new Point(_from.X, _from.Y + fromHeight / 2);
                EndPoint = new Point(_to.X + toWidth, _to.Y + toHeight / 2);
            }

            double dx = Math.Abs(EndPoint.X - StartPoint.X);
            double offset = Math.Min(dx / 2, 150);

            if (isToRight)
            {
                ControlPoint1 = new Point(StartPoint.X + offset, StartPoint.Y);
                ControlPoint2 = new Point(EndPoint.X - offset, EndPoint.Y);
            }
            else
            {
                ControlPoint1 = new Point(StartPoint.X - offset, StartPoint.Y);
                ControlPoint2 = new Point(EndPoint.X + offset, EndPoint.Y);
            }
        }
        else
        {
            // For general links, use centers for now
            StartPoint = new Point(_from.X + fromWidth / 2, _from.Y + fromHeight / 2);
            EndPoint = new Point(_to.X + toWidth / 2, _to.Y + toHeight / 2);
            ControlPoint1 = StartPoint;
            ControlPoint2 = EndPoint;
        }
    }
}
