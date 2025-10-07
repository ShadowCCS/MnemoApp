using MnemoApp.Core.LaTeX.Parser;
using MnemoApp.Core.LaTeX.Metrics;
using MnemoApp.Core.LaTeX.Symbols;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MnemoApp.Core.LaTeX.Layout;

public class LayoutBuilder
{
    private readonly FontMetrics _metrics;
    private readonly double _fontSize;

    public LayoutBuilder(double fontSize = 16.0)
    {
        _metrics = new FontMetrics();
        _fontSize = fontSize;
    }

    public Box BuildLayout(LaTeXNode node)
    {
        return node switch
        {
            TextNode text => BuildText(text),
            SymbolNode symbol => BuildSymbol(symbol),
            FractionNode fraction => BuildFraction(fraction),
            ScriptNode script => BuildScript(script),
            GroupNode group => BuildGroup(group),
            SqrtNode sqrt => BuildSqrt(sqrt),
            DelimiterNode delim => BuildDelimiter(delim),
            SpaceNode space => BuildSpace(space),
            TextModeNode textMode => BuildTextMode(textMode),
            MathbbNode mathbb => BuildMathbb(mathbb),
            MatrixNode matrix => BuildMatrix(matrix),
            _ => new SpaceBox(0)
        };
    }

    private Box BuildText(TextNode text)
    {
        if (string.IsNullOrEmpty(text.Content))
            return new SpaceBox(0);

        var hbox = new HBox();
        foreach (var ch in text.Content)
        {
            var (width, height, depth) = _metrics.MeasureChar(ch.ToString(), _fontSize);
            var charBox = new CharBox(ch.ToString(), _fontSize)
            {
                Width = width,
                Height = height,
                Depth = depth
            };
            hbox.Add(charBox);
        }
        return hbox;
    }

    private Box BuildSymbol(SymbolNode symbol)
    {
        var symbolChar = SymbolRegistry.GetSymbol(symbol.Symbol);
        if (symbolChar == null)
        {
            // Unknown symbol, render the command name
            return BuildText(new TextNode(symbol.Symbol));
        }

        var (width, height, depth) = _metrics.MeasureChar(symbolChar, _fontSize);
        return new CharBox(symbolChar, _fontSize)
        {
            Width = width,
            Height = height,
            Depth = depth
        };
    }

    private Box BuildFraction(FractionNode fraction)
    {
        var numBuilder = new LayoutBuilder(_fontSize * 0.8);
        var denomBuilder = new LayoutBuilder(_fontSize * 0.8);

        var numerator = numBuilder.BuildLayout(fraction.Numerator);
        var denominator = denomBuilder.BuildLayout(fraction.Denominator);

        var thickness = _metrics.GetFractionRuleThickness(_fontSize);
        var numeratorSpacing = _metrics.GetFractionNumeratorShift(_fontSize);
        var denominatorSpacing = _metrics.GetFractionDenominatorShift(_fontSize);
        return new FractionBox(numerator, denominator, thickness, numeratorSpacing, denominatorSpacing);
    }

    private Box BuildScript(ScriptNode script)
    {
        var baseBox = BuildLayout(script.Base);
        
        Box? subscriptBox = null;
        if (script.Subscript != null)
        {
            var subBuilder = new LayoutBuilder(_metrics.GetScriptSize(_fontSize));
            subscriptBox = subBuilder.BuildLayout(script.Subscript);
            subscriptBox.Shift = -_metrics.GetScriptShiftDown(_fontSize);
        }

        Box? superscriptBox = null;
        if (script.Superscript != null)
        {
            var supBuilder = new LayoutBuilder(_metrics.GetScriptSize(_fontSize));
            superscriptBox = supBuilder.BuildLayout(script.Superscript);
            superscriptBox.Shift = _metrics.GetScriptShiftUp(_fontSize);
        }

        return new ScriptBox(baseBox, subscriptBox, superscriptBox);
    }

    private Box BuildGroup(GroupNode group)
    {
        if (group.Children.Count == 0)
            return new SpaceBox(0);

        if (group.Children.Count == 1)
            return BuildLayout(group.Children[0]);

        var hbox = new HBox();
        foreach (var child in group.Children)
        {
            hbox.Add(BuildLayout(child));
        }
        return hbox;
    }

    private Box BuildSqrt(SqrtNode sqrt)
    {
        var content = BuildLayout(sqrt.Content);
        return new SqrtBox(content);
    }

    private Box BuildDelimiter(DelimiterNode delim)
    {
        var content = BuildLayout(delim.Content);
        
        var hbox = new HBox();
        
        // Add left delimiter
        var (leftWidth, leftHeight, leftDepth) = _metrics.MeasureChar(delim.LeftDelim, _fontSize * 1.2);
        var leftBox = new CharBox(delim.LeftDelim, _fontSize * 1.2)
        {
            Width = leftWidth,
            Height = Math.Max(leftHeight, content.Height),
            Depth = Math.Max(leftDepth, content.Depth)
        };
        hbox.Add(leftBox);
        
        // Add content
        hbox.Add(content);
        
        // Add right delimiter
        var (rightWidth, rightHeight, rightDepth) = _metrics.MeasureChar(delim.RightDelim, _fontSize * 1.2);
        var rightBox = new CharBox(delim.RightDelim, _fontSize * 1.2)
        {
            Width = rightWidth,
            Height = Math.Max(rightHeight, content.Height),
            Depth = Math.Max(rightDepth, content.Depth)
        };
        hbox.Add(rightBox);
        
        return hbox;
    }

    private Box BuildSpace(SpaceNode space)
    {
        // Convert em units to pixels (1em = font size)
        var spaceWidth = space.Width * _fontSize;
        return new SpaceBox(spaceWidth);
    }

    private Box BuildTextMode(TextModeNode textMode)
    {
        // Text mode renders content in regular font (not italic)
        return BuildLayout(textMode.Content);
    }

    private Box BuildMathbb(MathbbNode mathbb)
    {
        // Handle blackboard bold symbols
        if (mathbb.Content is TextNode textNode)
        {
            var symbolKey = $"mathbb{{{textNode.Content}}}";
            var symbol = SymbolRegistry.GetSymbol(symbolKey);
            if (symbol != null)
            {
                var (width, height, depth) = _metrics.MeasureChar(symbol, _fontSize);
                return new CharBox(symbol, _fontSize)
                {
                    Width = width,
                    Height = height,
                    Depth = depth
                };
            }
        }
        
        // Fallback to regular text
        return BuildLayout(mathbb.Content);
    }

    private Box BuildMatrix(MatrixNode matrix)
    {
        var cellBoxes = new List<List<Box>>();
        
        // Build layout for each cell
        foreach (var row in matrix.Rows)
        {
            var rowBoxes = new List<Box>();
            foreach (var cell in row)
            {
                rowBoxes.Add(BuildLayout(cell));
            }
            cellBoxes.Add(rowBoxes);
        }

        return new MatrixBox(cellBoxes, matrix.MatrixType);
    }
}

