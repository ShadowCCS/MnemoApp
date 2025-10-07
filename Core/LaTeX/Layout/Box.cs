using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MnemoApp.Core.LaTeX.Layout;

public abstract class Box
{
    public double Width { get; set; }
    public double Height { get; set; }
    public double Depth { get; set; }  // Depth below baseline
    public double Shift { get; set; }  // Vertical shift from baseline
    
    public double TotalHeight => Height + Depth;
}

public class CharBox : Box
{
    public string Character { get; }
    public double FontSize { get; }

    public CharBox(string character, double fontSize)
    {
        Character = character;
        FontSize = fontSize;
    }
}

public class HBox : Box
{
    public List<Box> Children { get; } = new();

    public void Add(Box box)
    {
        Children.Add(box);
        RecalculateMetrics();
    }

    private void RecalculateMetrics()
    {
        // Optimized: single pass instead of multiple LINQ queries
        Width = 0;
        Height = 0;
        Depth = 0;

        foreach (var child in Children)
        {
            Width += child.Width;
            var childHeight = child.Height + child.Shift;
            if (childHeight > Height)
                Height = childHeight;
            var childDepth = child.Depth - child.Shift;
            if (childDepth > Depth)
                Depth = childDepth;
        }
    }
}

public class VBox : Box
{
    public List<Box> Children { get; } = new();

    public void Add(Box box)
    {
        Children.Add(box);
        RecalculateMetrics();
    }

    private void RecalculateMetrics()
    {
        // Optimized: single pass instead of multiple LINQ queries
        Width = 0;
        Height = 0;
        Depth = 0;

        foreach (var child in Children)
        {
            if (child.Width > Width)
                Width = child.Width;
            Height += child.TotalHeight;
        }
    }
}

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
            Height = Math.Max(baseBox.Height, superscript.TotalHeight - baseBox.Height * 0.5);
        }
        else
        {
            Height = baseBox.Height;
        }

        if (subscript != null)
        {
            Depth = Math.Max(baseBox.Depth, subscript.TotalHeight + baseBox.Height * 0.5);
        }
        else
        {
            Depth = baseBox.Depth;
        }
    }
}

public class SqrtBox : Box
{
    public Box Content { get; }
    public double SymbolWidth { get; }
    
    public SqrtBox(Box content, double symbolWidth = 12.0)
    {
        Content = content;
        SymbolWidth = symbolWidth;
        
        Width = symbolWidth + content.Width + 4;
        Height = content.Height + 4;
        Depth = content.Depth;
    }
}

public class SpaceBox : Box
{
    public SpaceBox(double width)
    {
        Width = width;
        Height = 0;
        Depth = 0;
    }
}

public class RuleBox : Box
{
    public RuleBox(double width, double thickness)
    {
        Width = width;
        Height = thickness / 2;
        Depth = thickness / 2;
    }
}

public class MatrixBox : Box
{
    public List<List<Box>> Cells { get; }
    public string MatrixType { get; }
    public double CellPadding { get; }
    public double RowSpacing { get; }
    public List<double> ColumnWidths { get; }
    public List<double> RowHeights { get; }
    public List<double> RowDepths { get; }

    public MatrixBox(List<List<Box>> cells, string matrixType, double cellPadding = 8.0, double rowSpacing = 4.0)
    {
        Cells = cells;
        MatrixType = matrixType;
        CellPadding = cellPadding;
        RowSpacing = rowSpacing;
        
        // Calculate column widths (max width in each column)
        ColumnWidths = new List<double>();
        if (cells.Count > 0)
        {
            var numCols = cells.Max(row => row.Count);
            for (int col = 0; col < numCols; col++)
            {
                var maxWidth = 0.0;
                foreach (var row in cells)
                {
                    if (col < row.Count)
                    {
                        maxWidth = Math.Max(maxWidth, row[col].Width);
                    }
                }
                ColumnWidths.Add(maxWidth);
            }
        }

        // Calculate row heights and depths
        RowHeights = new List<double>();
        RowDepths = new List<double>();
        foreach (var row in cells)
        {
            var maxHeight = row.Count > 0 ? row.Max(cell => cell.Height) : 0;
            var maxDepth = row.Count > 0 ? row.Max(cell => cell.Depth) : 0;
            RowHeights.Add(maxHeight);
            RowDepths.Add(maxDepth);
        }

        // Calculate total dimensions
        Width = ColumnWidths.Sum() + (ColumnWidths.Count - 1) * CellPadding * 2;
        
        // Add delimiter width for parentheses/brackets
        if (matrixType != "matrix")
        {
            Width += 20; // Space for delimiters
        }

        var totalRowHeight = 0.0;
        for (int i = 0; i < RowHeights.Count; i++)
        {
            totalRowHeight += RowHeights[i] + RowDepths[i];
            if (i < RowHeights.Count - 1)
                totalRowHeight += RowSpacing;
        }

        Height = totalRowHeight / 2 + 4;
        Depth = totalRowHeight / 2 + 4;
    }
}

