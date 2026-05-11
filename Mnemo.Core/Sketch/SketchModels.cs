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
    Number,
    Boolean,
    Colon,
    Comma,
    LeftBracket,
    RightBracket,
    LeftBrace,
    RightBrace,
    ArrowDirected,
    KeywordSketch,
    KeywordClass,
    KeywordGroup,
    KeywordEdge,
    Comment,
    Newline,
    EndOfFile,
    Invalid
}

public sealed record SketchToken(SketchTokenKind Kind, string Text, string Value, SourceSpan Span);

public sealed record RawSketchAst(IReadOnlyList<RawSketchStatement> Statements, SourceSpan Span);

public abstract record RawSketchStatement(SourceSpan Span);

public sealed record RawSketchEdgeDecl(
    RawSketchNodeRef Source,
    RawSketchNodeRef Target,
    string? Label,
    SourceSpan Span) : RawSketchStatement(Span);

public sealed record RawSketchNodeDecl(
    RawSketchNodeRef Ref,
    string? Label,
    SourceSpan Span) : RawSketchStatement(Span);

public sealed record RawSketchComment(string Text, SourceSpan Span) : RawSketchStatement(Span);

public sealed record RawSketchIgnoredStatement(string Kind, SourceSpan Span) : RawSketchStatement(Span);

public sealed record RawSketchNodeRef(string Id, string Label, bool IsBracketId, SourceSpan Span);

public sealed record SketchParseResult(
    RawSketchAst Ast,
    IReadOnlyList<SketchToken> Tokens,
    IReadOnlyList<SketchDiagnostic> Diagnostics);

public sealed record ResolvedSketchDiagram(
    int Version,
    IReadOnlyList<ResolvedSketchNode> Nodes,
    IReadOnlyList<ResolvedSketchEdge> Edges,
    IReadOnlyList<SketchDiagnostic> Diagnostics);

public sealed record ResolvedSketchNode(
    string Id,
    string Label,
    bool Declared,
    bool Implicit,
    IReadOnlyList<SourceSpan> SourceSpans);

public sealed record ResolvedSketchEdge(
    string Id,
    string SourceId,
    string TargetId,
    string? Label,
    SourceSpan SourceSpan);

public sealed record LaidOutSketchDiagram(
    IReadOnlyList<LaidOutSketchNode> Nodes,
    IReadOnlyList<LaidOutSketchEdge> Edges,
    SketchBounds Bounds,
    IReadOnlyList<SketchDiagnostic> Diagnostics);

public sealed record LaidOutSketchNode(
    string Id,
    string Label,
    IReadOnlyList<string> LabelLines,
    double X,
    double Y,
    double Width,
    double Height);

public sealed record LaidOutSketchEdge(
    string Id,
    string SourceId,
    string TargetId,
    string? Label,
    double X1,
    double Y1,
    double X2,
    double Y2);

public sealed record SketchBounds(double Width, double Height);

public sealed record SketchSvgRenderResult(
    string Svg,
    SketchBounds Bounds,
    IReadOnlyList<SketchDiagnostic> Diagnostics);
