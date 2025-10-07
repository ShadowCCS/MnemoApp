using System.Collections.Generic;

namespace MnemoApp.Core.LaTeX.Parser;

public abstract record LaTeXNode;

public record TextNode(string Content) : LaTeXNode;

public record SymbolNode(string Symbol) : LaTeXNode;

public record FractionNode(LaTeXNode Numerator, LaTeXNode Denominator) : LaTeXNode;

public record ScriptNode(LaTeXNode Base, LaTeXNode? Subscript, LaTeXNode? Superscript) : LaTeXNode;

public record GroupNode(List<LaTeXNode> Children) : LaTeXNode;

public record SqrtNode(LaTeXNode Content, LaTeXNode? Index = null) : LaTeXNode;

public record DelimiterNode(string LeftDelim, LaTeXNode Content, string RightDelim) : LaTeXNode;

public record SpaceNode(double Width) : LaTeXNode;

public record TextModeNode(LaTeXNode Content) : LaTeXNode;

public record MathbbNode(LaTeXNode Content) : LaTeXNode;

public record MatrixNode(List<List<LaTeXNode>> Rows, string MatrixType) : LaTeXNode;

