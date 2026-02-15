using System.Collections.Generic;
using Mnemo.UI.Services.LaTeX.Rendering;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

/// <summary>
/// Base for all math layout boxes. Coordinates: baselineY is the Avalonia Y (down) where this box's baseline is drawn.
/// </summary>
public abstract class Box
{
    public double Width { get; set; }
    public double Height { get; set; }
    public double Depth { get; set; }
    public double Shift { get; set; }

    public double TotalHeight => Height + Depth;

    /// <summary>Draw this box so its baseline is at (x, baselineY). Y increases downward.</summary>
    public abstract void Render(IMathRenderContext ctx, double x, double baselineY);

    /// <summary>Enumerates child boxes and their render positions for traversal (e.g. bounds calculation). Base returns empty.</summary>
    public virtual IReadOnlyList<(Box child, double x, double baselineY)> GetChildPositions(double x, double baselineY) =>
        [];
}