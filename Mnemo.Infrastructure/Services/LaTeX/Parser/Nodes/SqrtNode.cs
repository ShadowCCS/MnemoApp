namespace Mnemo.Infrastructure.Services.LaTeX.Parser.Nodes;

public record SqrtNode(LaTeXNode Content, LaTeXNode? Index = null) : LaTeXNode;

