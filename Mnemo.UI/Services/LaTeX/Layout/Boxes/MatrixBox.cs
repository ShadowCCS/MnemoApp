using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Mnemo.UI.Services.LaTeX.Rendering;

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
            Width += DelimiterOffset * 2 + CellPadding;
        }

        var totalRowHeight = 0.0;
        for (int i = 0; i < RowHeights.Count; i++)
        {
            totalRowHeight += RowHeights[i] + RowDepths[i];
            if (i < RowHeights.Count - 1)
                totalRowHeight += RowSpacing;
        }

        Height = totalRowHeight / 2 + 2;
        Depth = totalRowHeight / 2 + 2;
    }

    private const double DelimiterOffset = 10;
    private const double DelimiterThickness = 1.5;

    public override IReadOnlyList<(Box child, double x, double baselineY)> GetChildPositions(double x, double baselineY)
    {
        var list = new List<(Box, double, double)>();
        var delimiterOffset = MatrixType != "matrix" ? DelimiterOffset : 0.0;
        var currentY = baselineY - Height + 2;
        for (var rowIdx = 0; rowIdx < Cells.Count; rowIdx++)
        {
            var row = Cells[rowIdx];
            var rowHeight = RowHeights[rowIdx];
            var rowDepth = RowDepths[rowIdx];
            var rowBaseline = currentY + rowHeight;
            var currentX = x + delimiterOffset;
            for (var colIdx = 0; colIdx < row.Count; colIdx++)
            {
                var cell = row[colIdx];
                var colWidth = ColumnWidths[colIdx];
                var cellX = currentX + (colWidth - cell.Width) / 2;
                list.Add((cell, cellX, rowBaseline));
                currentX += colWidth + CellPadding * 2;
            }
            currentY += rowHeight + rowDepth + RowSpacing;
        }
        return list;
    }

    public override void Render(IMathRenderContext ctx, double x, double baselineY)
    {
        var delimiterOffset = 0.0;

        if (MatrixType != "matrix")
        {
            delimiterOffset = DelimiterOffset;
            var delimHeight = (Height + Depth) * 0.9;
            var delimY = baselineY - Height + (Height + Depth - delimHeight) / 2;
            DrawDelimiter(ctx, MatrixType, x + 2, delimY, delimHeight, true);
        }

        var currentY = baselineY - Height + 2;
        for (var rowIdx = 0; rowIdx < Cells.Count; rowIdx++)
        {
            var row = Cells[rowIdx];
            var rowHeight = RowHeights[rowIdx];
            var rowDepth = RowDepths[rowIdx];
            var rowBaseline = currentY + rowHeight;

            var currentX = x + delimiterOffset;
            for (var colIdx = 0; colIdx < row.Count; colIdx++)
            {
                var cell = row[colIdx];
                var colWidth = ColumnWidths[colIdx];
                var cellX = currentX + (colWidth - cell.Width) / 2;
                ctx.RenderChild(cell, cellX, rowBaseline);
                currentX += colWidth + CellPadding * 2;
            }

            currentY += rowHeight + rowDepth + RowSpacing;
        }

        if (MatrixType != "matrix")
        {
            var delimHeight = (Height + Depth) * 0.9;
            var delimY = baselineY - Height + (Height + Depth - delimHeight) / 2;
            DrawDelimiter(ctx, MatrixType, x + Width - delimiterOffset + 5, delimY, delimHeight, false);
        }
    }

    private static void DrawDelimiter(IMathRenderContext ctx, string matrixType, double x, double y, double height, bool isLeft)
    {
        var path = new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point(x, y), IsClosed = false };

        switch (matrixType)
        {
            case "pmatrix":
                if (isLeft)
                {
                    figure.Segments!.Add(new BezierSegment
                    {
                        Point1 = new Point(x - 3, y + height * 0.2),
                        Point2 = new Point(x - 3, y + height * 0.8),
                        Point3 = new Point(x, y + height)
                    });
                }
                else
                {
                    figure.Segments!.Add(new BezierSegment
                    {
                        Point1 = new Point(x + 3, y + height * 0.2),
                        Point2 = new Point(x + 3, y + height * 0.8),
                        Point3 = new Point(x, y + height)
                    });
                }
                break;
            case "bmatrix":
                if (isLeft)
                {
                    figure.Segments!.Add(new LineSegment { Point = new Point(x + 4, y) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x, y) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x, y + height) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x + 4, y + height) });
                }
                else
                {
                    figure.Segments!.Add(new LineSegment { Point = new Point(x - 4, y) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x, y) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x, y + height) });
                    figure.Segments!.Add(new LineSegment { Point = new Point(x - 4, y + height) });
                }
                break;
            case "vmatrix":
            case "Vmatrix":
                figure.Segments!.Add(new LineSegment { Point = new Point(x, y + height) });
                break;
        }

        path.Figures!.Add(figure);
        ctx.DrawPath(path, DelimiterThickness);
    }
}