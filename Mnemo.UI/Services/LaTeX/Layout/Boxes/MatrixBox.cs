using System;
using System.Collections.Generic;
using System.Linq;

namespace Mnemo.UI.Services.LaTeX.Layout.Boxes;

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

        RowHeights = new List<double>();
        RowDepths = new List<double>();
        foreach (var row in cells)
        {
            var maxHeight = row.Count > 0 ? row.Max(cell => cell.Height) : 0;
            var maxDepth = row.Count > 0 ? row.Max(cell => cell.Depth) : 0;
            RowHeights.Add(maxHeight);
            RowDepths.Add(maxDepth);
        }

        Width = ColumnWidths.Sum() + (ColumnWidths.Count - 1) * CellPadding * 2;
        
        if (matrixType != "matrix")
        {
            Width += 35;
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