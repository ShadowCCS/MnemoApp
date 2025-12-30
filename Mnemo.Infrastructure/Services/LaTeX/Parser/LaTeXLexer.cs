using System;
using System.Collections.Generic;
using System.Text;

namespace Mnemo.Infrastructure.Services.LaTeX.Parser;

public class LaTeXLexer
{
    private readonly string _input;
    private int _position;

    public LaTeXLexer(string input)
    {
        _input = input ?? string.Empty;
        _position = 0;
    }

    public List<LaTeXToken> Tokenize()
    {
        var tokens = new List<LaTeXToken>();

        while (_position < _input.Length)
        {
            var ch = _input[_position];

            if (ch == '\\')
            {
                // Check for newline command \\
                if (_position + 1 < _input.Length && _input[_position + 1] == '\\')
                {
                    tokens.Add(new LaTeXToken(LaTeXTokenType.NewLine, "\\\\", _position));
                    _position += 2;
                }
                else
                {
                    tokens.Add(ReadCommand());
                }
            }
            else if (ch == '{')
            {
                tokens.Add(new LaTeXToken(LaTeXTokenType.OpenBrace, "{", _position));
                _position++;
            }
            else if (ch == '}')
            {
                tokens.Add(new LaTeXToken(LaTeXTokenType.CloseBrace, "}", _position));
                _position++;
            }
            else if (ch == '[')
            {
                tokens.Add(new LaTeXToken(LaTeXTokenType.OpenBracket, "[", _position));
                _position++;
            }
            else if (ch == ']')
            {
                tokens.Add(new LaTeXToken(LaTeXTokenType.CloseBracket, "]", _position));
                _position++;
            }
            else if (ch == '(')
            {
                tokens.Add(new LaTeXToken(LaTeXTokenType.OpenParen, "(", _position));
                _position++;
            }
            else if (ch == ')')
            {
                tokens.Add(new LaTeXToken(LaTeXTokenType.CloseParen, ")", _position));
                _position++;
            }
            else if (ch == '_')
            {
                tokens.Add(new LaTeXToken(LaTeXTokenType.Subscript, "_", _position));
                _position++;
            }
            else if (ch == '^')
            {
                tokens.Add(new LaTeXToken(LaTeXTokenType.Superscript, "^", _position));
                _position++;
            }
            else if (ch == '&')
            {
                tokens.Add(new LaTeXToken(LaTeXTokenType.Ampersand, "&", _position));
                _position++;
            }
            else if (IsBinaryOperator(ch))
            {
                tokens.Add(new LaTeXToken(LaTeXTokenType.BinaryOperator, ch.ToString(), _position));
                _position++;
            }
            else if (IsRelationOperator(ch))
            {
                tokens.Add(new LaTeXToken(LaTeXTokenType.RelationOperator, ch.ToString(), _position));
                _position++;
            }
            else if (IsPunctuation(ch))
            {
                tokens.Add(new LaTeXToken(LaTeXTokenType.Punctuation, ch.ToString(), _position));
                _position++;
            }
            else if (char.IsWhiteSpace(ch))
            {
                _position++;
            }
            else
            {
                tokens.Add(ReadText());
            }
        }

        tokens.Add(new LaTeXToken(LaTeXTokenType.EOF, "", _position));
        return tokens;
    }

    private LaTeXToken ReadCommand()
    {
        var start = _position;
        _position++; // skip '\'

        var sb = new StringBuilder();
        while (_position < _input.Length && char.IsLetter(_input[_position]))
        {
            sb.Append(_input[_position]);
            _position++;
        }

        return new LaTeXToken(LaTeXTokenType.Command, sb.ToString(), start);
    }

    private LaTeXToken ReadText()
    {
        var start = _position;
        var sb = new StringBuilder();

        while (_position < _input.Length)
        {
            var ch = _input[_position];
            if (ch == '\\' || ch == '{' || ch == '}' || ch == '[' || ch == ']' || 
                ch == '(' || ch == ')' || ch == '_' || ch == '^' || ch == '&' || 
                IsBinaryOperator(ch) || IsRelationOperator(ch) || IsPunctuation(ch) ||
                char.IsWhiteSpace(ch))
                break;

            sb.Append(ch);
            _position++;
        }

        return new LaTeXToken(LaTeXTokenType.Text, sb.ToString(), start);
    }

    private static bool IsBinaryOperator(char ch)
    {
        return ch is '+' or '-' or '*' or '/' or '×' or '÷' or '⋅' or '∘' or '±' or '∓' or '∧' or '∨' or '⊕' or '⊗';
    }

    private static bool IsRelationOperator(char ch)
    {
        return ch is '=' or '≠' or '<' or '>' or '≤' or '≥' or '≈' or '≡' or '∼' or '∝' or '≪' or '≫' or '∈' or '∉' or '⊂' or '⊃' or '⊆' or '⊇';
    }

    private static bool IsPunctuation(char ch)
    {
        return ch is ',' or ';' or ':' or '!' or '?' or '.' or '…';
    }
}

