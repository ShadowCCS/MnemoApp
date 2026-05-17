using System.Collections.Generic;

namespace Mnemo.Core.Sketch;

public enum SketchDiagnosticSeverity
{
    Error,
    Warning,
    Info
}

public readonly record struct SourcePosition(int Offset, int Line, int Column);

public readonly record struct SourceSpan(SourcePosition Start, SourcePosition End);

public sealed record SketchDiagnostic(
    SketchDiagnosticSeverity Severity,
    string Code,
    string Message,
    SourceSpan? Span = null);

public enum SketchTokenKind
{
    Identifier,
    String,
    HexColor,
    Number,
    Boolean,
    Colon,
    Comma,
    LeftBracket,
    RightBracket,
    LeftParen,
    RightParen,
    LeftBrace,
    RightBrace,
    ArrowDirected,
    ArrowUndirected,
    ArrowBidirectional,
    KeywordSketch,
    KeywordClass,
    KeywordGroup,
    KeywordEdge,
    Comment,
    Newline,
    EndOfFile,
    Invalid
}

public enum SketchEdgeDirection
{
    Directed,
    Undirected,
    Bidirectional
}

public enum SketchLayoutDirection
{
    LeftToRight,
    TopToBottom,
    RightToLeft,
    BottomToTop
}

public enum SketchEdgeLineStyle
{
    Solid,
    Dashed,
    Dotted
}

public sealed record SketchToken(SketchTokenKind Kind, string Text, string Value, SourceSpan Span);

public sealed record RawSketchAst(IReadOnlyList<RawSketchStatement> Statements, SourceSpan Span);

public abstract record RawSketchStatement(SourceSpan Span);

public sealed record RawSketchMetaBlock(
    IReadOnlyList<RawSketchProperty> Properties,
    SourceSpan Span) : RawSketchStatement(Span);

public sealed record RawSketchEdgeDecl(
    RawSketchNodeRef Source,
    RawSketchNodeRef Target,
    SketchEdgeDirection Direction,
    string? Label,
    IReadOnlyList<RawSketchProperty> Properties,
    SourceSpan Span) : RawSketchStatement(Span);

public sealed record RawSketchNodeDecl(
    RawSketchNodeRef Ref,
    string? Label,
    IReadOnlyList<RawSketchProperty> Properties,
    SourceSpan Span) : RawSketchStatement(Span);

public sealed record RawSketchClassDecl(
    string Name,
    IReadOnlyList<RawSketchProperty> Properties,
    SourceSpan Span) : RawSketchStatement(Span);

public sealed record RawSketchGroupDecl(
    string Name,
    string? Label,
    IReadOnlyList<RawSketchProperty> Properties,
    IReadOnlyList<RawSketchNodeRef> MemberRefs,
    SourceSpan Span) : RawSketchStatement(Span);

public sealed record RawSketchProperty(string Key, string Value, SourceSpan Span);

public sealed record RawSketchComment(string Text, SourceSpan Span) : RawSketchStatement(Span);

public sealed record RawSketchIgnoredStatement(string Kind, SourceSpan Span) : RawSketchStatement(Span);

public sealed record RawSketchNodeRef(string Id, string Label, bool IsBracketId, SourceSpan Span);

public sealed record SketchParseResult(
    RawSketchAst Ast,
    IReadOnlyList<SketchToken> Tokens,
    IReadOnlyList<SketchDiagnostic> Diagnostics);

public sealed record SketchDiagramMeta(
    string? Title,
    SketchLayoutDirection Direction,
    string? Layout)
{
    public static readonly SketchDiagramMeta Default = new(null, SketchLayoutDirection.LeftToRight, "dag");
}

public sealed record ResolvedSketchGroup(
    string Id,
    string Label,
    IReadOnlyList<string> NodeIds,
    ResolvedSketchStyle Style,
    SourceSpan SourceSpan);

public sealed record ResolvedSketchDiagram(
    int Version,
    SketchDiagramMeta Meta,
    IReadOnlyList<ResolvedSketchNode> Nodes,
    IReadOnlyList<ResolvedSketchEdge> Edges,
    IReadOnlyList<ResolvedSketchGroup> Groups,
    IReadOnlyList<SketchDiagnostic> Diagnostics);

public sealed record ResolvedSketchNode(
    string Id,
    string Label,
    ResolvedSketchStyle Style,
    bool Declared,
    bool Implicit,
    IReadOnlyList<SourceSpan> SourceSpans);

public sealed record ResolvedSketchEdge(
    string Id,
    string SourceId,
    string TargetId,
    SketchEdgeDirection Direction,
    string? Label,
    ResolvedSketchStyle Style,
    SourceSpan SourceSpan);

public sealed record ResolvedSketchStyle(
    SketchColorValue? Fill = null,
    SketchColorValue? Stroke = null,
    double? StrokeWidth = null,
    string? Shape = null,
    SketchEdgeLineStyle? LineStyle = null,
    string? Tooltip = null);

public enum SketchColorKind
{
    Named,
    Hex,
    Rgb,
    Rgba,
    Theme
}

public sealed record SketchColorValue(SketchColorKind Kind, string Value);

public sealed record LaidOutSketchGroup(
    string Id,
    string Label,
    ResolvedSketchStyle Style,
    double X,
    double Y,
    double Width,
    double Height);

public sealed record LaidOutSketchDiagram(
    SketchLayoutDirection Direction,
    IReadOnlyList<LaidOutSketchNode> Nodes,
    IReadOnlyList<LaidOutSketchEdge> Edges,
    IReadOnlyList<LaidOutSketchGroup> Groups,
    SketchBounds Bounds,
    IReadOnlyList<SketchDiagnostic> Diagnostics);

public sealed record LaidOutSketchNode(
    string Id,
    string Label,
    IReadOnlyList<string> LabelLines,
    ResolvedSketchStyle Style,
    double X,
    double Y,
    double Width,
    double Height);

public sealed record LaidOutSketchEdge(
    string Id,
    string SourceId,
    string TargetId,
    SketchEdgeDirection Direction,
    string? Label,
    ResolvedSketchStyle Style,
    double X1,
    double Y1,
    double X2,
    double Y2);

public sealed record SketchBounds(double Width, double Height);

public sealed record SketchSvgRenderResult(
    string Svg,
    SketchBounds Bounds,
    IReadOnlyList<SketchDiagnostic> Diagnostics);
