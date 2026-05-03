using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Mermaid2Pptx;

public sealed class SvgDocumentParser
{
    private static readonly Regex HtmlVoidElementRegex = new(
        @"<(?<name>area|base|br|col|embed|hr|img|input|link|meta|param|source|track|wbr)(?<attrs>\s[^<>]*?)?>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly SvgStyleResolver _styleResolver = new();
    private readonly SvgPathParser _pathParser = new();

    public SvgScene Parse(string svgMarkup)
    {
        var document = XDocument.Parse(NormalizeBrowserSvgMarkup(svgMarkup), LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new InvalidOperationException("SVG XML has no root element.");
        if (!root.Name.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Input XML root element must be <svg>.");
        }

        var viewBox = ParseViewBox(root);
        var width = SvgNumber.Parse(root.Attribute("width")?.Value, viewBox.Width > 0 ? viewBox.Width : 800);
        var height = SvgNumber.Parse(root.Attribute("height")?.Value, viewBox.Height > 0 ? viewBox.Height : 600);
        if (viewBox.Width <= 0 || viewBox.Height <= 0)
        {
            viewBox = new SvgRect(0, 0, width, height);
        }

        var scene = new SvgScene
        {
            Width = width,
            Height = height,
            ViewBox = viewBox
        };

        var rules = _styleResolver.ParseStyleRules(root);
        var rootStyle = _styleResolver.Resolve(root, SvgStyle.Default, [], rules);
        var rootTransform = SvgTransformResolver.Parse(root.Attribute("transform")?.Value);
        var rootAncestors = new[] { SvgNodeInfo.From(root) };
        ParseDefinitions(root, rootStyle, rootAncestors, rules, scene);

        foreach (var child in root.Elements())
        {
            Traverse(child, rootStyle, rootTransform, rootAncestors, rules, scene);
        }

        return scene;
    }

    private static string NormalizeBrowserSvgMarkup(string svgMarkup)
    {
        var normalized = svgMarkup.Replace("&nbsp;", "&#160;", StringComparison.OrdinalIgnoreCase);
        return HtmlVoidElementRegex.Replace(normalized, match =>
        {
            var attrs = match.Groups["attrs"].Value;
            return attrs.TrimEnd().EndsWith("/", StringComparison.Ordinal)
                ? match.Value
                : $"<{match.Groups["name"].Value}{attrs} />";
        });
    }

    private void Traverse(
        XElement element,
        SvgStyle inheritedStyle,
        AffineMatrix parentTransform,
        IReadOnlyList<SvgNodeInfo> ancestors,
        IReadOnlyList<CssRule> rules,
        SvgScene scene)
    {
        var tag = element.Name.LocalName;
        if (tag.Equals("defs", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("marker", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("style", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("metadata", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("title", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("desc", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var style = _styleResolver.Resolve(element, inheritedStyle, ancestors, rules);
        var transform = parentTransform.Multiply(SvgTransformResolver.Parse(element.Attribute("transform")?.Value));
        var currentInfo = SvgNodeInfo.From(element);
        var nextAncestors = ancestors.Concat([currentInfo]).ToArray();

        if (!style.IsVisible)
        {
            return;
        }

        switch (tag.ToLowerInvariant())
        {
            case "g":
            case "a":
            case "switch":
                foreach (var child in element.Elements())
                {
                    Traverse(child, style, transform, nextAncestors, rules, scene);
                }
                break;

            case "svg":
                var nestedTransform = transform.Multiply(NestedSvgViewportTransform(element));
                foreach (var child in element.Elements())
                {
                    Traverse(child, style, nestedTransform, nextAncestors, rules, scene);
                }
                break;

            case "rect":
                AddRect(element, style, transform, scene);
                break;

            case "circle":
                AddCircle(element, style, transform, scene);
                break;

            case "ellipse":
                AddEllipse(element, style, transform, scene);
                break;

            case "line":
                AddLine(element, style, transform, scene);
                break;

            case "polyline":
                AddPolyline(element, style, transform, false, scene);
                break;

            case "polygon":
                AddPolyline(element, style, transform, true, scene);
                break;

            case "path":
                AddPath(element, style, transform, scene);
                break;

            case "text":
                AddText(element, style, transform, scene);
                break;

            case "foreignobject":
                AddForeignObjectText(element, style, transform, nextAncestors, rules, scene);
                break;

            case "tspan":
                break;

            default:
                scene.Warnings.Add($"Unsupported SVG element <{tag}>; attempting child conversion only.");
                foreach (var child in element.Elements())
                {
                    Traverse(child, style, transform, nextAncestors, rules, scene);
                }
                break;
        }
    }

    private static void AddRect(XElement element, SvgStyle style, AffineMatrix transform, SvgScene scene)
    {
        var width = SvgNumber.Parse(element.Attribute("width")?.Value);
        var height = SvgNumber.Parse(element.Attribute("height")?.Value);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        scene.Elements.Add(new SvgRectElement
        {
            TagName = "rect",
            ElementId = element.Attribute("id")?.Value,
            Classes = Classes(element),
            Style = style,
            Transform = transform,
            X = SvgNumber.Parse(element.Attribute("x")?.Value),
            Y = SvgNumber.Parse(element.Attribute("y")?.Value),
            Width = width,
            Height = height,
            Rx = SvgNumber.Parse(element.Attribute("rx")?.Value),
            Ry = SvgNumber.Parse(element.Attribute("ry")?.Value)
        });
    }

    private static void AddCircle(XElement element, SvgStyle style, AffineMatrix transform, SvgScene scene)
    {
        var radius = SvgNumber.Parse(element.Attribute("r")?.Value);
        if (radius <= 0)
        {
            return;
        }

        scene.Elements.Add(new SvgEllipseElement
        {
            TagName = "circle",
            ElementId = element.Attribute("id")?.Value,
            Classes = Classes(element),
            Style = style,
            Transform = transform,
            Cx = SvgNumber.Parse(element.Attribute("cx")?.Value),
            Cy = SvgNumber.Parse(element.Attribute("cy")?.Value),
            Rx = radius,
            Ry = radius
        });
    }

    private static void AddEllipse(XElement element, SvgStyle style, AffineMatrix transform, SvgScene scene)
    {
        var rx = SvgNumber.Parse(element.Attribute("rx")?.Value);
        var ry = SvgNumber.Parse(element.Attribute("ry")?.Value);
        if (rx <= 0 || ry <= 0)
        {
            return;
        }

        scene.Elements.Add(new SvgEllipseElement
        {
            TagName = "ellipse",
            ElementId = element.Attribute("id")?.Value,
            Classes = Classes(element),
            Style = style,
            Transform = transform,
            Cx = SvgNumber.Parse(element.Attribute("cx")?.Value),
            Cy = SvgNumber.Parse(element.Attribute("cy")?.Value),
            Rx = rx,
            Ry = ry
        });
    }

    private static void AddLine(XElement element, SvgStyle style, AffineMatrix transform, SvgScene scene)
    {
        scene.Elements.Add(new SvgLineElement
        {
            TagName = "line",
            ElementId = element.Attribute("id")?.Value,
            Classes = Classes(element),
            Style = style,
            Transform = transform,
            X1 = SvgNumber.Parse(element.Attribute("x1")?.Value),
            Y1 = SvgNumber.Parse(element.Attribute("y1")?.Value),
            X2 = SvgNumber.Parse(element.Attribute("x2")?.Value),
            Y2 = SvgNumber.Parse(element.Attribute("y2")?.Value)
        });
    }

    private static void AddPolyline(XElement element, SvgStyle style, AffineMatrix transform, bool closed, SvgScene scene)
    {
        var points = ParsePoints(element.Attribute("points")?.Value).ToArray();
        if (points.Length == 0)
        {
            return;
        }

        scene.Elements.Add(new SvgPolylineElement
        {
            TagName = closed ? "polygon" : "polyline",
            ElementId = element.Attribute("id")?.Value,
            Classes = Classes(element),
            Style = style,
            Transform = transform,
            Points = points,
            Closed = closed
        });
    }

    private void AddPath(XElement element, SvgStyle style, AffineMatrix transform, SvgScene scene)
    {
        var data = element.Attribute("d")?.Value;
        if (string.IsNullOrWhiteSpace(data))
        {
            return;
        }

        try
        {
            scene.Elements.Add(new SvgPathElement
            {
                TagName = "path",
                ElementId = element.Attribute("id")?.Value,
                Classes = Classes(element),
                Style = style,
                Transform = transform,
                Segments = _pathParser.Parse(data)
            });
        }
        catch (FormatException exception)
        {
            scene.Warnings.Add($"Unable to parse path '{element.Attribute("id")?.Value ?? "(no id)"}': {exception.Message}");
        }
    }

    private static void AddText(XElement element, SvgStyle style, AffineMatrix transform, SvgScene scene)
    {
        var lines = ReadTextLines(element, style).Where(line => !string.IsNullOrWhiteSpace(line.Text)).ToArray();
        if (lines.Length == 0)
        {
            return;
        }

        scene.Elements.Add(new SvgTextElement
        {
            TagName = "text",
            ElementId = element.Attribute("id")?.Value,
            Classes = Classes(element),
            Style = style,
            Transform = transform,
            Lines = lines
        });
    }

    private void AddForeignObjectText(
        XElement element,
        SvgStyle style,
        AffineMatrix transform,
        IReadOnlyList<SvgNodeInfo> ancestors,
        IReadOnlyList<CssRule> rules,
        SvgScene scene)
    {
        var runs = new List<(string Text, SvgStyle Style)>();
        CollectForeignObjectTextRuns(element, style, ancestors.SkipLast(1).ToArray(), rules, runs);
        var textLines = NormalizeForeignObjectTextLines(runs.Select(run => run.Text)).ToArray();
        if (textLines.Length == 0)
        {
            var width = SvgNumber.Parse(element.Attribute("width")?.Value);
            var height = SvgNumber.Parse(element.Attribute("height")?.Value);
            if (width > 0 || height > 0)
            {
                scene.Warnings.Add("Unsupported foreignObject without extractable text.");
            }
            return;
        }

        var textStyle = runs.FirstOrDefault(run => !string.IsNullOrWhiteSpace(run.Text)).Style ?? style;
        var x = SvgNumber.Parse(element.Attribute("x")?.Value);
        var y = SvgNumber.Parse(element.Attribute("y")?.Value);
        scene.Elements.Add(new SvgTextElement
        {
            TagName = "foreignObject",
            ElementId = element.Attribute("id")?.Value,
            Classes = Classes(element),
            Style = textStyle,
            Transform = transform,
            Lines = textLines
                .Select((line, index) => new SvgTextLine(x, y + textStyle.FontSize + index * textStyle.FontSize * 1.2, line))
                .ToArray(),
            TextBox = new SvgRect(
                x,
                y,
                Math.Max(1, SvgNumber.Parse(element.Attribute("width")?.Value, textLines.Max(line => line.Length) * textStyle.FontSize * 0.62)),
                Math.Max(1, SvgNumber.Parse(element.Attribute("height")?.Value, textLines.Length * textStyle.FontSize * 1.25)))
        });
    }

    private void CollectForeignObjectTextRuns(
        XElement element,
        SvgStyle inheritedStyle,
        IReadOnlyList<SvgNodeInfo> ancestors,
        IReadOnlyList<CssRule> rules,
        List<(string Text, SvgStyle Style)> runs)
    {
        var style = _styleResolver.Resolve(element, inheritedStyle, ancestors, rules);
        var currentAncestors = ancestors.Concat([SvgNodeInfo.From(element)]).ToArray();
        if (element.Name.LocalName.Equals("br", StringComparison.OrdinalIgnoreCase))
        {
            runs.Add(("\n", style));
            return;
        }

        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    var normalized = NormalizeText(text.Value);
                    if (!string.IsNullOrWhiteSpace(normalized))
                    {
                        runs.Add((normalized, style));
                    }
                    break;
                case XElement child:
                    CollectForeignObjectTextRuns(child, style, currentAncestors, rules, runs);
                    break;
            }
        }
    }

    private static IEnumerable<string> NormalizeForeignObjectTextLines(IEnumerable<string> runs)
    {
        var text = string.Join(' ', runs);
        foreach (var line in text.Split('\n'))
        {
            var normalized = NormalizeText(line);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                yield return normalized;
            }
        }
    }

    private static IEnumerable<SvgTextLine> ReadTextLines(XElement textElement, SvgStyle style)
    {
        var baseX = ParseTextLength(textElement.Attribute("x")?.Value, 0, style);
        var baseY = ParseTextLength(textElement.Attribute("y")?.Value, 0, style);
        var currentX = baseX + ParseTextOffset(textElement.Attribute("dx")?.Value, style);
        var currentY = baseY + ParseTextOffset(textElement.Attribute("dy")?.Value, style);

        var directText = string.Concat(textElement.Nodes().OfType<XText>().Select(t => t.Value));
        if (!string.IsNullOrWhiteSpace(directText))
        {
            yield return new SvgTextLine(currentX, currentY, NormalizeText(directText));
        }

        foreach (var tspan in textElement.Elements().Where(e => e.Name.LocalName.Equals("tspan", StringComparison.OrdinalIgnoreCase)))
        {
            currentX = ParseTextLength(tspan.Attribute("x")?.Value, currentX, style);
            currentY = ParseTextLength(tspan.Attribute("y")?.Value, currentY, style);
            currentX += ParseTextOffset(tspan.Attribute("dx")?.Value, style);
            currentY += ParseTextOffset(tspan.Attribute("dy")?.Value, style);
            yield return new SvgTextLine(currentX, currentY, NormalizeText(tspan.Value));
        }
    }

    private static double ParseTextOffset(string? value, SvgStyle style)
    {
        return ParseTextLength(value, 0, style);
    }

    private static double ParseTextLength(string? value, double fallback, SvgStyle style)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        if (trimmed.EndsWith("em", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith("rem", StringComparison.OrdinalIgnoreCase))
        {
            var unitLength = trimmed.EndsWith("rem", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
            return SvgNumber.Parse(trimmed[..^unitLength]) * style.FontSize;
        }

        if (trimmed.EndsWith('%'))
        {
            return SvgNumber.Parse(trimmed[..^1]) * style.FontSize / 100d;
        }

        return SvgNumber.Parse(trimmed, fallback);
    }

    private static IEnumerable<SvgPoint> ParsePoints(string? points)
    {
        if (string.IsNullOrWhiteSpace(points))
        {
            yield break;
        }

        var normalized = points.Replace(',', ' ');
        var values = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => SvgNumber.Parse(value, double.NaN))
            .Where(value => !double.IsNaN(value))
            .ToArray();

        for (var i = 0; i + 1 < values.Length; i += 2)
        {
            yield return new SvgPoint(values[i], values[i + 1]);
        }
    }

    private static SvgRect ParseViewBox(XElement root)
    {
        var values = (root.Attribute("viewBox")?.Value ?? string.Empty)
            .Replace(',', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => SvgNumber.Parse(value, double.NaN))
            .Where(value => !double.IsNaN(value))
            .ToArray();

        return values.Length >= 4
            ? new SvgRect(values[0], values[1], values[2], values[3])
            : new SvgRect(0, 0, 0, 0);
    }

    private static IReadOnlyList<string> Classes(XElement element) =>
        (element.Attribute("class")?.Value ?? string.Empty)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string NormalizeText(string text) =>
        string.Join(' ', text.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private void ParseDefinitions(
        XElement root,
        SvgStyle rootStyle,
        IReadOnlyList<SvgNodeInfo> rootAncestors,
        IReadOnlyList<CssRule> rules,
        SvgScene scene)
    {
        foreach (var marker in root.Descendants().Where(e => e.Name.LocalName.Equals("marker", StringComparison.OrdinalIgnoreCase)))
        {
            var id = marker.Attribute("id")?.Value;
            if (!string.IsNullOrWhiteSpace(id))
            {
                var markerScene = new SvgScene
                {
                    Width = SvgNumber.Parse(marker.Attribute("markerWidth")?.Value),
                    Height = SvgNumber.Parse(marker.Attribute("markerHeight")?.Value),
                    ViewBox = ParseViewBox(marker)
                };
                var defsAncestors = rootAncestors.Concat([SvgNodeInfo.From(marker.Parent ?? root)]).ToArray();
                var markerStyle = _styleResolver.Resolve(marker, rootStyle, defsAncestors, rules);
                var markerAncestors = defsAncestors.Concat([SvgNodeInfo.From(marker)]).ToArray();
                var markerTransform = SvgTransformResolver.Parse(marker.Attribute("transform")?.Value);

                foreach (var child in marker.Elements())
                {
                    Traverse(child, markerStyle, markerTransform, markerAncestors, rules, markerScene);
                }

                scene.Markers[id] = new SvgMarker(
                    id,
                    SvgNumber.Parse(marker.Attribute("refX")?.Value),
                    SvgNumber.Parse(marker.Attribute("refY")?.Value),
                    SvgNumber.Parse(marker.Attribute("markerWidth")?.Value),
                    SvgNumber.Parse(marker.Attribute("markerHeight")?.Value),
                    markerScene.ViewBox,
                    markerScene.Elements.ToArray());
            }
        }
    }

    private static AffineMatrix NestedSvgViewportTransform(XElement element)
    {
        var viewBox = ParseViewBox(element);
        if (viewBox.Width <= 0 || viewBox.Height <= 0)
        {
            return AffineMatrix.Identity;
        }

        var width = SvgNumber.Parse(element.Attribute("width")?.Value, viewBox.Width);
        var height = SvgNumber.Parse(element.Attribute("height")?.Value, viewBox.Height);
        if (width <= 0 || height <= 0)
        {
            return AffineMatrix.Identity;
        }

        var x = SvgNumber.Parse(element.Attribute("x")?.Value);
        var y = SvgNumber.Parse(element.Attribute("y")?.Value);
        return new AffineMatrix(
            width / viewBox.Width,
            0,
            0,
            height / viewBox.Height,
            x - viewBox.X * width / viewBox.Width,
            y - viewBox.Y * height / viewBox.Height);
    }
}
