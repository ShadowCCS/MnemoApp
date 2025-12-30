namespace Mnemo.Infrastructure.Services.LaTeX.Parser;

public record LaTeXToken(LaTeXTokenType Type, string Value, int Position);
