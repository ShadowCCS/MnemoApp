using System;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

public class FractionBox : Box
{
    public Box Numerator { get; }
    public Box Denominator { get; }
    public double RuleThickness { get; }
    public double NumeratorSpacing { get; }
    public double DenominatorSpacing { get; }

    public FractionBox(Box numerator, Box denominator, double ruleThickness = 1.0, double numeratorSpacing = 2.0, double denominatorSpacing = 2.0)
    {
        Numerator = numerator;
        Denominator = denominator;
        RuleThickness = ruleThickness;
        NumeratorSpacing = numeratorSpacing;
        DenominatorSpacing = denominatorSpacing;

        Width = Math.Max(numerator.Width, denominator.Width) + 4;
        Height = numerator.TotalHeight + numeratorSpacing + RuleThickness / 2;
        Depth = denominator.TotalHeight + denominatorSpacing + RuleThickness / 2;
    }
}

