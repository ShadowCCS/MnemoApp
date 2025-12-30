namespace Mnemo.Infrastructure.Services.LaTeX.Parser.Nodes;

public record FractionNode(LaTeXNode Numerator, LaTeXNode Denominator) : LaTeXNode;

