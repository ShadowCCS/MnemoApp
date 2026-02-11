using System;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

public class SqrtBox : Box
{
    public Box Content { get; }
    public double SymbolWidth { get; }
    public double RuleThickness { get; }
    public double Padding { get; }

    private const double VerticalBuffer = 2.0;
    
    public SqrtBox(Box content, double symbolWidth, double ruleThickness, double padding)
    {
        Content = content;
        SymbolWidth = symbolWidth;
        RuleThickness = ruleThickness;
        Padding = padding;
        
        Width = symbolWidth + content.Width + padding;
        
        // Height includes: content height + top padding + rule thickness + buffer
        Height = content.Height + padding + ruleThickness + VerticalBuffer;
        
        // Depth includes: content depth + buffer for the sqrt symbol bottom
        Depth = Math.Max(content.Depth, padding) + VerticalBuffer;
    }
}
