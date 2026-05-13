namespace Mermaid2Pptx;

public sealed class PptxDeckModel
{
    public double WidthInches { get; init; } = 13.333;
    public double HeightInches { get; init; } = 7.5;
    public List<PptxSlideModel> Slides { get; } = [];
    public List<string> Warnings { get; } = [];
}

public sealed class PptxSlideModel
{
    public required long WidthEmu { get; init; }
    public required long HeightEmu { get; init; }
    public List<PptxShape> Shapes { get; } = [];
    public List<string> Warnings { get; } = [];
}

public readonly record struct PptPoint(long X, long Y);

public sealed class PptxShape
{
    public int Id { get; set; }
    public required string Name { get; init; }
    public required PptxShapeKind Kind { get; init; }
    public long X { get; set; }
    public long Y { get; set; }
    public long Cx { get; set; }
    public long Cy { get; set; }
    public string? PresetGeometry { get; init; }
    public int? PresetAdjustValue { get; init; }
    public IReadOnlyList<PptxPathCommand> PathCommands { get; init; } = [];
    public long PathWidth { get; init; }
    public long PathHeight { get; init; }
    public SvgStyle Style { get; init; } = SvgStyle.Default;
    public long LineWidthEmu { get; init; }
    public string? Text { get; init; }
    public string TextAlignment { get; init; } = "left";
    public bool NoWrapText { get; init; }
    public bool PreferNoFill { get; init; }
    public bool PreferNoLine { get; init; }
    public bool ArrowStart { get; set; }
    public bool ArrowEnd { get; set; }
    public bool FlipH { get; init; }
    public bool FlipV { get; init; }
}

public enum PptxShapeKind
{
    Preset,
    Custom,
    Line,
    Text
}

public abstract record PptxPathCommand;
public sealed record PptxMoveTo(PptPoint Point) : PptxPathCommand;
public sealed record PptxLineTo(PptPoint Point) : PptxPathCommand;
public sealed record PptxCubicBezierTo(PptPoint Control1, PptPoint Control2, PptPoint Point) : PptxPathCommand;
public sealed record PptxQuadraticBezierTo(PptPoint Control, PptPoint Point) : PptxPathCommand;
public sealed record PptxClosePath : PptxPathCommand;
