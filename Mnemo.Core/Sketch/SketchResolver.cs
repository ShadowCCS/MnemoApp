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
        var groups = new List<ResolvedSketchGroup>();
        var edgeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var classes = new Dictionary<string, ResolvedSketchStyle>(StringComparer.Ordinal);
        var meta = SketchDiagramMeta.Default;

        foreach (var statement in parseResult.Ast.Statements)
        {
            switch (statement)
            {
                case RawSketchMetaBlock metaBlock:
                    meta = ResolveMeta(metaBlock.Properties);
                    break;
                case RawSketchClassDecl classDecl:
                    classes[classDecl.Name] = ResolveStyle(classDecl.Properties, classes);
                    break;
                case RawSketchNodeDecl nodeDecl:
                    DeclareNode(nodes, nodeDecl, classes);
                    break;
                case RawSketchGroupDecl groupDecl:
                    ResolveGroup(nodes, groups, groupDecl, classes);
                    break;
                case RawSketchEdgeDecl edgeDecl:
                    EnsureImplicitNode(nodes, edgeDecl.Source);
                    EnsureImplicitNode(nodes, edgeDecl.Target);
                    var opToken = EdgeDirectionToken(edgeDecl.Direction);
                    var edgeBaseId = $"edge:{edgeDecl.Source.Id}:{opToken}:{edgeDecl.Target.Id}";
                    edgeCounts.TryGetValue(edgeBaseId, out var edgeIndex);
                    edgeCounts[edgeBaseId] = edgeIndex + 1;
                    edges.Add(new ResolvedSketchEdge(
                        $"{edgeBaseId}:{edgeIndex}",
                        edgeDecl.Source.Id,
                        edgeDecl.Target.Id,
                        edgeDecl.Direction,
                        edgeDecl.Label,
                        ResolveStyle(edgeDecl.Properties, classes),
                        edgeDecl.Span));
                    break;
            }
        }

        return new ResolvedSketchDiagram(
            1,
            meta,
            nodes.Values.Select(n => new ResolvedSketchNode(n.Id, n.Label, n.Style, n.Declared, n.Implicit, n.SourceSpans)).ToArray(),
            edges,
            groups,
            parseResult.Diagnostics);
    }

    private static void ResolveGroup(
        Dictionary<string, MutableNode> nodes,
        List<ResolvedSketchGroup> groups,
        RawSketchGroupDecl groupDecl,
        IReadOnlyDictionary<string, ResolvedSketchStyle> classes)
    {
        foreach (var memberRef in groupDecl.MemberRefs)
            EnsureImplicitNode(nodes, memberRef);

        var style = ResolveStyle(groupDecl.Properties, classes);
        var label = groupDecl.Label ?? groupDecl.Name;
        var nodeIds = groupDecl.MemberRefs.Select(r => r.Id).Distinct(StringComparer.Ordinal).ToArray();

        groups.Add(new ResolvedSketchGroup(groupDecl.Name, label, nodeIds, style, groupDecl.Span));
    }

    private static SketchDiagramMeta ResolveMeta(IReadOnlyList<RawSketchProperty> properties)
    {
        var title = FindProperty(properties, "title");
        var layout = FindProperty(properties, "layout");
        var directionRaw = FindProperty(properties, "direction") ?? FindProperty(properties, "layout.dir");
        var direction = ParseLayoutDirection(directionRaw);
        return new SketchDiagramMeta(title, direction, layout ?? "dag");
    }

    private static SketchLayoutDirection ParseLayoutDirection(string? value) =>
        (value ?? string.Empty).ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal) switch
        {
            "left-to-right" or "ltr" or "lr" => SketchLayoutDirection.LeftToRight,
            "top-to-bottom" or "ttb" or "tb" => SketchLayoutDirection.TopToBottom,
            "right-to-left" or "rtl" or "rl" => SketchLayoutDirection.RightToLeft,
            "bottom-to-top" or "btt" or "bt" => SketchLayoutDirection.BottomToTop,
            _ => SketchLayoutDirection.LeftToRight
        };

    private static string EdgeDirectionToken(SketchEdgeDirection direction) =>
        direction switch
        {
            SketchEdgeDirection.Undirected => "--",
            SketchEdgeDirection.Bidirectional => "<->",
            _ => "->"
        };

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

        var classNames = ParseClassNames(FindProperty(properties, "class"));
        foreach (var className in classNames)
        {
            if (classes.TryGetValue(className, out var classStyle))
                style = MergeStyles(style, classStyle);
        }

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
                "style" => style with { LineStyle = ParseEdgeLineStyle(property.Value) },
                "tooltip" => style with { Tooltip = property.Value.Trim() },
                _ => style
            };
        }

        return style;
    }

    /// <summary>
    /// Merges an accumulated class style with a new (later) class style.
    /// The new class's non-null properties take priority, falling back to the accumulated values.
    /// </summary>
    private static ResolvedSketchStyle MergeStyles(ResolvedSketchStyle accumulated, ResolvedSketchStyle newClass) =>
        new(
            newClass.Fill ?? accumulated.Fill,
            newClass.Stroke ?? accumulated.Stroke,
            newClass.StrokeWidth ?? accumulated.StrokeWidth,
            newClass.Shape ?? accumulated.Shape,
            newClass.LineStyle ?? accumulated.LineStyle,
            newClass.Tooltip ?? accumulated.Tooltip);

    private static SketchEdgeLineStyle? ParseEdgeLineStyle(string value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "dashed" => SketchEdgeLineStyle.Dashed,
            "dotted" => SketchEdgeLineStyle.Dotted,
            "solid" => SketchEdgeLineStyle.Solid,
            _ => null
        };

    /// <summary>
    /// Parses a class value that may be a single name like <c>service</c>
    /// or a bracket-list like <c>[service,critical]</c>.
    /// </summary>
    private static IReadOnlyList<string> ParseClassNames(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var trimmed = value.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            var inner = trimmed[1..^1];
            return inner
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => s.Length > 0)
                .ToArray();
        }

        return [trimmed];
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
