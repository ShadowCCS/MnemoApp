using System;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

public class FractionBox : Box
{
    public Box Numerator { get; }
    public Box Denominator { get; }
    public double RuleThickness { get; }
    public double NumeratorSpacing { get; }
    public double DenominatorSpacing { get; }

    private const double HorizontalPadding = 4.0;
    private const double VerticalBuffer = 2.0;

    public FractionBox(Box numerator, Box denominator, double ruleThickness = 1.0, double numeratorSpacing = 2.0, double denominatorSpacing = 2.0)
    {
        Numerator = numerator;
        Denominator = denominator;
        RuleThickness = ruleThickness;
        NumeratorSpacing = numeratorSpacing;
        DenominatorSpacing = denominatorSpacing;

        Width = Math.Max(numerator.Width, denominator.Width) + HorizontalPadding;
        
        // Height above baseline: rule center + spacing + numerator total height
        Height = RuleThickness / 2 + NumeratorSpacing + numerator.TotalHeight + VerticalBuffer;
        
        // Depth below baseline: rule center + spacing + denominator total height
        Depth = RuleThickness / 2 + DenominatorSpacing + denominator.TotalHeight + VerticalBuffer;
    }
}