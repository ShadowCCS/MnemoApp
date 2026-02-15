using Mnemo.UI.Services.LaTeX.Rendering;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

public class SpaceBox : Box
{
    public SpaceBox(double width)
    {
        Width = width;
        Height = 0;
        Depth = 0;
    }

    public override void Render(IMathRenderContext ctx, double x, double baselineY) { }
}

