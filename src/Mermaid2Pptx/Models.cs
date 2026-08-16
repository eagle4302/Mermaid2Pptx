using System.Globalization;

namespace Mermaid2Pptx;

public sealed record ExtractedSvg(
    int SourceSlideIndex,
    int SvgIndex,
    string SvgMarkup,
    double? ContainerX = null,
    double? ContainerY = null,
    double? ContainerWidth = null,
    double? ContainerHeight = null,
    double? SlideWidth = null,
    double? SlideHeight = null);

public readonly record struct SvgPoint(double X, double Y)
{
    public static SvgPoint operator +(SvgPoint left, SvgPoint right) => new(left.X + right.X, left.Y + right.Y);
    public static SvgPoint operator -(SvgPoint left, SvgPoint right) => new(left.X - right.X, left.Y - right.Y);
    public static SvgPoint operator *(SvgPoint point, double scalar) => new(point.X * scalar, point.Y * scalar);
}

public readonly record struct SvgRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public static SvgRect FromPoints(IEnumerable<SvgPoint> points)
    {
        var list = points.ToList();
        if (list.Count == 0)
        {
            return new SvgRect(0, 0, 1, 1);
        }

        var minX = list.Min(p => p.X);
        var minY = list.Min(p => p.Y);
        var maxX = list.Max(p => p.X);
        var maxY = list.Max(p => p.Y);
        return new SvgRect(minX, minY, Math.Max(0.01, maxX - minX), Math.Max(0.01, maxY - minY));
    }
}

public readonly record struct AffineMatrix(double A, double B, double C, double D, double E, double F)
{
    public static AffineMatrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public SvgPoint Transform(SvgPoint point) =>
        new(A * point.X + C * point.Y + E, B * point.X + D * point.Y + F);

    public AffineMatrix Multiply(AffineMatrix other) =>
        new(
            A * other.A + C * other.B,
            B * other.A + D * other.B,
            A * other.C + C * other.D,
            B * other.C + D * other.D,
            A * other.E + C * other.F + E,
            B * other.E + D * other.F + F);
}

public sealed class SvgScene
{
    public double Width { get; init; }
    public double Height { get; init; }
    public SvgRect ViewBox { get; init; }
    public List<SvgElement> Elements { get; } = [];
    public Dictionary<string, SvgMarker> Markers { get; } = new(StringComparer.Ordinal);
    public List<string> Warnings { get; } = [];
}

public sealed record SvgMarker(
    string Id,
    double RefX,
    double RefY,
    double MarkerWidth,
    double MarkerHeight,
    SvgRect ViewBox,
    IReadOnlyList<SvgElement> Elements);

public sealed record SvgStyle
{
    public string? Fill { get; init; } = "black";
    public string? Stroke { get; init; } = "none";
    public double StrokeWidth { get; init; } = 1;
    public string? StrokeDashArray { get; init; }
    public double Opacity { get; init; } = 1;
    public double FillOpacity { get; init; } = 1;
    public double StrokeOpacity { get; init; } = 1;
    public string? Color { get; init; } = "black";
    public string? BackgroundColor { get; init; }
    public string? FontFamily { get; init; } = "Arial";
    public double FontSize { get; init; } = 16;
    public string? FontWeight { get; init; } = "normal";
    public string? FontStyle { get; init; } = "normal";
    public string? TextAnchor { get; init; } = "start";
    public string? TextAlign { get; init; }
    public string? Display { get; init; } = "inline";
    public string? Visibility { get; init; } = "visible";
    public string? MarkerStart { get; init; }
    public string? MarkerEnd { get; init; }

    public static SvgStyle Default { get; } = new();

    public bool IsVisible =>
        !StringEquals(Display, "none") &&
        !StringEquals(Visibility, "hidden") &&
        Opacity > 0;

    public SvgStyle Merge(IReadOnlyDictionary<string, string> declarations)
    {
        var style = this;
        foreach (var (key, raw) in declarations)
        {
            var value = raw.Trim();
            style = key.Trim().ToLowerInvariant() switch
            {
                "fill" => style with { Fill = value },
                "stroke" => style with { Stroke = value },
                "stroke-width" => style with { StrokeWidth = SvgNumber.Parse(value, style.StrokeWidth) },
                "stroke-dasharray" => style with { StrokeDashArray = value },
                "opacity" => style with { Opacity = Clamp01(SvgNumber.Parse(value, style.Opacity)) },
                "fill-opacity" => style with { FillOpacity = Clamp01(SvgNumber.Parse(value, style.FillOpacity)) },
                "stroke-opacity" => style with { StrokeOpacity = Clamp01(SvgNumber.Parse(value, style.StrokeOpacity)) },
                "color" => style with { Color = value },
                "background" => style with { BackgroundColor = value },
                "background-color" => style with { BackgroundColor = value },
                "font-family" => style with { FontFamily = value.Trim('\'', '"') },
                "font-size" => style with { FontSize = SvgNumber.Parse(value, style.FontSize) },
                "font-weight" => style with { FontWeight = value },
                "font-style" => style with { FontStyle = value },
                "text-anchor" => style with { TextAnchor = value },
                "text-align" => style with { TextAlign = value },
                "display" => style with { Display = value },
                "visibility" => style with { Visibility = value },
                "marker-start" => style with { MarkerStart = value },
                "marker-end" => style with { MarkerEnd = value },
                _ => style
            };
        }

        return style;
    }

    private static bool StringEquals(string? left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));
}

public abstract class SvgElement
{
    public required string TagName { get; init; }
    public string? ElementId { get; init; }
    public IReadOnlyList<string> Classes { get; init; } = [];
    public required SvgStyle Style { get; init; }
    public required AffineMatrix Transform { get; init; }
}

public sealed class SvgRectElement : SvgElement
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double Rx { get; init; }
    public double Ry { get; init; }
}

public sealed class SvgEllipseElement : SvgElement
{
    public double Cx { get; init; }
    public double Cy { get; init; }
    public double Rx { get; init; }
    public double Ry { get; init; }
}

public sealed class SvgLineElement : SvgElement
{
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
}

public sealed class SvgPolylineElement : SvgElement
{
    public required IReadOnlyList<SvgPoint> Points { get; init; }
    public bool Closed { get; init; }
}

public sealed class SvgPathElement : SvgElement
{
    public required IReadOnlyList<SvgPathSegment> Segments { get; init; }
}

public sealed class SvgTextElement : SvgElement
{
    public required IReadOnlyList<SvgTextLine> Lines { get; init; }
    public SvgRect? TextBox { get; init; }
}

public sealed class SvgPresetShapeElement : SvgElement
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public required string PresetGeometry { get; init; }
    public int? PresetAdjustValue { get; init; }
}

public sealed record SvgTextLine(double X, double Y, string Text);

public abstract record SvgPathSegment;
public sealed record MoveTo(SvgPoint Point) : SvgPathSegment;
public sealed record LineTo(SvgPoint Point) : SvgPathSegment;
public sealed record CubicBezierTo(SvgPoint Control1, SvgPoint Control2, SvgPoint Point) : SvgPathSegment;
public sealed record QuadraticBezierTo(SvgPoint Control, SvgPoint Point) : SvgPathSegment;
public sealed record ClosePath : SvgPathSegment;

public static class SvgNumber
{
    public static double Parse(string? value, double fallback = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        var end = 0;
        while (end < trimmed.Length &&
               (char.IsDigit(trimmed[end]) ||
                trimmed[end] is '.' or '-' or '+' or 'e' or 'E'))
        {
            end++;
        }

        if (end == 0)
        {
            return fallback;
        }

        return double.TryParse(trimmed[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;
    }
}
