using System;
using System.Collections.Generic;
using System.Linq;

namespace Mnemo.Core.Sketch;

public sealed class SketchResolver
{
    public ResolvedSketchDiagram Resolve(SketchParseResult parseResult)
    {
        var nodes = new Dictionary<string, MutableNode>(StringComparer.Ordinal);
        var edges = new List<ResolvedSketchEdge>();
        var edgeCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var statement in parseResult.Ast.Statements)
        {
            switch (statement)
            {
                case RawSketchNodeDecl nodeDecl:
                    DeclareNode(nodes, nodeDecl);
                    break;
                case RawSketchEdgeDecl edgeDecl:
                    EnsureImplicitNode(nodes, edgeDecl.Source);
                    EnsureImplicitNode(nodes, edgeDecl.Target);
                    var edgeBaseId = $"edge:{edgeDecl.Source.Id}:->:{edgeDecl.Target.Id}";
                    edgeCounts.TryGetValue(edgeBaseId, out var edgeIndex);
                    edgeCounts[edgeBaseId] = edgeIndex + 1;
                    edges.Add(new ResolvedSketchEdge(
                        $"{edgeBaseId}:{edgeIndex}",
                        edgeDecl.Source.Id,
                        edgeDecl.Target.Id,
                        edgeDecl.Label,
                        edgeDecl.Span));
                    break;
            }
        }

        return new ResolvedSketchDiagram(
            1,
            nodes.Values.Select(n => new ResolvedSketchNode(n.Id, n.Label, n.Declared, n.Implicit, n.SourceSpans)).ToArray(),
            edges,
            parseResult.Diagnostics);
    }

    private static void DeclareNode(Dictionary<string, MutableNode> nodes, RawSketchNodeDecl declaration)
    {
        if (!nodes.TryGetValue(declaration.Ref.Id, out var node))
        {
            node = new MutableNode(declaration.Ref.Id, declaration.Label ?? declaration.Ref.Label, true, false);
            nodes.Add(node.Id, node);
        }

        node.Declared = true;
        node.Implicit = false;
        if (!string.IsNullOrWhiteSpace(declaration.Label))
            node.Label = declaration.Label;
        node.SourceSpans.Add(declaration.Span);
    }

    private static void EnsureImplicitNode(Dictionary<string, MutableNode> nodes, RawSketchNodeRef nodeRef)
    {
        if (nodes.TryGetValue(nodeRef.Id, out var existing))
        {
            existing.SourceSpans.Add(nodeRef.Span);
            return;
        }

        nodes.Add(nodeRef.Id, new MutableNode(nodeRef.Id, nodeRef.Label, false, true)
        {
            SourceSpans = { nodeRef.Span }
        });
    }

    private sealed class MutableNode
    {
        public MutableNode(string id, string label, bool declared, bool isImplicit)
        {
            Id = id;
            Label = label;
            Declared = declared;
            Implicit = isImplicit;
        }

        public string Id { get; }
        public string Label { get; set; }
        public bool Declared { get; set; }
        public bool Implicit { get; set; }
        public List<SourceSpan> SourceSpans { get; } = new();
    }
}
