using System.Globalization;
using System.Net;
using System.Text;

namespace Mermaid2Pptx;

public static class DrawIoFlowchartWriter
{
    public static string Write(IReadOnlyList<FlowchartGraph> graphs)
    {
        var pages = new StringBuilder();
        for (var index = 0; index < graphs.Count; index++)
        {
            var graph = graphs[index];
            FlowchartLayout.AssignIfMissing(graph);
            pages.Append(DiagramXml(graph, index + 1));
        }

        return $"""
<mxfile host="Mermaid2Pptx" type="device">
{pages}</mxfile>
""";
    }

    public static string Write(FlowchartGraph graph) => Write([graph]);

    private static string DiagramXml(FlowchartGraph graph, int pageNumber)
    {
        var cells = new StringBuilder();
        cells.AppendLine("""        <mxCell id="0"/>""");
        cells.AppendLine("""        <mxCell id="1" parent="0"/>""");
        foreach (var node in graph.Nodes)
        {
            cells.AppendLine(VertexXml(node));
        }

        var edgeIndex = 1;
        foreach (var edge in graph.Edges)
        {
            cells.AppendLine(EdgeXml(edge, edgeIndex, graph.Direction));
            edgeIndex++;
        }

        var name = string.IsNullOrWhiteSpace(graph.Title) ? $"Page-{pageNumber}" : graph.Title;
        return $"""
  <diagram id="page-{pageNumber.ToString(CultureInfo.InvariantCulture)}" name="{Esc(name)}">
    <mxGraphModel dx="1200" dy="800" grid="1" gridSize="10" page="1" pageScale="1" pageWidth="1169" pageHeight="827">
      <root>
{cells.ToString().TrimEnd()}
      </root>
    </mxGraphModel>
  </diagram>

""";
    }

    private static string VertexXml(FlowchartNode node)
    {
        var style = NodeStyle(node);
        var value = Esc(node.Label);
        return $"""
        <mxCell id="{Esc(node.Id)}" value="{value}" style="{style}" vertex="1" parent="1">
          <mxGeometry x="{N(node.X)}" y="{N(node.Y)}" width="{N(Math.Max(1, node.Width))}" height="{N(Math.Max(1, node.Height))}" as="geometry"/>
        </mxCell>
""";
    }

    private static string EdgeXml(FlowchartEdge edge, int index, string direction)
    {
        var (exitX, exitY, entryX, entryY) = Ports(direction);
        var style = new StringBuilder("html=1;");
        style.Append(edge.Arrow ? "endArrow=classic;" : "endArrow=none;");
        if (edge.Dashed)
        {
            style.Append("dashed=1;dashPattern=8 8;");
        }

        style.Append(CultureInfo.InvariantCulture, $"exitX={exitX};exitY={exitY};entryX={entryX};entryY={entryY};");
        var id = "e" + index.ToString(CultureInfo.InvariantCulture);
        var valueAttr = string.IsNullOrWhiteSpace(edge.Label)
            ? " "
            : $""" value="{Esc(edge.Label)}" """;
        return $"""
        <mxCell id="{id}"{valueAttr}style="{style}" edge="1" parent="1" source="{Esc(edge.SourceId)}" target="{Esc(edge.TargetId)}">
          <mxGeometry relative="1" as="geometry"/>
        </mxCell>
""";
    }

    private static (string ExitX, string ExitY, string EntryX, string EntryY) Ports(string direction) =>
        direction switch
        {
            "LR" => ("1", "0.5", "0", "0.5"),
            "RL" => ("0", "0.5", "1", "0.5"),
            "BT" => ("0.5", "0", "0.5", "1"),
            _ => ("0.5", "1", "0.5", "0")
        };

    private static string NodeStyle(FlowchartNode node)
    {
        var fill = node.Fill ?? DefaultFill(node.Kind);
        var stroke = node.Stroke ?? DefaultStroke(node.Kind);
        var shape = node.Kind switch
        {
            FlowchartNodeKind.Diamond => "rhombus;whiteSpace=wrap;html=1;",
            FlowchartNodeKind.Stadium or FlowchartNodeKind.Circle => "ellipse;whiteSpace=wrap;html=1;",
            FlowchartNodeKind.Rounded => "rounded=1;whiteSpace=wrap;html=1;",
            FlowchartNodeKind.Hexagon => "hexagon;whiteSpace=wrap;html=1;",
            FlowchartNodeKind.Parallelogram => "shape=parallelogram;whiteSpace=wrap;html=1;",
            FlowchartNodeKind.Cylinder => "shape=cylinder;whiteSpace=wrap;html=1;",
            FlowchartNodeKind.Subroutine => "shape=mxgraph.flowchart.predefined_process;whiteSpace=wrap;html=1;",
            _ => "rounded=0;whiteSpace=wrap;html=1;"
        };
        return shape + "fillColor=" + fill + ";strokeColor=" + stroke + ";";
    }

    private static string DefaultFill(FlowchartNodeKind kind) => kind switch
    {
        FlowchartNodeKind.Diamond => "#fff2cc",
        FlowchartNodeKind.Stadium or FlowchartNodeKind.Circle => "#d5e8d4",
        _ => "#dae8fc"
    };

