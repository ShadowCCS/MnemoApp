using System.Collections.Generic;

namespace Mnemo.Infrastructure.Services.LaTeX.Parser.Nodes;

public record MatrixNode(List<List<LaTeXNode>> Rows, string MatrixType) : LaTeXNode;

