namespace Mermaid2Pptx;

public enum FlowchartNodeKind
{
    Rectangle,
    Rounded,
    Stadium,
    Diamond,
    Circle,
    Cylinder,
    Hexagon,
    Parallelogram,
    Subroutine
}

public sealed class FlowchartNode
{
    public required string Id { get; init; }
    public string Label { get; set; } = string.Empty;
    public FlowchartNodeKind Kind { get; set; } = FlowchartNodeKind.Rectangle;
    public string? Fill { get; set; }
    public string? Stroke { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public sealed class FlowchartEdge
{
    public required string SourceId { get; init; }
    public required string TargetId { get; init; }
    public string Label { get; set; } = string.Empty;
    public bool Dashed { get; set; }
    public bool Arrow { get; set; } = true;
}

public sealed class FlowchartGraph
{
    public string Title { get; set; } = "Page-1";
    public string Direction { get; set; } = "TD";
    public List<FlowchartNode> Nodes { get; } = [];
    public List<FlowchartEdge> Edges { get; } = [];
    public List<string> Warnings { get; } = [];

    public FlowchartNode GetOrAddNode(string id)
    {
        var existing = Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }

        var created = new FlowchartNode { Id = id, Label = id };
        Nodes.Add(created);
        return created;
    }
}
