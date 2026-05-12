using Mnemo.Core.Sketch;

namespace Mnemo.Infrastructure.Tests;

public class SketchCompilerTests
{
    [Fact]
    public void Resolve_CreatesImplicitNodesAndLabeledEdge()
    {
        var diagram = new SketchCompiler().Resolve("""
            A -> B
            B -> C : depends on
            """);

        Assert.DoesNotContain(diagram.Diagnostics, d => d.Severity == SketchDiagnosticSeverity.Error);
        Assert.Equal(new[] { "A", "B", "C" }, diagram.Nodes.Select(n => n.Id).ToArray());
        Assert.Equal(2, diagram.Edges.Count);
        Assert.Equal("depends on", diagram.Edges[1].Label);
    }

    [Fact]
    public void CompileToSvg_RendersNodesEdgesAndEscapesLabels()
    {
        var result = new SketchCompiler().CompileToSvg("""
            A -> B : "uses <api>"
            """);

        Assert.Contains("<svg", result.Svg);
        Assert.Contains("node:A", result.Svg);
        Assert.Contains("node:B", result.Svg);
        Assert.Contains("uses &lt;api&gt;", result.Svg);
    }

    [Fact]
    public void Layout_SizesAndWrapsLongNodeLabels()
    {
        var result = new SketchCompiler().CompileToSvg("""
            NodeA "This label is long enough to wrap inside the node"
            NodeB "Short"
            NodeA -> NodeB
            """);

        var diagram = new SketchCompiler().Layout("""
            NodeA "This label is long enough to wrap inside the node"
            NodeB "Short"
            NodeA -> NodeB
            """);
        var nodeA = diagram.Nodes.Single(n => n.Id == "NodeA");
        var nodeB = diagram.Nodes.Single(n => n.Id == "NodeB");

        Assert.True(nodeA.Width > nodeB.Width);
        Assert.True(nodeA.Height > nodeB.Height);
        Assert.True(nodeA.LabelLines.Count > 1);
        Assert.Contains("This label is long enough", result.Svg);
        Assert.Contains("wrap inside the node", result.Svg);
    }

    [Fact]
    public void Resolve_AllowsBracketNodesLabelsCommentsAndPropertyBlocks()
    {
        var diagram = new SketchCompiler().Resolve("""
            # comment
            sketch {
              title: "Auth"
            }
            [api] "Public API" {
              fill: blue-100
            }
            [db] "Database"
            [api] -> [db] : queries {
              stroke: blue-700
            }
            """);

        Assert.DoesNotContain(diagram.Diagnostics, d => d.Severity == SketchDiagnosticSeverity.Error);
        Assert.Equal(new[] { "api", "db" }, diagram.Nodes.Select(n => n.Id).ToArray());
        Assert.Equal("Public API", diagram.Nodes[0].Label);
        Assert.Equal("queries", diagram.Edges.Single().Label);
    }

    [Fact]
    public void CompileToSvg_AppliesClassAndInlineStyles()
    {
        var result = new SketchCompiler().CompileToSvg("""
            class service {
              shape: rounded-rect
              fill: blue-100
              stroke: blue-700
            }

            [api] "Public API" {
              class: service
            }
            [db] "Database" {
              fill: green-100
              stroke: green-700
            }
            [api] -> [db] : queries {
              stroke: red-700
              stroke-width: 3
            }
            """);

        Assert.Contains("fill=\"#dbeafe\"", result.Svg);
        Assert.Contains("stroke=\"#1d4ed8\"", result.Svg);
        Assert.Contains("fill=\"#dcfce7\"", result.Svg);
        Assert.Contains("stroke=\"#15803d\"", result.Svg);
        Assert.Contains("stroke=\"#b91c1c\" stroke-width=\"3\"", result.Svg);
        Assert.Contains("fill=\"#b91c1c\"", result.Svg);
    }

    [Fact]
    public void CompileToSvg_SupportsHexNamedAndThemeColors()
    {
        var result = new SketchCompiler().CompileToSvg("""
            [api] "API" {
              fill: hex(#123abc)
              stroke: blue
            }
            [worker] "Worker" {
              fill: purple
              stroke: theme(swatch1)
            }
            [api] -> [worker] : calls {
              stroke: rgba(20,184,166,0.5)
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == SketchDiagnosticSeverity.Error);
        Assert.Contains("fill=\"#123abc\"", result.Svg);
        Assert.Contains("stroke=\"#3b82f6\"", result.Svg);
        Assert.Contains("fill=\"#a855f7\"", result.Svg);
        Assert.Contains("stroke=\"theme(swatch1)\"", result.Svg);
        Assert.Contains("stroke=\"rgba(20,184,166,0.5)\"", result.Svg);
    }

    [Fact]
    public void CompileToSvg_TreatsRawHashAsCommentInPropertyBlocks()
    {
        var result = new SketchCompiler().CompileToSvg("""
            [api] "API" {
              fill: blue # this is a comment
              stroke: hex(#123abc)
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == SketchDiagnosticSeverity.Error);
        Assert.Contains("fill=\"#3b82f6\"", result.Svg);
        Assert.Contains("stroke=\"#123abc\"", result.Svg);
    }
}
