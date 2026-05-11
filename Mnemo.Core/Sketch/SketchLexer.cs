using System;
using System.Collections.Generic;

namespace Mnemo.Core.Sketch;

public sealed class SketchLexer
{
    private readonly string _source;
    private readonly List<SketchDiagnostic> _diagnostics = new();
    private readonly List<SketchToken> _tokens = new();
    private int _offset;
    private int _line = 1;
    private int _column = 1;

    public SketchLexer(string source)
    {
        _source = source ?? string.Empty;
    }

    public (IReadOnlyList<SketchToken> Tokens, IReadOnlyList<SketchDiagnostic> Diagnostics) Lex()
    {
        while (!IsAtEnd)
        {
            var c = Peek();
            if (c is ' ' or '\t')
            {
                Advance();
                continue;
            }

            if (c is '\r' or '\n')
            {
                ReadNewline();
                continue;
            }

            if (c == '#')
            {
                ReadComment();
                continue;
            }

            if (c == '-' && Peek(1) == '>')
            {
                AddFixed(SketchTokenKind.ArrowDirected, "->", 2);
                continue;
            }

            if (c == ':')
            {
                AddFixed(SketchTokenKind.Colon, ":", 1);
                continue;
            }

            if (c == ',')
            {
                AddFixed(SketchTokenKind.Comma, ",", 1);
                continue;
            }

            if (c == '[')
            {
                AddFixed(SketchTokenKind.LeftBracket, "[", 1);
                continue;
            }

            if (c == ']')
            {
                AddFixed(SketchTokenKind.RightBracket, "]", 1);
                continue;
            }

            if (c == '{')
            {
                AddFixed(SketchTokenKind.LeftBrace, "{", 1);
                continue;
            }

            if (c == '}')
            {
                AddFixed(SketchTokenKind.RightBrace, "}", 1);
                continue;
            }

            if (c == '"')
            {
                ReadString();
                continue;
            }

            if (IsIdentifierStart(c))
            {
                ReadIdentifier();
                continue;
            }

            if (char.IsDigit(c))
            {
                ReadNumber();
                continue;
            }

            var start = CurrentPosition;
            var invalid = Advance().ToString();
            var span = new SourceSpan(start, CurrentPosition);
            _tokens.Add(new SketchToken(SketchTokenKind.Invalid, invalid, invalid, span));
            _diagnostics.Add(new SketchDiagnostic(
                SketchDiagnosticSeverity.Error,
                "SKETCH_LEX_INVALID_CHARACTER",
                $"Unexpected character '{invalid}'.",
                span));
        }

        _tokens.Add(new SketchToken(SketchTokenKind.EndOfFile, string.Empty, string.Empty, ZeroWidthSpan()));
        return (_tokens, _diagnostics);
    }

    private bool IsAtEnd => _offset >= _source.Length;

    private SourcePosition CurrentPosition => new(_offset, _line, _column);

    private char Peek(int lookahead = 0)
    {
        var index = _offset + lookahead;
        return index >= _source.Length ? '\0' : _source[index];
    }

    private char Advance()
    {
        var c = _source[_offset++];
        if (c == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }

        return c;
    }

    private void AddFixed(SketchTokenKind kind, string text, int length)
    {
        var start = CurrentPosition;
        for (var i = 0; i < length; i++)
            Advance();
        _tokens.Add(new SketchToken(kind, text, text, new SourceSpan(start, CurrentPosition)));
    }

    private void ReadNewline()
    {
        var start = CurrentPosition;
        if (Peek() == '\r')
        {
            Advance();
            if (Peek() == '\n')
                Advance();
            else
            {
                _line++;
                _column = 1;
            }
        }
        else
        {
            Advance();
        }

        _tokens.Add(new SketchToken(SketchTokenKind.Newline, "\n", "\n", new SourceSpan(start, CurrentPosition)));
    }

    private void ReadComment()
    {
        var start = CurrentPosition;
        while (!IsAtEnd && Peek() is not '\r' and not '\n')
            Advance();
        var text = _source[start.Offset.._offset];
        _tokens.Add(new SketchToken(SketchTokenKind.Comment, text, text[1..].Trim(), new SourceSpan(start, CurrentPosition)));
    }

    private void ReadString()
    {
        var start = CurrentPosition;
        Advance();
        var value = new System.Text.StringBuilder();
        var closed = false;

        while (!IsAtEnd)
        {
            var c = Advance();
            if (c == '"')
            {
                closed = true;
                break;
            }

            if (c is '\r' or '\n')
                break;

            if (c == '\\' && !IsAtEnd)
            {
                value.Append(Advance() switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    'n' => '\n',
                    't' => '\t',
                    var escaped => escaped
                });
                continue;
            }

            value.Append(c);
        }

        var span = new SourceSpan(start, CurrentPosition);
        var text = _source[start.Offset..Math.Min(_offset, _source.Length)];
        _tokens.Add(new SketchToken(SketchTokenKind.String, text, value.ToString(), span));
        if (!closed)
        {
            _diagnostics.Add(new SketchDiagnostic(
                SketchDiagnosticSeverity.Error,
                "SKETCH_LEX_UNTERMINATED_STRING",
                "String literal is missing a closing quote.",
                span));
        }
    }

    private void ReadIdentifier()
    {
        var start = CurrentPosition;
        while (IsIdentifierPart(Peek()))
            Advance();
        var text = _source[start.Offset.._offset];
        var kind = text switch
        {
            "sketch" => SketchTokenKind.KeywordSketch,
            "class" => SketchTokenKind.KeywordClass,
            "group" => SketchTokenKind.KeywordGroup,
            "edge" => SketchTokenKind.KeywordEdge,
            "true" or "false" => SketchTokenKind.Boolean,
            _ => SketchTokenKind.Identifier
        };
        _tokens.Add(new SketchToken(kind, text, text, new SourceSpan(start, CurrentPosition)));
    }

    private void ReadNumber()
    {
        var start = CurrentPosition;
        while (char.IsDigit(Peek()))
            Advance();
        if (Peek() == '.' && char.IsDigit(Peek(1)))
        {
            Advance();
            while (char.IsDigit(Peek()))
                Advance();
        }

        var text = _source[start.Offset.._offset];
        _tokens.Add(new SketchToken(SketchTokenKind.Number, text, text, new SourceSpan(start, CurrentPosition)));
    }

    private SourceSpan ZeroWidthSpan() => new(CurrentPosition, CurrentPosition);

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c is '_' or '-';
}
