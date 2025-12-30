using Mnemo.Infrastructure.Services.LaTeX.Parser.Nodes;
using System;
using System.Collections.Generic;

namespace Mnemo.Infrastructure.Services.LaTeX.Parser;

public class LaTeXParser
{
    private readonly List<LaTeXToken> _tokens;
    private int _current;
    private readonly List<ParseError> _errors = new();

    public LaTeXParser(List<LaTeXToken> tokens)
    {
        _tokens = tokens;
        _current = 0;
    }

    public IReadOnlyList<ParseError> Errors => _errors;

    public LaTeXNode Parse()
    {
        var children = new List<LaTeXNode>();

        while (!IsAtEnd())
        {
            var node = ParseExpression();
            if (node != null)
                children.Add(node);
        }

        return children.Count == 1 ? children[0] : new GroupNode(children);
    }

    private LaTeXNode? ParseExpression()
    {
        var node = ParsePrimary();
        if (node == null) return null;

        // Check for subscript/superscript
        LaTeXNode? subscript = null;
        LaTeXNode? superscript = null;

        while (Match(LaTeXTokenType.Subscript) || Match(LaTeXTokenType.Superscript))
        {
            var isSubscript = Previous().Type == LaTeXTokenType.Subscript;
            var scriptContent = ParsePrimary();

            if (isSubscript)
                subscript = scriptContent;
            else
                superscript = scriptContent;
        }

        if (subscript != null || superscript != null)
        {
            return new ScriptNode(node, subscript, superscript);
        }

        return node;
    }

    private LaTeXNode? ParsePrimary()
    {
        if (Match(LaTeXTokenType.Text))
        {
            return new TextNode(Previous().Value);
        }

        if (Match(LaTeXTokenType.BinaryOperator))
        {
            return new TextNode(Previous().Value);
        }

        if (Match(LaTeXTokenType.RelationOperator))
        {
            return new TextNode(Previous().Value);
        }

        if (Match(LaTeXTokenType.Punctuation))
        {
            return new TextNode(Previous().Value);
        }

        if (Match(LaTeXTokenType.Command))
        {
            var cmd = Previous().Value;
            return ParseCommand(cmd);
        }

        if (Match(LaTeXTokenType.OpenBrace))
        {
            var children = new List<LaTeXNode>();
            while (!Check(LaTeXTokenType.CloseBrace) && !IsAtEnd())
            {
                var node = ParseExpression();
                if (node != null)
                    children.Add(node);
            }

            if (!Match(LaTeXTokenType.CloseBrace))
            {
                RecordError("Expected closing brace", Peek().Position);
                // Error recovery: continue parsing
            }

            return children.Count == 1 ? children[0] : new GroupNode(children);
        }

        if (Match(LaTeXTokenType.OpenParen))
        {
            var children = new List<LaTeXNode>();
            children.Add(new TextNode("("));
            
            while (!Check(LaTeXTokenType.CloseParen) && !IsAtEnd())
            {
                var node = ParseExpression();
                if (node != null)
                    children.Add(node);
            }

            if (Match(LaTeXTokenType.CloseParen))
            {
                children.Add(new TextNode(")"));
            }
            else
            {
                RecordError("Expected closing parenthesis", Peek().Position);
            }

            return new GroupNode(children);
        }

        return null;
    }

    private LaTeXNode ParseCommand(string command)
    {
        return command switch
        {
            "frac" => ParseFraction(),
            "sqrt" => ParseSqrt(),
            "left" => ParseDelimiter(),
            "quad" => new SpaceNode(1.0),
            "qquad" => new SpaceNode(2.0),
            "text" => ParseText(),
            "mathbb" => ParseMathbb(),
            "begin" => ParseEnvironment(),
            "displaystyle" => new SpaceNode(0),
            "textstyle" => new SpaceNode(0),
            _ => ParseSymbol(command)
        };
    }

    private LaTeXNode ParseFraction()
    {
        var numerator = ParsePrimary() ?? new TextNode("");
        var denominator = ParsePrimary() ?? new TextNode("");
        return new FractionNode(numerator, denominator);
    }

    private LaTeXNode ParseSqrt()
    {
        LaTeXNode? index = null;
        
        // Check for optional index [n]
        if (Match(LaTeXTokenType.OpenBracket))
        {
            var indexNodes = new List<LaTeXNode>();
            while (!Check(LaTeXTokenType.CloseBracket) && !IsAtEnd())
            {
                var node = ParseExpression();
                if (node != null)
                    indexNodes.Add(node);
            }
            
            if (!Match(LaTeXTokenType.CloseBracket))
            {
                RecordError("Expected closing bracket", Peek().Position);
            }
            
            index = indexNodes.Count == 1 ? indexNodes[0] : new GroupNode(indexNodes);
        }

        var content = ParsePrimary() ?? new TextNode("");
        return new SqrtNode(content, index);
    }