    private static string DefaultStroke(FlowchartNodeKind kind) => kind switch
    {
        FlowchartNodeKind.Diamond => "#d6b656",
        FlowchartNodeKind.Stadium or FlowchartNodeKind.Circle => "#82b366",
        _ => "#6c8ebf"
    };

    private static string Esc(string value) => WebUtility.HtmlEncode(value);

    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

public static class FlowchartLayout
{
    public static void AssignIfMissing(FlowchartGraph graph)
    {
        if (graph.Nodes.Count == 0 || graph.Nodes.Any(node => node.Width > 0 && node.Height > 0 && (node.X != 0 || node.Y != 0 || graph.Nodes.Count == 1)))
        {
            foreach (var node in graph.Nodes)
            {
                EnsureSize(node);
            }

            if (graph.Nodes.All(node => node.Width > 0 && node.Height > 0) &&
                (graph.Nodes.Count == 1 || graph.Nodes.Any(node => node.X != 0 || node.Y != 0)))
            {
                return;
            }
        }

        Assign(graph);
    }

    public static void Assign(FlowchartGraph graph)
    {
        foreach (var node in graph.Nodes)
        {
            EnsureSize(node);
        }

        var incoming = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            if (incoming.ContainsKey(edge.TargetId))
            {
                incoming[edge.TargetId]++;
            }
        }

        var roots = graph.Nodes.Where(node => incoming[node.Id] == 0).Select(node => node.Id).ToList();
        if (roots.Count == 0 && graph.Nodes.Count > 0)
        {
            roots.Add(graph.Nodes[0].Id);
        }

        var layer = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new Queue<string>(roots);
        foreach (var root in roots)
        {
            layer[root] = 0;
        }

        var outgoing = graph.Edges
            .GroupBy(edge => edge.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.TargetId).Distinct().ToList(), StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!outgoing.TryGetValue(id, out var next))
            {
                continue;
            }

            foreach (var target in next)
            {
                var nextLayer = layer[id] + 1;
                if (!layer.TryGetValue(target, out var existing) || nextLayer > existing)
                {
                    layer[target] = nextLayer;
                    queue.Enqueue(target);
                }
            }
        }

        foreach (var node in graph.Nodes)
        {
            layer.TryAdd(node.Id, 0);
        }

        var groups = graph.Nodes
            .GroupBy(node => layer[node.Id])
            .OrderBy(group => group.Key)
            .Select(group => group.ToList())
            .ToList();

        const double origin = 40;
        const double layerGap = 110;
        const double nodeGap = 36;
        var horizontal = graph.Direction is "LR" or "RL";
        var reverse = graph.Direction is "RL" or "BT";
        if (reverse)
        {
            groups.Reverse();
        }

        double cursor = origin;
        foreach (var group in groups)
        {
            var span = horizontal
                ? group.Sum(node => node.Height) + nodeGap * Math.Max(0, group.Count - 1)
                : group.Sum(node => node.Width) + nodeGap * Math.Max(0, group.Count - 1);
            var start = origin;
            var offset = start;
            var layerThickness = horizontal ? group.Max(node => node.Width) : group.Max(node => node.Height);
            foreach (var node in group)
            {
                if (horizontal)
                {
                    node.X = cursor;
                    node.Y = offset;
                    offset += node.Height + nodeGap;
                }
                else
                {
                    node.X = offset;
                    node.Y = cursor;
                    offset += node.Width + nodeGap;
                }
            }

            _ = span;
            cursor += layerThickness + layerGap;
        }
    }

    private static void EnsureSize(FlowchartNode node)
    {
        var labelWidth = Math.Max(24, node.Label.Length * 8d + 36);
        if (node.Width <= 0 || node.Height <= 0)
        {
            (node.Width, node.Height) = node.Kind switch
            {
                FlowchartNodeKind.Diamond => (Math.Max(140, labelWidth), 80),
                FlowchartNodeKind.Stadium => (Math.Max(140, labelWidth), 60),
                FlowchartNodeKind.Circle => (80, 80),
                FlowchartNodeKind.Cylinder => (Math.Max(120, labelWidth), 80),
                FlowchartNodeKind.Hexagon => (Math.Max(140, labelWidth), 60),
                _ => (Math.Max(120, labelWidth), 50)
            };
        }
    }
}

public static class FlowchartInterop
{
    public static string MermaidToDrawIo(string mermaid)
    {
        var graphs = new MermaidFlowchartParser().ParseAll(mermaid);
        foreach (var graph in graphs)
        {
            foreach (var node in graph.Nodes)
            {
                node.X = 0;
                node.Y = 0;
                node.Width = 0;
                node.Height = 0;
            }

            FlowchartLayout.Assign(graph);
        }

        return DrawIoFlowchartWriter.Write(graphs);
    }

    public static string DrawIoToMermaid(string drawIoXml)
    {
        var graphs = new DrawIoDocumentParser().ParseFlowcharts(drawIoXml);
        if (graphs.All(graph => graph.Nodes.Count == 0))
        {
            throw new InvalidOperationException("Draw.io document did not contain flowchart nodes.");
        }

        return MermaidFlowchartWriter.Write(graphs.Where(graph => graph.Nodes.Count > 0).ToArray());
    }
}
