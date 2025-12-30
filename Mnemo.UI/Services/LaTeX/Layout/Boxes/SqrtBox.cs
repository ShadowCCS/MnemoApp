namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

public class SqrtBox : Box
{
    public Box Content { get; }
    public double SymbolWidth { get; }
    public double RuleThickness { get; }
    public double Padding { get; }
    
    public SqrtBox(Box content, double symbolWidth, double ruleThickness, double padding)
    {
        Content = content;
        SymbolWidth = symbolWidth;
        RuleThickness = ruleThickness;
        Padding = padding;
        
        Width = symbolWidth + content.Width + padding;
        Height = content.Height + ruleThickness + padding;
        Depth = content.Depth;
    }
}
