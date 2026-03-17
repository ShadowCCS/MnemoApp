using System;
using Avalonia;
using Avalonia.Collections;
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
    private string _type = EdgeTypes.Solid;

    [ObservableProperty]
    private string? _label;

    [ObservableProperty]
    private string? _color; // When null, view uses theme MindmapEdgeStrokeBrush

    [ObservableProperty]
    private Point _startPoint;

    [ObservableProperty]
    private Point _endPoint;

    /// <summary>
    /// Actual rendered endpoints of the main line. These may be pulled back
    /// from the node boundary when an arrow head is present so that the tip
    /// of the arrow remains thin and is not drawn on top of the line.
    /// </summary>
    [ObservableProperty]
    private Point _visualStartPoint;

    [ObservableProperty]
    private Point _visualEndPoint;

    [ObservableProperty]
    private Point _controlPoint1;

    [ObservableProperty]
    private Point _controlPoint2;

    [ObservableProperty]
    private Point _centerPoint;

    /// <summary>Second line points for Type=double (parallel offset).</summary>
    [ObservableProperty]
    private Point _offsetStartPoint;

    [ObservableProperty]
    private Point _offsetEndPoint;

    /// <summary>Rendered endpoints for the second line (double edges).</summary>
    [ObservableProperty]
    private Point _visualOffsetStartPoint;

    [ObservableProperty]
    private Point _visualOffsetEndPoint;

    [ObservableProperty]
    private Point _offsetControlPoint1;

    [ObservableProperty]
    private Point _offsetControlPoint2;

    /// <summary>Arrow head triangles for Type=bidirect (3 points each: left, tip, right).</summary>
    [ObservableProperty]
    private AvaloniaList<Point> _arrowStartPoints = [];

    [ObservableProperty]
    private AvaloniaList<Point> _arrowEndPoints = [];

    /// <summary>View state: true when this edge or its endpoints are hovered (label at full opacity).</summary>
    [ObservableProperty]
    private bool _isLabelHighlighted = false;

    /// <summary>Set by the ViewModel when either endpoint node is hidden due to a collapsed ancestor. Not persisted.</summary>
    [ObservableProperty]
    private bool _isHidden;

    public NodeViewModel From => _from;
    public NodeViewModel To => _to;

    public EdgeViewModel(MindmapEdge edge, NodeViewModel from, NodeViewModel to)
    {
        _edge = edge;
        _from = from;
        _to = to;
        _id = edge.Id;
        _kind = edge.Kind;
        _type = string.IsNullOrEmpty(edge.Type) ? EdgeTypes.Solid : edge.Type;
        _label = edge.Label;
        _color = to.Color; // null when node has no style color → theme fallback in view

        // Subscribe to position changes on both nodes
        _from.PropertyChanged += OnNodePropertyChanged;
        _to.PropertyChanged += OnNodePropertyChanged;

        UpdatePoints();
    }

    partial void OnTypeChanged(string value)
    {
        _edge.Type = value;
        // Geometry (double offset line, arrow heads, shortened endpoints) depends on Type;
        // without this, only dash style updates when switching solid→arrow etc.
        UpdatePoints();
    }

    private void OnNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodeViewModel.X) ||
            e.PropertyName == nameof(NodeViewModel.Y) ||
            e.PropertyName == nameof(NodeViewModel.Width) ||
            e.PropertyName == nameof(NodeViewModel.Height) ||
            e.PropertyName == nameof(NodeViewModel.ActualWidth) ||
            e.PropertyName == nameof(NodeViewModel.ActualHeight))
        {
            UpdatePoints();
        }
        else if (e.PropertyName == nameof(NodeViewModel.Color) && sender == _to)
        {
            Color = _to.Color;
        }
    }

    public void Dispose()
    {
        _from.PropertyChanged -= OnNodePropertyChanged;
        _to.PropertyChanged -= OnNodePropertyChanged;
        GC.SuppressFinalize(this);
    }

    private const double BezierPullFraction = 0.4;
    private const double BezierPullMin = 30;
    private const double BezierPullMax = 200;
    private const double DoubleLineOffset = 2.0;
    private const double ArrowLength = 10;
    private const double ArrowHalfWidth = 4;
    private const int BezierSampleSteps = 32;

    private void UpdatePoints()
    {
        double fromWidth  = _from.ActualWidth;
        double fromHeight = _from.ActualHeight;
        double toWidth    = _to.ActualWidth;
        double toHeight   = _to.ActualHeight;

        double fromCx = _from.X + fromWidth  / 2;
        double fromCy = _from.Y + fromHeight / 2;
        double toCx   = _to.X   + toWidth    / 2;
        double toCy   = _to.Y   + toHeight   / 2;
        double dx = toCx - fromCx;
        double dy = toCy - fromCy;

        StartPoint = GetAttachPoint(_from.X, _from.Y, fromWidth,  fromHeight,  dx,  dy);
        EndPoint   = GetAttachPoint(_to.X,   _to.Y,   toWidth,    toHeight,   -dx, -dy);

        // Defaults for rendered endpoints: full segment between node attach points.
        VisualStartPoint       = StartPoint;
        VisualEndPoint         = EndPoint;
        VisualOffsetStartPoint = StartPoint;
        VisualOffsetEndPoint   = EndPoint;

        if (Kind == MindmapEdgeKind.Hierarchy)
        {
            // Tangent pull distance: proportional to segment length, clamped.
            double segDx   = EndPoint.X - StartPoint.X;
            double segDy   = EndPoint.Y - StartPoint.Y;
            double dist    = Math.Sqrt(segDx * segDx + segDy * segDy);
            double pull    = Math.Clamp(dist * BezierPullFraction, BezierPullMin, BezierPullMax);

            // Exit direction = the side the start-attach point sits on.
            Point exitDir  = GetAttachNormal(_from.X, _from.Y, fromWidth, fromHeight, dx, dy);
            // Entry direction = opposite of the side the end-attach point sits on.
            Point entryDir = GetAttachNormal(_to.X, _to.Y, toWidth, toHeight, -dx, -dy);

            ControlPoint1 = new Point(
                StartPoint.X + exitDir.X  * pull,
                StartPoint.Y + exitDir.Y  * pull);

            ControlPoint2 = new Point(
                EndPoint.X   + entryDir.X * pull,
                EndPoint.Y   + entryDir.Y * pull);
        }
        else
        {
            ControlPoint1 = StartPoint;
            ControlPoint2 = EndPoint;
        }

        // Cubic Bezier at t=0.5 for label placement (always needed for label hit-test and display)
        double t = 0.5, u = 1 - t;
        double cx = u * u * u * StartPoint.X + 3 * u * u * t * ControlPoint1.X + 3 * u * t * t * ControlPoint2.X + t * t * t * EndPoint.X;
        double cy = u * u * u * StartPoint.Y + 3 * u * u * t * ControlPoint1.Y + 3 * u * t * t * ControlPoint2.Y + t * t * t * EndPoint.Y;
        CenterPoint = new Point(cx, cy);

        if (Type == EdgeTypes.Double)
        {
            double tx = 3 * u * u * (ControlPoint1.X - StartPoint.X) + 6 * u * t * (ControlPoint2.X - ControlPoint1.X) + 3 * t * t * (EndPoint.X - ControlPoint2.X);
            double ty = 3 * u * u * (ControlPoint1.Y - StartPoint.Y) + 6 * u * t * (ControlPoint2.Y - ControlPoint1.Y) + 3 * t * t * (EndPoint.Y - ControlPoint2.Y);
            double len = Math.Sqrt(tx * tx + ty * ty);
            if (len < 1e-6) len = 1;
            double nx = -ty / len, ny = tx / len;
            OffsetStartPoint = new Point(StartPoint.X + nx * DoubleLineOffset, StartPoint.Y + ny * DoubleLineOffset);
            OffsetEndPoint   = new Point(EndPoint.X + nx * DoubleLineOffset, EndPoint.Y + ny * DoubleLineOffset);
            OffsetControlPoint1 = new Point(ControlPoint1.X + nx * DoubleLineOffset, ControlPoint1.Y + ny * DoubleLineOffset);
            OffsetControlPoint2 = new Point(ControlPoint2.X + nx * DoubleLineOffset, ControlPoint2.Y + ny * DoubleLineOffset);

            VisualOffsetStartPoint = OffsetStartPoint;
            VisualOffsetEndPoint   = OffsetEndPoint;
        }
        else
        {
            OffsetStartPoint = StartPoint;
            OffsetEndPoint = EndPoint;
            OffsetControlPoint1 = ControlPoint1;
            OffsetControlPoint2 = ControlPoint2;
        }

        if (Type == EdgeTypes.Arrow || Type == EdgeTypes.Bidirect)
        {
            double ex = EndPoint.X - ControlPoint2.X, ey = EndPoint.Y - ControlPoint2.Y;
            double elen = Math.Sqrt(ex * ex + ey * ey);
            if (elen < 1e-6) { ex = 1; ey = 0; elen = 1; }
            ex /= elen; ey /= elen;
            Point endBase = new Point(EndPoint.X - ex * ArrowLength, EndPoint.Y - ey * ArrowLength);
            double px = -ey, py = ex;
            ArrowEndPoints = new AvaloniaList<Point>
            {
                new Point(endBase.X + px * ArrowHalfWidth, endBase.Y + py * ArrowHalfWidth),
                EndPoint,
                new Point(endBase.X - px * ArrowHalfWidth, endBase.Y - py * ArrowHalfWidth)
            };

            // Shorten rendered main line so it ends at the base of the arrow head.
            VisualEndPoint = endBase;

            // And adjust the offset (double) line similarly when present.
            double oex = OffsetEndPoint.X - OffsetControlPoint2.X;
            double oey = OffsetEndPoint.Y - OffsetControlPoint2.Y;
            double oelen = Math.Sqrt(oex * oex + oey * oey);
            if (oelen < 1e-6) { oex = ex; oey = ey; oelen = 1; }
            oex /= oelen; oey /= oelen;
            Point offsetEndBase = new Point(OffsetEndPoint.X - oex * ArrowLength, OffsetEndPoint.Y - oey * ArrowLength);
            VisualOffsetEndPoint = offsetEndBase;

            if (Type == EdgeTypes.Bidirect)
            {
                // For the start arrow we want the tip pointing back toward the
                // "from" node, so the triangle tip is at StartPoint and the
                // base is pulled inwards along the curve direction.
                double sx = StartPoint.X - ControlPoint1.X, sy = StartPoint.Y - ControlPoint1.Y;
                double slen = Math.Sqrt(sx * sx + sy * sy);
                if (slen < 1e-6) { sx = 1; sy = 0; slen = 1; }
                sx /= slen; sy /= slen;
                Point startBase = new Point(StartPoint.X - sx * ArrowLength, StartPoint.Y - sy * ArrowLength);
                double qx = -sy, qy = sx;
                ArrowStartPoints = new AvaloniaList<Point>
                {
                    new Point(startBase.X + qx * ArrowHalfWidth, startBase.Y + qy * ArrowHalfWidth),
                    StartPoint,
                    new Point(startBase.X - qx * ArrowHalfWidth, startBase.Y - qy * ArrowHalfWidth)
                };

                // Shorten the rendered start of the line so it meets the
                // base of the arrow head instead of running underneath it.
                VisualStartPoint = startBase;

                double osx = OffsetStartPoint.X - OffsetControlPoint1.X;
                double osy = OffsetStartPoint.Y - OffsetControlPoint1.Y;
                double oslen = Math.Sqrt(osx * osx + osy * osy);
                if (oslen < 1e-6) { osx = sx; osy = sy; oslen = 1; }
                osx /= oslen; osy /= oslen;
                Point offsetStartBase = new Point(OffsetStartPoint.X - osx * ArrowLength, OffsetStartPoint.Y - osy * ArrowLength);
                VisualOffsetStartPoint = offsetStartBase;
            }
            else
                ArrowStartPoints = [];
        }
        else
        {
            ArrowStartPoints = [];
            ArrowEndPoints = [];
        }
    }

    /// <summary>Unit outward normal for the attach side. Same dominant-direction logic as GetAttachPoint.</summary>
    private static Point GetAttachNormal(double x, double y, double w, double h, double dx, double dy)
    {
        bool horizontal = Math.Abs(dx) >= Math.Abs(dy);
        if (horizontal) return dx >= 0 ? new Point(1, 0) : new Point(-1, 0);
        return dy >= 0 ? new Point(0, 1) : new Point(0, -1);
    }

    /// <summary>Get attach point on node boundary: top, bottom, left, or right center. (dx,dy) = direction toward the other node.</summary>
    private static Point GetAttachPoint(double x, double y, double w, double h, double dx, double dy)
    {
        bool horizontal = Math.Abs(dx) >= Math.Abs(dy);
        if (horizontal)
        {
            if (dx >= 0) return new Point(x + w, y + h / 2); // right
            return new Point(x, y + h / 2); // left
        }
        if (dy >= 0) return new Point(x + w / 2, y + h); // bottom
        return new Point(x + w / 2, y); // top
    }

    /// <summary>Minimum distance from point to the cubic bezier (approximate by sampling).</summary>
    public double GetDistanceToCurve(Point p)
    {
        const int steps = BezierSampleSteps;
        double min = double.MaxValue;
        double px = p.X, py = p.Y;
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            double u = 1 - t;
            double x = u * u * u * StartPoint.X + 3 * u * u * t * ControlPoint1.X + 3 * u * t * t * ControlPoint2.X + t * t * t * EndPoint.X;
            double y = u * u * u * StartPoint.Y + 3 * u * u * t * ControlPoint1.Y + 3 * u * t * t * ControlPoint2.Y + t * t * t * EndPoint.Y;
            double dx = px - x, dy = py - y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < min) min = d;
        }
        return min;
    }
}
