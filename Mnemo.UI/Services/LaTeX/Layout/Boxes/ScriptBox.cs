using System;
using System.Collections.Generic;
using Mnemo.UI.Services.LaTeX.Rendering;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

public class ScriptBox : Box
{
    public override IReadOnlyList<(Box child, double x, double baselineY)> GetChildPositions(double x, double baselineY)
    {
        var scriptX = x + Base.Width;
        var list = new List<(Box, double, double)> { (Base, x, baselineY) };
        if (Superscript != null)
            list.Add((Superscript, scriptX, baselineY - Superscript.Shift));
        if (Subscript != null)
            list.Add((Subscript, scriptX, baselineY - Subscript.Shift));
        return list;
    }

    public Box Base { get; }
    public Box? Subscript { get; }
    public Box? Superscript { get; }

    private const double ScriptBuffer = 1.0;

    public ScriptBox(Box baseBox, Box? subscript, Box? superscript)
    {
        Base = baseBox;
        Subscript = subscript;
        Superscript = superscript;

        Width = baseBox.Width + Math.Max(subscript?.Width ?? 0, superscript?.Width ?? 0);
        
        // Calculate height considering superscript shift (positive shift = up)
        var baseHeight = baseBox.Height;
        if (superscript != null)
        {
            // Superscript shift is positive, so we need height = superscript top position
            var supTop = superscript.Shift + superscript.Height;
            baseHeight = Math.Max(baseHeight, supTop + ScriptBuffer);
        }
        Height = baseHeight;

        // Calculate depth considering subscript shift (negative shift = down)
        var baseDepth = baseBox.Depth;
        if (subscript != null)
        {
            // Subscript shift is negative, so depth = |shift| + subscript depth
            var subBottom = Math.Abs(subscript.Shift) + subscript.Depth;
            baseDepth = Math.Max(baseDepth, subBottom + ScriptBuffer);
        }
        Depth = baseDepth;
    }

    public override void Render(IMathRenderContext ctx, double x, double baselineY)
    {
        ctx.RenderChild(Base, x, baselineY);

        var scriptX = x + Base.Width;

        if (Superscript != null)
        {
            var supBaselineY = baselineY - Superscript.Shift;
            ctx.RenderChild(Superscript, scriptX, supBaselineY);
        }

        if (Subscript != null)
        {
            var subBaselineY = baselineY - Subscript.Shift;
            ctx.RenderChild(Subscript, scriptX, subBaselineY);
        }
    }
}