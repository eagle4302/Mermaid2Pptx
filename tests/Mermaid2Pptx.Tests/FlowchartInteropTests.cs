using System.Xml.Linq;
using Xunit;

namespace Mermaid2Pptx.Tests;

public sealed class FlowchartInteropTests
{
    private const string SampleMermaid = """
flowchart LR
  A([Start]) --> B{Decision}
  B -- Yes --> C[Native PowerPoint shapes]
  B -- No --> D[Warning report]
  C --> E([Done])
  D --> E
""";

    [Fact]
    public void Parses_mermaid_flowchart_shapes_labels_and_chains()
    {
        var graph = new MermaidFlowchartParser().Parse(SampleMermaid);

        Assert.Equal("LR", graph.Direction);
        Assert.Equal(5, graph.Nodes.Count);
        Assert.Equal(FlowchartNodeKind.Stadium, graph.Nodes.Single(node => node.Id == "A").Kind);
        Assert.Equal("Start", graph.Nodes.Single(node => node.Id == "A").Label);
        Assert.Equal(FlowchartNodeKind.Diamond, graph.Nodes.Single(node => node.Id == "B").Kind);
        Assert.Equal(FlowchartNodeKind.Rectangle, graph.Nodes.Single(node => node.Id == "C").Kind);
        Assert.Contains(graph.Edges, edge => edge.SourceId == "B" && edge.TargetId == "C" && edge.Label == "Yes");
        Assert.Contains(graph.Edges, edge => edge.SourceId == "D" && edge.TargetId == "E");
        Assert.Equal(5, graph.Edges.Count);
    }

    [Fact]
    public void Parses_pipe_and_dashed_edge_labels()
    {
        var graph = new MermaidFlowchartParser().Parse("""
flowchart TD
  A[Start] -->|Go| B(Next)
  B -.-> C{{Prep}}
  C --- D[(Disk)]
""");

        Assert.Equal(FlowchartNodeKind.Rounded, graph.Nodes.Single(node => node.Id == "B").Kind);
        Assert.Equal(FlowchartNodeKind.Hexagon, graph.Nodes.Single(node => node.Id == "C").Kind);
        Assert.Equal(FlowchartNodeKind.Cylinder, graph.Nodes.Single(node => node.Id == "D").Kind);
        Assert.Contains(graph.Edges, edge => edge.Label == "Go" && edge.Arrow);
        Assert.Contains(graph.Edges, edge => edge.SourceId == "B" && edge.Dashed && edge.Arrow);
        Assert.Contains(graph.Edges, edge => edge.SourceId == "C" && !edge.Arrow);
    }

    [Fact]
    public void Rejects_non_flowchart_mermaid()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new MermaidFlowchartParser().Parse("sequenceDiagram\n  A->>B: hi"));
        Assert.Contains("flowchart/graph", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mermaid_round_trips_through_drawio()
    {
        var drawIo = FlowchartInterop.MermaidToDrawIo(SampleMermaid);
        Assert.Contains("<mxfile", drawIo, StringComparison.Ordinal);
        Assert.Contains("rhombus", drawIo, StringComparison.Ordinal);
        Assert.Contains("ellipse", drawIo, StringComparison.Ordinal);
        Assert.Contains("Start", drawIo, StringComparison.Ordinal);
        Assert.Contains("source=\"A\"", drawIo, StringComparison.Ordinal);
        Assert.Contains("target=\"B\"", drawIo, StringComparison.Ordinal);

        var mermaid = FlowchartInterop.DrawIoToMermaid(drawIo);
        var graph = new MermaidFlowchartParser().Parse(mermaid);
        Assert.Equal(5, graph.Nodes.Count);
        Assert.Equal(5, graph.Edges.Count);
        Assert.Contains(graph.Nodes, node => node.Kind == FlowchartNodeKind.Diamond && node.Label == "Decision");
        Assert.Contains(graph.Nodes, node => node.Kind == FlowchartNodeKind.Stadium && node.Label == "Start");
        Assert.Contains(graph.Edges, edge => edge.Label == "Yes");
        Assert.Contains(graph.Edges, edge => edge.Label == "No");
        Assert.Contains("flowchart LR", mermaid, StringComparison.Ordinal);
    }

    [Fact]
    public void Drawio_sample_converts_to_mermaid()
    {
        var xml = File.ReadAllText(FindRepoPath("samples/flowchart.drawio"));
        var mermaid = FlowchartInterop.DrawIoToMermaid(xml);
        var graph = new MermaidFlowchartParser().Parse(mermaid);

        Assert.Contains(graph.Nodes, node => node.Label == "Start");
        Assert.Contains(graph.Nodes, node => node.Label == "Approved?");
        Assert.Contains(graph.Nodes, node => node.Label == "Native PPTX shapes");
        Assert.Contains(graph.Edges, edge => edge.Label == "Yes");
        Assert.Contains(graph.Edges, edge => edge.Dashed);
        Assert.Contains(graph.Nodes, node => node.Kind == FlowchartNodeKind.Diamond);
    }

    [Fact]
    public void Drawio_round_trips_through_mermaid_preserving_connectivity()
    {
        var xml = File.ReadAllText(FindRepoPath("samples/flowchart.drawio"));
        var mermaid = FlowchartInterop.DrawIoToMermaid(xml);
        var roundTrip = FlowchartInterop.MermaidToDrawIo(mermaid);
        var graph = new DrawIoDocumentParser().ParseFlowcharts(roundTrip).Single();

        Assert.Equal(4, graph.Nodes.Count);
        Assert.Equal(3, graph.Edges.Count);
        Assert.Contains(graph.Nodes, node => node.Label == "Start");
        Assert.Contains(graph.Edges, edge => edge.Label == "Yes");
        Assert.Contains(
            XDocument.Parse(roundTrip).Descendants(),
            element => element.Name.LocalName == "mxCell");
    }

    [Fact]
    public void Infers_drawio_and_mermaid_output_formats()
    {
        var drawIo = CliOptions.Parse(["--mermaid", "flowchart LR; A-->B", "--out", "diagram.drawio"]);
        Assert.Equal(OutputFormat.DrawIo, drawIo.ResolveOutputFormat());

        var mermaid = CliOptions.Parse(["--drawio-file", "in.drawio", "--out", "diagram.mmd"]);
        Assert.Equal(OutputFormat.Mermaid, mermaid.ResolveOutputFormat());

        var explicitTo = CliOptions.Parse(["--mermaid", "flowchart LR; A-->B", "--to", "drawio", "--out", "out.bin"]);
        Assert.Equal(OutputFormat.DrawIo, explicitTo.ResolveOutputFormat());
    }

    private static string FindRepoPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}' from {AppContext.BaseDirectory}.");
    }
}
