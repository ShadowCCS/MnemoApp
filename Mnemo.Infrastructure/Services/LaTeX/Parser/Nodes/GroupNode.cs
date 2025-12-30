using System.Collections.Generic;

namespace Mnemo.Infrastructure.Services.LaTeX.Parser.Nodes;

public record GroupNode(List<LaTeXNode> Children) : LaTeXNode;

