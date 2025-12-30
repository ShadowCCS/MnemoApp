namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

public class RuleBox : Box
{
    public RuleBox(double width, double thickness)
    {
        Width = width;
        Height = thickness / 2;
        Depth = thickness / 2;
    }
}

