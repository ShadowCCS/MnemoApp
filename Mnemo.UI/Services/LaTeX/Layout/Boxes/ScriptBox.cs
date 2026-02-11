using System;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

public class ScriptBox : Box
{
    public Box Base { get; }
    public Box? Subscript { get; }
    public Box? Superscript { get; }

    private const double ScriptBuffer = 4.0;

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
}