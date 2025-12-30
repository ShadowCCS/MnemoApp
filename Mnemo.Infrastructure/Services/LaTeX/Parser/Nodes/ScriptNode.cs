namespace Mnemo.Infrastructure.Services.LaTeX.Parser.Nodes;

public record ScriptNode(LaTeXNode Base, LaTeXNode? Subscript, LaTeXNode? Superscript) : LaTeXNode;

