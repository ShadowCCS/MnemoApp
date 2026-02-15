using Avalonia.Media;
using Mnemo.UI.Services.LaTeX.Layout.Boxes;

namespace Mnemo.UI.Services.LaTeX.Rendering;

/// <summary>
/// Contract for rendering the math box tree. Coordinates use Avalonia convention:
/// baselineY is the Y coordinate (Y increases downward) where the box's baseline is drawn.
/// </summary>
public interface IMathRenderContext
{
    void RenderChild(Box box, double x, double baselineY);
    void DrawText(string character, double fontSize, double x, double baselineY);
    void DrawLine(double x1, double y1, double x2, double y2, double thickness);
    void DrawPath(PathGeometry path, double thickness);
    IBrush GetBrush();
}
