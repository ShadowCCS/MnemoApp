using Mnemo.UI.Services.LaTeX.Rendering;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

public class RuleBox : Box
{
    public RuleBox(double width, double thickness)
    {
        Width = width;
        Height = thickness / 2;
        Depth = thickness / 2;
    }

    public override void Render(IMathRenderContext ctx, double x, double baselineY)
    {
        var thickness = Height + Depth;
        ctx.DrawLine(x, baselineY, x + Width, baselineY, thickness);
    }
}