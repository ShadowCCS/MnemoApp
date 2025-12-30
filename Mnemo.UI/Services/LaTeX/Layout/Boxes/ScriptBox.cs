using System;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

public class ScriptBox : Box
{
    public Box Base { get; }
    public Box? Subscript { get; }
    public Box? Superscript { get; }

    public ScriptBox(Box baseBox, Box? subscript, Box? superscript)
    {
        Base = baseBox;
        Subscript = subscript;
        Superscript = superscript;

        Width = baseBox.Width + Math.Max(subscript?.Width ?? 0, superscript?.Width ?? 0);
        
        if (superscript != null)
        {
            Height = baseBox.Height + superscript.TotalHeight;
        }
        else
        {
            Height = baseBox.Height;
        }

        if (subscript != null)
        {
            Depth = baseBox.Depth + subscript.TotalHeight;
        }
        else
        {
            Depth = baseBox.Depth;
        }
    }
}

