using System.Collections.Generic;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

public abstract class Box
{
    public double Width { get; set; }
    public double Height { get; set; }
    public double Depth { get; set; }  // Depth below baseline
    public double Shift { get; set; }  // Vertical shift from baseline
    
    public double TotalHeight => Height + Depth;
}

