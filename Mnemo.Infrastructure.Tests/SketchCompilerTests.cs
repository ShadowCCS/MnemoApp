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
}
