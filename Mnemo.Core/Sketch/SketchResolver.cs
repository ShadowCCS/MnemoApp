using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Mnemo.Core.Sketch;

public sealed class SketchResolver
{
    public ResolvedSketchDiagram Resolve(SketchParseResult parseResult)
    {
        var nodes = new Dictionary<string, MutableNode>(StringComparer.Ordinal);
        var edges = new List<ResolvedSketchEdge>();
        var edgeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var classes = new Dictionary<string, ResolvedSketchStyle>(StringComparer.Ordinal);

        foreach (var statement in parseResult.Ast.Statements)
        {
            switch (statement)
            {
                case RawSketchClassDecl classDecl:
                    classes[classDecl.Name] = ResolveStyle(classDecl.Properties, classes);
                    break;
                case RawSketchNodeDecl nodeDecl:
                    DeclareNode(nodes, nodeDecl, classes);
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
                        ResolveStyle(edgeDecl.Properties, classes),
                        edgeDecl.Span));
                    break;
            }
        }

        return new ResolvedSketchDiagram(
            1,
            nodes.Values.Select(n => new ResolvedSketchNode(n.Id, n.Label, n.Style, n.Declared, n.Implicit, n.SourceSpans)).ToArray(),
            edges,
            parseResult.Diagnostics);
    }

    private static void DeclareNode(
        Dictionary<string, MutableNode> nodes,
        RawSketchNodeDecl declaration,
        IReadOnlyDictionary<string, ResolvedSketchStyle> classes)
    {
        if (!nodes.TryGetValue(declaration.Ref.Id, out var node))
        {
            node = new MutableNode(declaration.Ref.Id, declaration.Label ?? declaration.Ref.Label, ResolveStyle(declaration.Properties, classes), true, false);
            nodes.Add(node.Id, node);
        }

        node.Declared = true;
        node.Implicit = false;
        if (!string.IsNullOrWhiteSpace(declaration.Label))
            node.Label = declaration.Label;
        node.Style = ResolveStyle(declaration.Properties, classes);
        node.SourceSpans.Add(declaration.Span);
    }

    private static void EnsureImplicitNode(Dictionary<string, MutableNode> nodes, RawSketchNodeRef nodeRef)
    {
        if (nodes.TryGetValue(nodeRef.Id, out var existing))
        {
            existing.SourceSpans.Add(nodeRef.Span);
            return;
        }

        nodes.Add(nodeRef.Id, new MutableNode(nodeRef.Id, nodeRef.Label, new ResolvedSketchStyle(), false, true)
        {
            SourceSpans = { nodeRef.Span }
        });
    }

    private static ResolvedSketchStyle ResolveStyle(
        IReadOnlyList<RawSketchProperty> properties,
        IReadOnlyDictionary<string, ResolvedSketchStyle> classes)
    {
        var style = new ResolvedSketchStyle();
        var className = FindProperty(properties, "class");
        if (className != null && classes.TryGetValue(className, out var classStyle))
            style = classStyle;

        foreach (var property in properties)
        {
            style = property.Key switch
            {
                "fill" => style with { Fill = ParseColorValue(property.Value) },
                "stroke" => style with { Stroke = ParseColorValue(property.Value) },
                "stroke-width" => double.TryParse(property.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var strokeWidth)
                    ? style with { StrokeWidth = strokeWidth }
                    : style,
                "shape" => style with { Shape = property.Value },
                _ => style
            };
        }

        return style;
    }

    private static string? FindProperty(IReadOnlyList<RawSketchProperty> properties, string key) =>
        properties.LastOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal))?.Value;

    private static SketchColorValue? ParseColorValue(string value)
    {
        var text = value.Trim();
        if (text.Length == 0)
            return null;

        if (TryReadFunction(text, "hex", out var hex))
            return new SketchColorValue(SketchColorKind.Hex, hex);
        if (TryReadFunction(text, "rgb", out var rgb))
            return new SketchColorValue(SketchColorKind.Rgb, rgb);
        if (TryReadFunction(text, "rgba", out var rgba))
            return new SketchColorValue(SketchColorKind.Rgba, rgba);
        if (TryReadFunction(text, "theme", out var theme))
            return new SketchColorValue(SketchColorKind.Theme, theme);

        return new SketchColorValue(SketchColorKind.Named, text);
    }

    private static bool TryReadFunction(string text, string name, out string argument)
    {
        argument = string.Empty;
        var prefix = name + "(";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !text.EndsWith(')'))
            return false;

        argument = text[prefix.Length..^1].Trim();
        return argument.Length > 0;
    }

    private sealed class MutableNode
    {
        public MutableNode(string id, string label, ResolvedSketchStyle style, bool declared, bool isImplicit)
        {
            Id = id;
            Label = label;
            Style = style;
            Declared = declared;
            Implicit = isImplicit;
        }

        public string Id { get; }
        public string Label { get; set; }
        public ResolvedSketchStyle Style { get; set; }
        public bool Declared { get; set; }
        public bool Implicit { get; set; }
        public List<SourceSpan> SourceSpans { get; } = new();
    }
}
