namespace Mnemo.Core.Sketch;

public sealed class SketchCompiler
{
    private readonly SketchResolver _resolver = new();
    private readonly SketchLayoutEngine _layoutEngine = new();
    private readonly SketchSvgRenderer _svgRenderer = new();

    public SketchParseResult Parse(string source)
    {
        var lexer = new SketchLexer(source);
        var (tokens, diagnostics) = lexer.Lex();
        return new SketchParser(tokens, diagnostics).Parse();
    }

    public ResolvedSketchDiagram Resolve(string source) => _resolver.Resolve(Parse(source));

    public LaidOutSketchDiagram Layout(string source) => _layoutEngine.Layout(Resolve(source));

    public SketchSvgRenderResult CompileToSvg(string source) => _svgRenderer.Render(Layout(source));
}