    private LaTeXNode ParseDelimiter()
    {
        // After \left, expect delimiter
        var leftDelim = Match(LaTeXTokenType.Text) ? Previous().Value : "(";
        
        var children = new List<LaTeXNode>();
        while (!Check(LaTeXTokenType.Command) || Peek().Value != "right")
        {
            if (IsAtEnd()) break;
            var node = ParseExpression();
            if (node != null)
                children.Add(node);
        }

        string rightDelim = ")";
        if (Match(LaTeXTokenType.Command) && Previous().Value == "right")
        {
            rightDelim = Match(LaTeXTokenType.Text) ? Previous().Value : ")";
        }

        var content = children.Count == 1 ? children[0] : new GroupNode(children);
        return new DelimiterNode(leftDelim, content, rightDelim);
    }

    private LaTeXNode ParseText()
    {
        var content = ParsePrimary() ?? new TextNode("");
        return new TextModeNode(content);
    }

    private LaTeXNode ParseMathbb()
    {
        var content = ParsePrimary() ?? new TextNode("");
        return new MathbbNode(content);
    }

    private LaTeXNode ParseSymbol(string command)
    {
        return new SymbolNode(command);
    }

    private bool Match(LaTeXTokenType type)
    {
        if (Check(type))
        {
            Advance();
            return true;
        }
        return false;
    }

    private bool Check(LaTeXTokenType type)
    {
        if (IsAtEnd()) return false;
        return Peek().Type == type;
    }

    private LaTeXToken Advance()
    {
        if (!IsAtEnd()) _current++;
        return Previous();
    }

    private bool IsAtEnd()
    {
        return Peek().Type == LaTeXTokenType.EOF;
    }

    private LaTeXToken Peek()
    {
        return _tokens[_current];
    }

    private LaTeXToken Previous()
    {
        return _tokens[_current - 1];
    }

    private void RecordError(string message, int position)
    {
        _errors.Add(new ParseError(message, position));
    }

    private LaTeXNode ParseEnvironment()
    {
        // Parse \begin{envname}
        if (!Match(LaTeXTokenType.OpenBrace))
        {
            RecordError("Expected { after \\begin", Peek().Position);
            return new TextNode("");
        }

        if (!Match(LaTeXTokenType.Text))
        {
            RecordError("Expected environment name", Peek().Position);
            return new TextNode("");
        }

        var envName = Previous().Value;

        if (!Match(LaTeXTokenType.CloseBrace))
        {
            RecordError("Expected } after environment name", Peek().Position);
        }

        return envName switch
        {
            "matrix" => ParseMatrix("matrix"),
            "pmatrix" => ParseMatrix("pmatrix"),
            "bmatrix" => ParseMatrix("bmatrix"),
            "vmatrix" => ParseMatrix("vmatrix"),
            "Vmatrix" => ParseMatrix("Vmatrix"),
            _ => ParseGenericEnvironment(envName)
        };
    }

    private LaTeXNode ParseMatrix(string matrixType)
    {
        var rows = new List<List<LaTeXNode>>();
        var currentRow = new List<LaTeXNode>();
        var currentCell = new List<LaTeXNode>();

        while (!IsAtEnd())
        {
            // Check for \end{...}
            if (Check(LaTeXTokenType.Command) && Peek().Value == "end")
            {
                // Add current cell and row
                if (currentCell.Count > 0)
                {
                    currentRow.Add(currentCell.Count == 1 ? currentCell[0] : new GroupNode(currentCell));
                    currentCell.Clear();
                }
                if (currentRow.Count > 0)
                {
                    rows.Add(currentRow);
                }
                
                // Consume \end{...}
                Advance(); // Skip 'end'
                if (Match(LaTeXTokenType.OpenBrace))
                {
                    if (Match(LaTeXTokenType.Text))
                    {
                        // Verify it matches
                    }
                    Match(LaTeXTokenType.CloseBrace);
                }
                break;
            }

            // Check for & (column separator)
            if (Match(LaTeXTokenType.Ampersand))
            {
                currentRow.Add(currentCell.Count == 1 ? currentCell[0] : new GroupNode(currentCell));
                currentCell.Clear();
                continue;
            }

            // Check for \\ (row separator)
            if (Match(LaTeXTokenType.NewLine))
            {
                if (currentCell.Count > 0)
                {
                    currentRow.Add(currentCell.Count == 1 ? currentCell[0] : new GroupNode(currentCell));
                    currentCell.Clear();
                }
                if (currentRow.Count > 0)
                {
                    rows.Add(currentRow);
                    currentRow = new List<LaTeXNode>();
                }
                continue;
            }

            // Parse regular content
            var node = ParseExpression();
            if (node != null)
            {
                currentCell.Add(node);
            }
        }

        return new MatrixNode(rows, matrixType);
    }

    private LaTeXNode ParseGenericEnvironment(string envName)
    {
        var children = new List<LaTeXNode>();
        
        while (!IsAtEnd())
        {
            if (Check(LaTeXTokenType.Command) && Peek().Value == "end")
            {
                Advance();
                if (Match(LaTeXTokenType.OpenBrace))
                {
                    Match(LaTeXTokenType.Text);
                    Match(LaTeXTokenType.CloseBrace);
                }
                break;
            }

            var node = ParseExpression();
            if (node != null)
                children.Add(node);
        }

        return children.Count == 1 ? children[0] : new GroupNode(children);
    }
}

