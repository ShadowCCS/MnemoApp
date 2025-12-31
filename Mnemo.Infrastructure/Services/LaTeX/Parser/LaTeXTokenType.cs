namespace Mnemo.Infrastructure.Services.LaTeX.Parser;

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