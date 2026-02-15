using System;
using System.Collections.Generic;
using Mnemo.UI.Services.LaTeX.Rendering;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

/// <summary>
/// Represents a mathematical fraction with numerator, denominator, and horizontal rule.
/// The box's baseline is the text baseline (for alignment with operators like +). The fraction rule
/// is drawn at the math axis height (e.g. from font metrics) above the baseline.
/// </summary>
public class FractionBox : Box
{
    public Box Numerator { get; }
    public Box Denominator { get; }
    public double RuleThickness { get; }
    public double NumeratorSpacing { get; }
    public double DenominatorSpacing { get; }
    /// <summary>Distance from text baseline to math axis (rule height). Y-down: rule is at baselineY - MathAxisHeight.</summary>
    public double MathAxisHeight { get; }

    private const double MinHorizontalClearance = 4.0;
    private const double ClearanceRuleThicknessMultiplier = 3.0;

    /// <summary>Additional spacing above numerator and below denominator for visual breathing room.</summary>
    private const double VerticalPadding = 2.0;

    /// <summary>Clearance on each side of the fraction rule so the bar extends past the numerator/denominator.</summary>
    private double HorizontalClearance => Math.Max(MinHorizontalClearance, ClearanceRuleThicknessMultiplier * RuleThickness);

    public FractionBox(Box numerator, Box denominator, double ruleThickness = 1.0, double numeratorSpacing = 2.0, double denominatorSpacing = 2.0, double mathAxisHeight = 0.0)
    {
        Numerator = numerator ?? throw new ArgumentNullException(nameof(numerator));
        Denominator = denominator ?? throw new ArgumentNullException(nameof(denominator));

        if (ruleThickness < 0) throw new ArgumentOutOfRangeException(nameof(ruleThickness));
        if (numeratorSpacing < 0) throw new ArgumentOutOfRangeException(nameof(numeratorSpacing));
        if (denominatorSpacing < 0) throw new ArgumentOutOfRangeException(nameof(denominatorSpacing));

        RuleThickness = ruleThickness;
        NumeratorSpacing = numeratorSpacing;
        DenominatorSpacing = denominatorSpacing;
        MathAxisHeight = mathAxisHeight;

        var contentWidth = Math.Max(numerator.Width, denominator.Width);
        Width = contentWidth + 2.0 * HorizontalClearance;

        var heightAboveRule = RuleThickness / 2 + NumeratorSpacing + numerator.TotalHeight + VerticalPadding;
        var depthBelowRule = RuleThickness / 2 + DenominatorSpacing + denominator.TotalHeight + VerticalPadding;
        Height = mathAxisHeight + heightAboveRule;
        Depth = Math.Max(0.0, depthBelowRule - mathAxisHeight);
    }

    private (double numX, double numBaselineY, double denomX, double denomBaselineY)
        CalculateChildPositions(double x, double baselineY)
    {
        var ruleCenterY = baselineY - MathAxisHeight;

        var numBaselineY = ruleCenterY - (RuleThickness / 2) - NumeratorSpacing - Numerator.Depth;
        var numX = x + (Width - Numerator.Width) / 2;

        var denomBaselineY = ruleCenterY + (RuleThickness / 2) + DenominatorSpacing + Denominator.Height;
        var denomX = x + (Width - Denominator.Width) / 2;

        return (numX, numBaselineY, denomX, denomBaselineY);
    }

    public override IReadOnlyList<(Box child, double x, double baselineY)> GetChildPositions(double x, double baselineY)
    {
        var (numX, numBaselineY, denomX, denomBaselineY) = CalculateChildPositions(x, baselineY);
        return [(Numerator, numX, numBaselineY), (Denominator, denomX, denomBaselineY)];
    }

    public override void Render(IMathRenderContext ctx, double x, double baselineY)
    {
        var (numX, numBaselineY, denomX, denomBaselineY) =
            CalculateChildPositions(x, baselineY);

        ctx.RenderChild(Numerator, numX, numBaselineY);

        // Draw the fraction line at math axis height (above text baseline)
        var ruleCenterY = baselineY - MathAxisHeight;
        var contentWidth = Math.Max(Numerator.Width, Denominator.Width);
        var lineWidth = contentWidth + 2 * HorizontalClearance;

        var centerX = x + Width / 2;
        var lineStart = centerX - lineWidth / 2;
        var lineEnd = centerX + lineWidth / 2;

        ctx.DrawLine(lineStart, ruleCenterY, lineEnd, ruleCenterY, RuleThickness);

        ctx.RenderChild(Denominator, denomX, denomBaselineY);
    }
}