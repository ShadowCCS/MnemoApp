namespace Mnemo.Infrastructure.Services.LaTeX.Parser.Nodes;

public record DelimiterNode(string LeftDelim, LaTeXNode Content, string RightDelim) : LaTeXNode;