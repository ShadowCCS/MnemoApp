using System;

namespace MnemoApp.Core.LaTeX.Parser;

public enum LaTeXTokenType
{
    Text,
    Command,
    OpenBrace,
    CloseBrace,
    OpenBracket,    // [
    CloseBracket,   // ]
    OpenParen,      // (
    CloseParen,     // )
    Subscript,      // _
    Superscript,    // ^
    Ampersand,      // & (for matrix columns)
    NewLine,        // \\ (for matrix rows)
    BinaryOperator, // +, -, *, /, etc.
    RelationOperator, // =, <, >, etc.
    Punctuation,    // ,, ;, :, etc.
    EOF
}

public record LaTeXToken(LaTeXTokenType Type, string Value, int Position);

public class ParseError : Exception
{
    public int Position { get; }
    
    public ParseError(string message, int position) : base(message)
    {
        Position = position;
    }
}

