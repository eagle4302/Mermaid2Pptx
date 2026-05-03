using System.Xml.Linq;

namespace Mermaid2Pptx;

public sealed class PptxXmlAudit
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public PptxXmlAuditReport Compare(SvgScene scene, PptxSlideModel slide, string slideXml)
    {
        var document = XDocument.Parse(slideXml);
        var xmlShapes = ReadXmlShapes(document).ToDictionary(shape => shape.Name, StringComparer.Ordinal);
        var primaryShapes = slide.Shapes
            .Where(shape => !shape.Name.StartsWith("Marker ", StringComparison.Ordinal))
            .ToArray();
        var items = new List<PptxXmlAuditItem>();

        for (var i = 0; i < scene.Elements.Count; i++)
        {
            var source = SvgAuditSource.From(i, scene.Elements[i]);
            var shape = i < primaryShapes.Length ? primaryShapes[i] : null;
            var target = shape is not null && xmlShapes.TryGetValue(shape.Name, out var xmlShape)
                ? CreateTarget(xmlShape)
                : null;
            var failures = Validate(shape, target).ToArray();
            items.Add(new PptxXmlAuditItem(
                source,
                shape?.Name,
                shape?.Kind.ToString(),
                shape is not null ? ExpectedXmlKind(shape) : null,
                shape is not null && ExpectedDash(shape),
                shape is not null && ExpectedHeadEnd(shape),
                shape is not null && ExpectedTailEnd(shape),
                target,
                failures.Length == 0,
                failures));
        }

        return new PptxXmlAuditReport(
            scene.Elements.Count,
            items.Count(item => item.PptxShapeName is not null),
            items.Count(item => item.Passed),
            items.Count(item => !item.Passed),
            items);
    }

    private static IReadOnlyList<XmlShapeInfo> ReadXmlShapes(XDocument document)
    {
        var shapeTree = document.Descendants(P + "spTree").FirstOrDefault();
        if (shapeTree is null)
        {
            return [];
        }

        return shapeTree.Elements()
            .Where(element => element.Name == P + "sp" || element.Name == P + "cxnSp")
            .Select(ReadXmlShape)
            .ToArray();
    }

    private static XmlShapeInfo ReadXmlShape(XElement element)
    {
        var nonVisual = element.Descendants(P + "cNvPr").FirstOrDefault();
        var transform = element.Descendants(A + "xfrm").FirstOrDefault();
        var line = element.Descendants(A + "ln").FirstOrDefault();
        var dash = line?.Descendants(A + "prstDash").FirstOrDefault();
        var geometry = element.Descendants(A + "prstGeom").FirstOrDefault();

        return new XmlShapeInfo(
            element.Name == P + "cxnSp" ? "p:cxnSp" : "p:sp",
            nonVisual?.Attribute("id")?.Value ?? string.Empty,
            nonVisual?.Attribute("name")?.Value ?? string.Empty,
            IsTrue(transform?.Attribute("flipH")?.Value),
            IsTrue(transform?.Attribute("flipV")?.Value),
            LongAttribute(transform?.Element(A + "off"), "x"),
            LongAttribute(transform?.Element(A + "off"), "y"),
            LongAttribute(transform?.Element(A + "ext"), "cx"),
            LongAttribute(transform?.Element(A + "ext"), "cy"),
            geometry?.Attribute("prst")?.Value,
            element.Descendants(A + "custGeom").Any(),
            dash?.Attribute("val")?.Value,
            line?.Descendants(A + "headEnd").Any() == true,
            line?.Descendants(A + "tailEnd").Any() == true);
    }

    private static IEnumerable<string> Validate(PptxShape? shape, PptxAuditTarget? target)
    {
        if (shape is null)
        {
            yield return "No mapped PptxShape for SVG element.";
            yield break;
        }

        if (target is null)
        {
            yield return $"No slide XML shape named '{shape.Name}'.";
            yield break;
        }

        if (!string.Equals(target.XmlElement, ExpectedXmlKind(shape), StringComparison.Ordinal))
        {
            yield return $"Expected {ExpectedXmlKind(shape)} but found {target.XmlElement}.";
        }

        if (shape.Kind == PptxShapeKind.Line)
        {
            if (!string.Equals(target.PresetGeometry, "line", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"Expected line preset geometry but found '{target.PresetGeometry}'.";
            }

            if (target.FlipH != shape.FlipH)
            {
                yield return $"Expected flipH={shape.FlipH} but found {target.FlipH}.";
            }

            if (target.FlipV != shape.FlipV)
            {
                yield return $"Expected flipV={shape.FlipV} but found {target.FlipV}.";
            }
        }

        if (shape.Kind == PptxShapeKind.Custom && !target.HasCustomGeometry)
        {
            yield return "Expected custom geometry.";
        }

        if (ExpectedDash(shape) != target.HasDash)
        {
            yield return $"Expected dash={ExpectedDash(shape)} but found {target.HasDash}.";
        }

        if (ExpectedHeadEnd(shape) != target.HasHeadEnd)
        {
            yield return $"Expected headEnd={ExpectedHeadEnd(shape)} but found {target.HasHeadEnd}.";
        }

        if (ExpectedTailEnd(shape) != target.HasTailEnd)
        {
            yield return $"Expected tailEnd={ExpectedTailEnd(shape)} but found {target.HasTailEnd}.";
        }
    }

    private static string ExpectedXmlKind(PptxShape shape) =>
        shape.Kind == PptxShapeKind.Line ? "p:cxnSp" : "p:sp";

    private static bool ExpectedDash(PptxShape shape) =>
        HasVisibleDash(shape.Style.StrokeDashArray);

    private static bool HasVisibleDash(string? dashArray)
    {
        if (string.IsNullOrWhiteSpace(dashArray))
        {
            return false;
        }

        var trimmed = dashArray.Trim();
        if (trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            trimmed is "0" or "0 0")
        {
            return false;
        }

        var values = trimmed
            .Replace(',', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => SvgNumber.Parse(value, double.NaN))
            .Where(value => !double.IsNaN(value))
            .ToArray();
        return values.Length > 0 && values.Any(value => value > 0);
    }

    private static bool ExpectedHeadEnd(PptxShape shape) => shape.ArrowStart;

    private static bool ExpectedTailEnd(PptxShape shape) => shape.ArrowEnd;

    private static long LongAttribute(XElement? element, string name) =>
        long.TryParse(element?.Attribute(name)?.Value, out var value) ? value : 0;

    private static bool IsTrue(string? value) =>
        value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static PptxAuditTarget CreateTarget(XmlShapeInfo xml) =>
        new(
            xml.XmlElement,
            xml.Id,
            xml.Name,
            xml.FlipH,
            xml.FlipV,
            xml.X,
            xml.Y,
            xml.Cx,
            xml.Cy,
            xml.PresetGeometry,
            xml.HasCustomGeometry,
            !string.IsNullOrWhiteSpace(xml.DashPreset),
            xml.DashPreset,
            xml.HasHeadEnd,
            xml.HasTailEnd);

    private sealed record XmlShapeInfo(
        string XmlElement,
        string Id,
        string Name,
        bool FlipH,
        bool FlipV,
        long X,
        long Y,
        long Cx,
        long Cy,
        string? PresetGeometry,
        bool HasCustomGeometry,
        string? DashPreset,
        bool HasHeadEnd,
        bool HasTailEnd);

    public sealed record PptxXmlAuditReport(
        int SvgElementCount,
        int ComparedCount,
        int PassedCount,
        int FailureCount,
        IReadOnlyList<PptxXmlAuditItem> Items);

    public sealed record PptxXmlAuditItem(
        SvgAuditSource Svg,
        string? PptxShapeName,
        string? PptxShapeKind,
        string? ExpectedXmlElement,
        bool ExpectedDash,
        bool ExpectedHeadEnd,
        bool ExpectedTailEnd,
        PptxAuditTarget? Pptx,
        bool Passed,
        IReadOnlyList<string> Failures);

    public sealed record SvgAuditSource(
        int Index,
        string Tag,
        string? ElementId,
        IReadOnlyList<string> Classes,
        string? Stroke,
        string? Fill,
        string? StrokeDashArray,
        string? MarkerStart,
        string? MarkerEnd,
        double? X1,
        double? Y1,
        double? X2,
        double? Y2)
    {
        public static SvgAuditSource From(int index, SvgElement element) =>
            element is SvgLineElement line
                ? new SvgAuditSource(
                    index,
                    element.TagName,
                    element.ElementId,
                    element.Classes.ToArray(),
                    element.Style.Stroke,
                    element.Style.Fill,
                    element.Style.StrokeDashArray,
                    element.Style.MarkerStart,
                    element.Style.MarkerEnd,
                    line.X1,
                    line.Y1,
                    line.X2,
                    line.Y2)
                : new SvgAuditSource(
                    index,
                    element.TagName,
                    element.ElementId,
                    element.Classes.ToArray(),
                    element.Style.Stroke,
                    element.Style.Fill,
                    element.Style.StrokeDashArray,
                    element.Style.MarkerStart,
                    element.Style.MarkerEnd,
                    null,
                    null,
                    null,
                    null);
    }

    public sealed record PptxAuditTarget(
        string XmlElement,
        string Id,
        string Name,
        bool FlipH,
        bool FlipV,
        long X,
        long Y,
        long Cx,
        long Cy,
        string? PresetGeometry,
        bool HasCustomGeometry,
        bool HasDash,
        string? DashPreset,
        bool HasHeadEnd,
        bool HasTailEnd);
}
