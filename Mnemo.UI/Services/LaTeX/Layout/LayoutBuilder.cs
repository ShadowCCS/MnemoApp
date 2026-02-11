using Mnemo.Infrastructure.Services.LaTeX.Parser;
using Mnemo.Infrastructure.Services.LaTeX.Parser.Nodes;
using Mnemo.Infrastructure.Services.LaTeX.Symbols;
using Mnemo.UI.Services.LaTeX.Layout.Boxes;
using Mnemo.UI.Services.LaTeX.Metrics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mnemo.UI.Services.LaTeX.Layout;

public class LayoutBuilder
{
    private readonly double _fontSize;
    
    // Spacing constants for mathematical operators
    private const double ThinSpace = 0.1667; // 1/6 em
    private const double MediumSpace = 0.2222; // 2/9 em
    private const double ThickSpace = 0.2778; // 5/18 em

    public LayoutBuilder(double fontSize = 16.0)
    {
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
        var content = text.Content;
        
        for (int i = 0; i < content.Length; i++)
        {
            var ch = content[i].ToString();
            
            // Add spacing before operators (but not at the start)
            if (i > 0)
            {
                var spacing = GetOperatorSpacing(ch);
                if (spacing > 0)
                {
                    hbox.Add(new SpaceBox(spacing));
                }
            }
            
            var (width, height, depth) = FontMetrics.Instance.MeasureChar(ch, _fontSize);
            var charBox = new CharBox(ch, _fontSize)
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

        var (width, height, depth) = FontMetrics.Instance.MeasureChar(symbolChar, _fontSize);
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

        var thickness = FontMetrics.Instance.GetFractionRuleThickness(_fontSize);
        var numeratorSpacing = FontMetrics.Instance.GetFractionNumeratorShift(_fontSize);
        var denominatorSpacing = FontMetrics.Instance.GetFractionDenominatorShift(_fontSize);
        return new FractionBox(numerator, denominator, thickness, numeratorSpacing, denominatorSpacing);
    }

    private Box BuildScript(ScriptNode script)
    {
        var baseBox = BuildLayout(script.Base);
        
        Box? subscriptBox = null;
        if (script.Subscript != null)
        {
            var subBuilder = new LayoutBuilder(FontMetrics.Instance.GetScriptSize(_fontSize));
            subscriptBox = subBuilder.BuildLayout(script.Subscript);
            subscriptBox.Shift = -FontMetrics.Instance.GetScriptShiftDown(_fontSize);
        }

        Box? superscriptBox = null;
        if (script.Superscript != null)
        {
            var supBuilder = new LayoutBuilder(FontMetrics.Instance.GetScriptSize(_fontSize));
            superscriptBox = supBuilder.BuildLayout(script.Superscript);
            superscriptBox.Shift = FontMetrics.Instance.GetScriptShiftUp(_fontSize);
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
        for (int i = 0; i < group.Children.Count; i++)
        {
            var child = group.Children[i];
            var childBox = BuildLayout(child);
            
            // Add spacing before this child if it's an operator
            if (i > 0)
            {
                var spacing = GetSpacingBeforeChild(child);
                if (spacing > 0)
                {
                    hbox.Add(new SpaceBox(spacing));
                }
            }
            
            hbox.Add(childBox);
            
            // Add spacing after this child if it's an operator (but not at the end)
            if (i < group.Children.Count - 1)
            {
                var spacing = GetSpacingAfterChild(child);
                if (spacing > 0)
                {
                    hbox.Add(new SpaceBox(spacing));
                }
            }
        }
        return hbox;
    }

    private Box BuildSqrt(SqrtNode sqrt)
    {
        var content = BuildLayout(sqrt.Content);
        var symbolWidth = FontMetrics.Instance.GetSqrtSymbolWidth(_fontSize);
        var ruleThickness = FontMetrics.Instance.GetSqrtRuleThickness(_fontSize);
        var padding = FontMetrics.Instance.GetSqrtPadding(_fontSize);
        return new SqrtBox(content, symbolWidth, ruleThickness, padding);
    }

    private Box BuildDelimiter(DelimiterNode delim)
    {
        var content = BuildLayout(delim.Content);
        
        var hbox = new HBox();
        
        // Calculate scaling for left delimiter to match content height
        // Standard height is height + depth of the character
        var standardMetrics = FontMetrics.Instance.MeasureChar(delim.LeftDelim, _fontSize);
        var targetHeight = content.TotalHeight;
        var standardTotal = standardMetrics.height + standardMetrics.depth;
        
        // Ensure we don't shrink below standard size
        var scale = Math.Max(1.0, targetHeight / Math.Max(0.1, standardTotal));
        var delimFontSize = _fontSize * scale;
        
        // Add left delimiter
        var (leftWidth, leftHeight, leftDepth) = FontMetrics.Instance.MeasureChar(delim.LeftDelim, delimFontSize);
        
        // Adjust for slight over-scaling if needed, or centering
        // For simple scaling, we just use the new metrics
        var leftBox = new CharBox(delim.LeftDelim, delimFontSize)
        {
            Width = leftWidth,
            Height = leftHeight,
            Depth = leftDepth
        };
        
        // Center the delimiter vertically relative to content axis if possible, 
        // but here we just align baselines or ensure coverage.
        // A simple heuristic: Shift down slightly if it's a bracket to center on math axis.
        // For now, let's keep shift 0 and rely on the font's baseline.
        
        hbox.Add(leftBox);
        
        // Add content
        hbox.Add(content);
        
        // Right delimiter
        var rightMetrics = FontMetrics.Instance.MeasureChar(delim.RightDelim, _fontSize);
        var rightStandardTotal = rightMetrics.height + rightMetrics.depth;
        var rightScale = Math.Max(1.0, targetHeight / Math.Max(0.1, rightStandardTotal));
        var rightFontSize = _fontSize * rightScale;
        
        var (rightWidth, rightHeight, rightDepth) = FontMetrics.Instance.MeasureChar(delim.RightDelim, rightFontSize);
        var rightBox = new CharBox(delim.RightDelim, rightFontSize)
        {
            Width = rightWidth,
            Height = rightHeight,
            Depth = rightDepth
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
                var (width, height, depth) = FontMetrics.Instance.MeasureChar(symbol, _fontSize);
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

    private bool IsBinaryOperator(string character)
    {
        return character is "+" or "-" or "*" or "/" or "×" or "÷" or "⋅" or "∘" or "±" or "∓" or "∧" or "∨" or "⊕" or "⊗";
    }

    private bool IsRelationOperator(string character)
    {
        return character is "=" or "≠" or "<" or ">" or "≤" or "≥" or "≈" or "≡" or "∼" or "∝" or "≪" or "≫" or "∈" or "∉" or "⊂" or "⊃" or "⊆" or "⊇";
    }

    private bool IsPunctuation(string character)
    {
        return character is "," or ";" or ":" or "!" or "?" or "." or "…" or "(" or ")" or "[" or "]" or "{" or "}";
    }

    private double GetOperatorSpacing(string character)
    {
        if (IsBinaryOperator(character))
            return MediumSpace * _fontSize;
        if (IsRelationOperator(character))
            return ThickSpace * _fontSize;
        if (IsPunctuation(character))
            return ThinSpace * _fontSize;
        return 0;
    }

    private double GetSpacingBeforeChild(LaTeXNode child)
    {
        // Check if this child starts with an operator
        if (child is TextNode textNode && !string.IsNullOrEmpty(textNode.Content))
        {
            var firstChar = textNode.Content[0].ToString();
            return GetOperatorSpacing(firstChar);
        }
        
        if (child is SymbolNode symbolNode)
        {
            var symbolChar = SymbolRegistry.GetSymbol(symbolNode.Symbol);
            if (symbolChar != null)
            {
                return GetOperatorSpacing(symbolChar);
            }
        }
        
        return 0;
    }

    private double GetSpacingAfterChild(LaTeXNode child)
    {
        // Check if this child ends with an operator
        if (child is TextNode textNode && !string.IsNullOrEmpty(textNode.Content))
        {
            var lastChar = textNode.Content[^1].ToString();
            return GetOperatorSpacing(lastChar);
        }
        
        if (child is SymbolNode symbolNode)
        {
            var symbolChar = SymbolRegistry.GetSymbol(symbolNode.Symbol);
            if (symbolChar != null)
            {
                return GetOperatorSpacing(symbolChar);
            }
        }
        
        return 0;
    }
}