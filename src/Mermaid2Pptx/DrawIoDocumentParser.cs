using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Mermaid2Pptx;

public sealed class DrawIoDocumentParser
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    public static bool LooksLikeDrawIo(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return trimmed.Contains("<mxfile", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Contains("<mxGraphModel", StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<SvgScene> Parse(string markup)
    {
        if (string.IsNullOrWhiteSpace(markup))
        {
            throw new ArgumentException("Draw.io markup is required.", nameof(markup));
        }

        var document = ParseXmlDocument(markup);
        var diagrams = document.Descendants()
            .Where(element => LocalName(element) == "diagram")
            .ToList();

        if (diagrams.Count > 0)
        {
            return diagrams.Select(ParseDiagramElement).ToArray();
        }

        var model = document.Descendants().FirstOrDefault(element => LocalName(element) == "mxGraphModel")
            ?? throw new InvalidOperationException("No mxGraphModel was found in the draw.io document.");
        return [ParseGraphModel(model)];
    }

    private static XDocument ParseXmlDocument(string markup)
    {
        var trimmed = markup.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        try
        {
            return XDocument.Parse(trimmed, LoadOptions.PreserveWhitespace);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Draw.io XML could not be parsed.", exception);
        }
    }

    private SvgScene ParseDiagramElement(XElement diagram)
    {
        var model = diagram.Elements().FirstOrDefault(element => LocalName(element) == "mxGraphModel");
        if (model is not null)
        {
            return ParseGraphModel(model);
        }

        var compressed = diagram.Value.Trim();
        if (string.IsNullOrWhiteSpace(compressed))
        {
            var empty = EmptyScene();
            empty.Warnings.Add("Draw.io diagram page is empty.");
            return empty;
        }

        var xml = DecompressDiagram(compressed);
        var nested = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var nestedModel = nested.Descendants().FirstOrDefault(element => LocalName(element) == "mxGraphModel")
            ?? throw new InvalidOperationException("Decompressed draw.io diagram did not contain mxGraphModel.");
        return ParseGraphModel(nestedModel);
    }

    private SvgScene ParseGraphModel(XElement model)
    {
        var scene = EmptyScene();
        var cells = ReadCells(model);
        if (cells.Count == 0)
        {
            scene.Warnings.Add("Draw.io page has no cells.");
            return scene;
        }

        var lookup = cells
            .Where(cell => !string.IsNullOrWhiteSpace(cell.Id))
            .GroupBy(cell => cell.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var cell in cells)
        {
            try
            {
                AddCell(scene, cell, lookup);
            }
            catch (Exception exception)
            {
                scene.Warnings.Add($"Skipped draw.io cell '{cell.Id}': {exception.Message}");
            }
        }

        var viewBox = SceneBounds(scene);
        var fitted = new SvgScene
        {
            Width = viewBox.Width,
            Height = viewBox.Height,
            ViewBox = viewBox
        };
        fitted.Elements.AddRange(scene.Elements);
        fitted.Warnings.AddRange(scene.Warnings);
        return fitted;
    }

    private static List<DrawIoCell> ReadCells(XElement model)
    {
        var cells = new List<DrawIoCell>();
        foreach (var element in model.Descendants().Where(candidate => LocalName(candidate) == "mxCell"))
        {
            var cell = ReadCell(element);
            if (cell is not null)
            {
                cells.Add(cell);
            }
        }

        return cells;
    }

    private static DrawIoCell? ReadCell(XElement element)
    {
        var host = element.Parent is not null &&
                   LocalName(element.Parent) is "object" or "UserObject"
            ? element.Parent
            : null;
        var id = Attr(element, "id") ?? Attr(host, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var geometry = element.Elements().FirstOrDefault(candidate => LocalName(candidate) == "mxGeometry");
        var style = ParseStyle(Attr(element, "style"));
        var value = Attr(element, "value") ??
                    Attr(host, "label") ??
                    Attr(host, "name") ??
                    string.Empty;

        return new DrawIoCell
        {
            Id = id,
            ParentId = Attr(element, "parent") ?? string.Empty,
            SourceId = Attr(element, "source"),
            TargetId = Attr(element, "target"),
            Value = value,
            Style = style,
            Vertex = IsFlag(element, "vertex"),
            Edge = IsFlag(element, "edge"),
            Visible = !style.TryGetValue("visible", out var visible) || visible != "0",
            X = Number(Attr(geometry, "x")),
            Y = Number(Attr(geometry, "y")),
            Width = Number(Attr(geometry, "width"), 0),
            Height = Number(Attr(geometry, "height"), 0),
            Relative = Attr(geometry, "relative") == "1",
            SourcePoint = NamedPoint(geometry, "sourcePoint"),
            TargetPoint = NamedPoint(geometry, "targetPoint"),
            Offset = NamedPoint(geometry, "offset"),
            Waypoints = ReadWaypoints(geometry)
        };
    }

    private static void AddCell(SvgScene scene, DrawIoCell cell, IReadOnlyDictionary<string, DrawIoCell> lookup)
    {
        if (!cell.Visible || (!cell.Vertex && !cell.Edge))
        {
            return;
        }

        if (cell.Edge)
        {
            AddEdge(scene, cell, lookup);
            return;
        }

        if (IsGroup(cell) && string.IsNullOrWhiteSpace(cell.Value) && !HasExplicitFill(cell.Style))
        {
            return;
        }

        if (lookup.TryGetValue(cell.ParentId, out var parent) && parent.Edge)
        {
            AddEdgeLabel(scene, cell, parent, lookup);
            return;
        }

        if (IsTextOnly(cell))
        {
            var textBounds = AbsoluteBounds(cell, lookup);
            AddLabel(scene, cell, textBounds, fillShape: false);
            return;
        }

        var bounds = AbsoluteBounds(cell, lookup);
        var (preset, warning) = ResolvePreset(cell.Style, bounds);
        if (warning is not null)
        {
            scene.Warnings.Add(warning);
        }

        var style = VertexStyle(cell.Style);
        scene.Elements.Add(new SvgPresetShapeElement
        {
            TagName = preset,
            ElementId = cell.Id,
            Style = style,
            Transform = AffineMatrix.Identity,
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height,
            PresetGeometry = preset,
            PresetAdjustValue = RoundRectAdjust(preset, cell.Style, bounds)
        });

        AddLabel(scene, cell, bounds, fillShape: true);
    }

    private static void AddEdge(SvgScene scene, DrawIoCell cell, IReadOnlyDictionary<string, DrawIoCell> lookup)
    {
        var points = EdgePoints(cell, lookup);
        if (points.Count < 2)
        {
            scene.Warnings.Add($"Draw.io edge '{cell.Id}' has no usable endpoints.");
            return;
        }

        var style = EdgeStyle(cell.Style);
        if (points.Count == 2)
        {
            scene.Elements.Add(new SvgLineElement
            {
                TagName = "line",
                ElementId = cell.Id,
                Style = style,
                Transform = AffineMatrix.Identity,
                X1 = points[0].X,
                Y1 = points[0].Y,
                X2 = points[1].X,
                Y2 = points[1].Y
            });
        }
        else
        {
            scene.Elements.Add(new SvgPolylineElement
            {
                TagName = "polyline",
                ElementId = cell.Id,
                Style = style,
                Transform = AffineMatrix.Identity,
                Points = points,
                Closed = false
            });
        }

        if (!string.IsNullOrWhiteSpace(cell.Value))
        {
            var midpoint = PointAlong(points, 0.5) + (cell.Offset ?? new SvgPoint(0, 0));
            AddFreeLabel(scene, cell, midpoint);
        }
    }

    private static void AddEdgeLabel(
        SvgScene scene,
        DrawIoCell label,
        DrawIoCell edge,
        IReadOnlyDictionary<string, DrawIoCell> lookup)
    {
        if (string.IsNullOrWhiteSpace(label.Value))
        {
            return;
        }

        var points = EdgePoints(edge, lookup);
        if (points.Count < 2)
        {
            return;
        }

        var along = label.Relative ? Math.Clamp(label.X + 0.5, 0, 1) : 0.5;
        var position = PointAlong(points, along) + (label.Offset ?? new SvgPoint(0, 0));
        AddFreeLabel(scene, label, position);
    }

    private static void AddLabel(SvgScene scene, DrawIoCell cell, SvgRect bounds, bool fillShape)
    {
        var text = PlainText(cell.Value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var style = LabelStyle(cell.Style, fillShape);
        var lines = SplitLines(text, bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        scene.Elements.Add(new SvgTextElement
        {
            TagName = "text",
            ElementId = string.IsNullOrWhiteSpace(cell.Id) ? null : cell.Id + "-label",
            Style = style,
            Transform = AffineMatrix.Identity,
            Lines = lines,
            TextBox = bounds
        });
    }

    private static void AddFreeLabel(SvgScene scene, DrawIoCell cell, SvgPoint position)
    {
        var text = PlainText(cell.Value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var style = LabelStyle(cell.Style, fillShape: false) with
        {
            Fill = "none",
            Stroke = "none",
            TextAlign = "center",
            TextAnchor = "middle"
        };
        var width = Math.Max(24, text.Length * Math.Max(8, style.FontSize) * 0.6);
        var height = Math.Max(style.FontSize * 1.4, SplitLines(text, 0, 0).Count * style.FontSize * 1.25);
        var box = new SvgRect(position.X - width / 2, position.Y - height / 2, width, height);
        scene.Elements.Add(new SvgTextElement
        {
            TagName = "text",
            ElementId = cell.Id,
            Style = style,
            Transform = AffineMatrix.Identity,
            Lines = SplitLines(text, position.X, position.Y),
            TextBox = box
        });
    }

    private static List<SvgPoint> EdgePoints(DrawIoCell edge, IReadOnlyDictionary<string, DrawIoCell> lookup)
    {
        var origin = ParentOrigin(edge.ParentId, lookup, visiting: []);
        var points = new List<SvgPoint>();

        if (!string.IsNullOrWhiteSpace(edge.SourceId) &&
            lookup.TryGetValue(edge.SourceId, out var source))
        {
            points.Add(PortPoint(source, lookup, edge.Style, "exitX", "exitY", "exitDx", "exitDy"));
        }
        else if (edge.SourcePoint is { } sourcePoint)
        {
            points.Add(sourcePoint + origin);
        }

        foreach (var waypoint in edge.Waypoints)
        {
            points.Add(waypoint + origin);
        }

        if (!string.IsNullOrWhiteSpace(edge.TargetId) &&
            lookup.TryGetValue(edge.TargetId, out var target))
        {
            points.Add(PortPoint(target, lookup, edge.Style, "entryX", "entryY", "entryDx", "entryDy"));
        }
        else if (edge.TargetPoint is { } targetPoint)
        {
            points.Add(targetPoint + origin);
        }

        return points;
    }

    private static SvgPoint PortPoint(
        DrawIoCell vertex,
        IReadOnlyDictionary<string, DrawIoCell> lookup,
        IReadOnlyDictionary<string, string> style,
        string xKey,
        string yKey,
        string dxKey,
        string dyKey)
    {
        var bounds = AbsoluteBounds(vertex, lookup);
        var xRatio = StyleNumber(style, xKey, 0.5);
        var yRatio = StyleNumber(style, yKey, 0.5);
        var dx = StyleNumber(style, dxKey, 0);
        var dy = StyleNumber(style, dyKey, 0);
        return new SvgPoint(bounds.X + bounds.Width * xRatio + dx, bounds.Y + bounds.Height * yRatio + dy);
    }

    private static SvgRect AbsoluteBounds(DrawIoCell cell, IReadOnlyDictionary<string, DrawIoCell> lookup)
    {
        var origin = ParentOrigin(cell.ParentId, lookup, []);
        return new SvgRect(
            origin.X + cell.X,
            origin.Y + cell.Y,
            Math.Max(0.01, cell.Width),
            Math.Max(0.01, cell.Height));
    }

    private static SvgPoint ParentOrigin(
        string parentId,
        IReadOnlyDictionary<string, DrawIoCell> lookup,
        HashSet<string> visiting)
    {
        if (string.IsNullOrWhiteSpace(parentId) || parentId is "0" or "1")
        {
            return new SvgPoint(0, 0);
        }

        if (!lookup.TryGetValue(parentId, out var parent) || parent.Edge)
        {
            return new SvgPoint(0, 0);
        }

        if (!visiting.Add(parent.Id))
        {
            return new SvgPoint(0, 0);
        }

        return ParentOrigin(parent.ParentId, lookup, visiting) + new SvgPoint(parent.X, parent.Y);
    }

    private static (string Preset, string? Warning) ResolvePreset(
        IReadOnlyDictionary<string, string> style,
        SvgRect bounds)
    {
        var raw = style.TryGetValue("shape", out var shape) ? shape.Trim() : string.Empty;
        var name = NormalizeShapeName(raw);

        if (name.Length == 0)
        {
            return IsTrue(style, "rounded") ? ("roundRect", null) : ("rect", null);
        }

        var mapped = name switch
        {
            "rectangle" or "rect" or "process" => IsTrue(style, "rounded") ? "roundRect" : "rect",
            "ellipse" or "oval" or "connector" or "onpageconnector" or "on_page_connector" => "ellipse",
            "rhombus" or "diamond" => "diamond",
            "decision" => "flowChartDecision",
            "parallelogram" => "parallelogram",
            "data" or "inputoutput" or "input_output" => "flowChartInputOutput",
            "hexagon" => "hexagon",
            "preparation" => "flowChartPreparation",
            "triangle" => "triangle",
            "terminator" => "flowChartTerminator",
            "document" => "flowChartDocument",
            "multidocument" or "multi_document" => "flowChartMultidocument",
            "predefinedprocess" or "predefined_process" => "flowChartPredefinedProcess",
            "internalstorage" or "internal_storage" => "flowChartInternalStorage",
            "manualinput" or "manual_input" => "flowChartManualInput",
            "manualoperation" or "manual_operation" => "flowChartManualOperation",
            "delay" => "flowChartDelay",
            "display" => "flowChartDisplay",
            "offpageconnector" or "off_page_connector" => "flowChartOffpageConnector",
            "or" => "flowChartOr",
            "summingjunction" or "summing_junction" => "flowChartSummingJunction",
            "collate" => "flowChartCollate",
            "sort" => "flowChartSort",
            "merge" => "flowChartMerge",
            "extract" => "flowChartExtract",
            "storeddata" or "stored_data" or "onlinestorage" => "flowChartOnlineStorage",
            "directdata" or "direct_data" or "disk" => "flowChartMagneticDisk",
            "card" => "foldedCorner",
            "cylinder" or "database" or "can" => "can",
            "cloud" => "cloud",
            "step" or "chevron" => "chevron",
            "cube" => "cube",
            "plus" => "plus",
            "roundedrectangle" or "rounded_rectangle" => "roundRect",
            "alternateprocess" or "alternate_process" => "flowChartAlternateProcess",
            "swimlane" => "rect",
            _ => null
        };

        if (mapped is not null)
        {
            return (mapped, null);
        }

        _ = bounds;
        return ("rect", $"Unsupported draw.io shape '{raw}' was mapped to a rectangle.");
    }

    private static string NormalizeShapeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var name = raw.Trim();
        const string flowchartPrefix = "mxgraph.flowchart.";
        const string basicPrefix = "mxgraph.basic.";
        if (name.StartsWith(flowchartPrefix, StringComparison.OrdinalIgnoreCase))
        {
            name = name[flowchartPrefix.Length..];
        }
        else if (name.StartsWith(basicPrefix, StringComparison.OrdinalIgnoreCase))
        {
            name = name[basicPrefix.Length..];
        }

        return name.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
    }

    private static int? RoundRectAdjust(string preset, IReadOnlyDictionary<string, string> style, SvgRect bounds)
    {
        if (preset is not "roundRect")
        {
            return null;
        }

        var radius = StyleNumber(style, "arcSize", double.NaN);
        if (double.IsNaN(radius) || radius <= 0)
        {
            radius = Math.Min(bounds.Width, bounds.Height) / 6d;
            var adjust = radius / Math.Max(0.01, Math.Min(bounds.Width, bounds.Height)) * 100000d;
            return (int)Math.Round(Math.Max(0, Math.Min(50000, adjust)));
        }

        return (int)Math.Round(Math.Max(0, Math.Min(50000, radius * 1000d)));
    }

    private static SvgStyle VertexStyle(IReadOnlyDictionary<string, string> style)
    {
        var fill = Color(style, "fillColor", "#ffffff");
        var stroke = Color(style, "strokeColor", "#000000");
        return SvgStyle.Default with
        {
            Fill = fill,
            Stroke = stroke,
            StrokeWidth = Math.Max(0.25, StyleNumber(style, "strokeWidth", 1)),
            StrokeDashArray = IsTrue(style, "dashed") ? DashPattern(style) : null,
            Opacity = StyleNumber(style, "opacity", 100) / 100d,
            FillOpacity = StyleNumber(style, "fillOpacity", 100) / 100d,
            StrokeOpacity = StyleNumber(style, "strokeOpacity", 100) / 100d,
            FontFamily = style.TryGetValue("fontFamily", out var font) ? font : "Arial",
            FontSize = StyleNumber(style, "fontSize", 12),
            Color = Color(style, "fontColor", "#000000")
        };
    }

    private static SvgStyle EdgeStyle(IReadOnlyDictionary<string, string> style)
    {
        var stroke = Color(style, "strokeColor", "#000000");
        var start = Arrow(style, "startArrow");
        var end = style.TryGetValue("endArrow", out var endArrow)
            ? ArrowValue(endArrow)
            : "classic";
        return SvgStyle.Default with
        {
            Fill = "none",
            Stroke = stroke,
            StrokeWidth = Math.Max(0.25, StyleNumber(style, "strokeWidth", 1)),
            StrokeDashArray = IsTrue(style, "dashed") ? DashPattern(style) : null,
            Opacity = StyleNumber(style, "opacity", 100) / 100d,
            StrokeOpacity = StyleNumber(style, "strokeOpacity", 100) / 100d,
            MarkerStart = start,
            MarkerEnd = end,
            FontFamily = style.TryGetValue("fontFamily", out var font) ? font : "Arial",
            FontSize = StyleNumber(style, "fontSize", 12),
            Color = Color(style, "fontColor", "#000000")
        };
    }

    private static SvgStyle LabelStyle(IReadOnlyDictionary<string, string> style, bool fillShape)
    {
        var align = style.TryGetValue("align", out var rawAlign) ? rawAlign.Trim().ToLowerInvariant() : "center";
        var bold = false;
        var italic = false;
        if (style.TryGetValue("fontStyle", out var rawFontStyle))
        {
            if (int.TryParse(rawFontStyle, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits))
            {
                bold = (bits & 1) != 0;
                italic = (bits & 2) != 0;
            }
            else
            {
                bold = rawFontStyle.Contains("bold", StringComparison.OrdinalIgnoreCase);
                italic = rawFontStyle.Contains("italic", StringComparison.OrdinalIgnoreCase);
            }
        }
        var color = Color(style, "fontColor", "#000000");
        return SvgStyle.Default with
        {
            Fill = fillShape ? "none" : color,
            Stroke = "none",
            Color = color,
            FontFamily = style.TryGetValue("fontFamily", out var font) ? font : "Arial",
            FontSize = StyleNumber(style, "fontSize", 12),
            FontWeight = bold ? "bold" : "normal",
            FontStyle = italic ? "italic" : "normal",
            TextAlign = align,
            TextAnchor = align switch
            {
                "left" => "start",
                "right" => "end",
                _ => "middle"
            }
        };
    }

    private static string? Arrow(IReadOnlyDictionary<string, string> style, string key) =>
        style.TryGetValue(key, out var value) ? ArrowValue(value) : null;

    private static string? ArrowValue(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("null", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;

    private static string DashPattern(IReadOnlyDictionary<string, string> style) =>
        style.TryGetValue("dashPattern", out var pattern) && !string.IsNullOrWhiteSpace(pattern)
            ? pattern.Replace(' ', ',')
            : "8,8";

    private static string? Color(IReadOnlyDictionary<string, string> style, string key, string fallback)
    {
        if (!style.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return "none";
        }

        if (value.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        return value;
    }

    private static Dictionary<string, string> ParseStyle(string? style)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(style))
        {
            return result;
        }

        foreach (var part in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator < 0)
            {
                result.TryAdd("shape", trimmed);
                continue;
            }

            result[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
        }

        return result;
    }

    private static IReadOnlyList<SvgTextLine> SplitLines(string text, double x, double y)
    {
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.TrimEnd())
            .ToArray();
        if (lines.Length == 0)
        {
            return [new SvgTextLine(x, y, text)];
        }

        return lines.Select(line => new SvgTextLine(x, y, line)).ToArray();
    }

    private static string PlainText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = WebUtility.HtmlDecode(value.Replace("\u00a0", " ", StringComparison.Ordinal));
        if (text.Contains('<', StringComparison.Ordinal))
        {
            text = Regex.Replace(text, @"<(br|BR)\s*/?>", "\n");
            text = Regex.Replace(text, @"</(div|p|li|h[1-6]|tr)[^>]*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", string.Empty);
            text = WebUtility.HtmlDecode(text);
        }

        return Regex.Replace(text, @"[ \t]+\n", "\n").Trim();
    }

    private static SvgPoint PointAlong(IReadOnlyList<SvgPoint> points, double t)
    {
        if (points.Count == 1)
        {
            return points[0];
        }

        var lengths = new double[points.Count - 1];
        var total = 0d;
        for (var i = 0; i < lengths.Length; i++)
        {
            var dx = points[i + 1].X - points[i].X;
            var dy = points[i + 1].Y - points[i].Y;
            lengths[i] = Math.Sqrt(dx * dx + dy * dy);
            total += lengths[i];
        }

        if (total <= 0)
        {
            return points[0];
        }

        var remaining = Math.Clamp(t, 0, 1) * total;
        for (var i = 0; i < lengths.Length; i++)
        {
            if (remaining <= lengths[i] || i == lengths.Length - 1)
            {
                var ratio = lengths[i] <= 0 ? 0 : remaining / lengths[i];
                return new SvgPoint(
                    points[i].X + (points[i + 1].X - points[i].X) * ratio,
                    points[i].Y + (points[i + 1].Y - points[i].Y) * ratio);
            }

            remaining -= lengths[i];
        }

        return points[^1];
    }

    private static SvgRect SceneBounds(SvgScene scene)
    {
        var points = new List<SvgPoint>();
        foreach (var element in scene.Elements)
        {
            switch (element)
            {
                case SvgPresetShapeElement preset:
                    points.Add(new SvgPoint(preset.X, preset.Y));
                    points.Add(new SvgPoint(preset.X + preset.Width, preset.Y + preset.Height));
                    break;
                case SvgLineElement line:
                    points.Add(new SvgPoint(line.X1, line.Y1));
                    points.Add(new SvgPoint(line.X2, line.Y2));
                    break;
                case SvgPolylineElement polyline:
                    points.AddRange(polyline.Points);
                    break;
                case SvgTextElement text when text.TextBox is { } box:
                    points.Add(new SvgPoint(box.X, box.Y));
                    points.Add(new SvgPoint(box.X + box.Width, box.Y + box.Height));
                    break;
            }
        }

        var bounds = SvgRect.FromPoints(points);
        const double pad = 16;
        return new SvgRect(bounds.X - pad, bounds.Y - pad, bounds.Width + pad * 2, bounds.Height + pad * 2);
    }

    private static SvgScene EmptyScene() => new()
    {
        Width = 1,
        Height = 1,
        ViewBox = new SvgRect(0, 0, 1, 1)
    };

    private static bool IsGroup(DrawIoCell cell) =>
        cell.Style.TryGetValue("shape", out var shape) &&
        shape.Equals("group", StringComparison.OrdinalIgnoreCase);

    private static bool IsTextOnly(DrawIoCell cell) =>
        cell.Style.TryGetValue("shape", out var shape) &&
        (shape.Equals("text", StringComparison.OrdinalIgnoreCase) ||
         shape.Equals("edgeLabel", StringComparison.OrdinalIgnoreCase));

    private static bool HasExplicitFill(IReadOnlyDictionary<string, string> style) =>
        style.TryGetValue("fillColor", out var fill) &&
        !string.Equals(fill, "none", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(fill, "default", StringComparison.OrdinalIgnoreCase);

    private static bool IsTrue(IReadOnlyDictionary<string, string> style, string key) =>
        style.TryGetValue(key, out var value) &&
        (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));

    private static double StyleNumber(IReadOnlyDictionary<string, string> style, string key, double fallback) =>
        style.TryGetValue(key, out var value) ? Number(value, fallback) : fallback;

    private static IReadOnlyList<SvgPoint> ReadWaypoints(XElement? geometry)
    {
        if (geometry is null)
        {
            return [];
        }

        var array = geometry.Descendants().FirstOrDefault(element =>
            LocalName(element) == "Array" && Attr(element, "as") == "points");
        if (array is null)
        {
            return [];
        }

        return array.Elements()
            .Where(element => LocalName(element) == "mxPoint")
            .Select(point => new SvgPoint(Number(Attr(point, "x")), Number(Attr(point, "y"))))
            .ToArray();
    }

    private static SvgPoint? NamedPoint(XElement? geometry, string name)
    {
        var point = geometry?.Elements().FirstOrDefault(element =>
            LocalName(element) == "mxPoint" && Attr(element, "as") == name);
        return point is null ? null : new SvgPoint(Number(Attr(point, "x")), Number(Attr(point, "y")));
    }

    private static bool IsFlag(XElement element, string name) => Attr(element, name) is "1" or "true";

    private static string? Attr(XElement? element, string name) => element?.Attribute(name)?.Value;

    private static string LocalName(XElement element) => element.Name.LocalName;

    private static double Number(string? value, double fallback = 0) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;

    internal static string DecompressDiagram(string data)
    {
        var trimmed = Regex.Replace(data.Trim(), @"\s+", string.Empty);
        if (trimmed.StartsWith('<'))
        {
            return trimmed;
        }

        if (trimmed.Contains('%', StringComparison.Ordinal))
        {
            var unescaped = TryUnescape(trimmed);
            if (unescaped.TrimStart().StartsWith('<'))
            {
                return unescaped;
            }

            trimmed = unescaped;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(trimmed);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Draw.io diagram compression is not valid Base64.");
        }

        var inflated = InflateBytes(bytes).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (!inflated.StartsWith('<'))
        {
            inflated = TryUnescape(inflated);
        }

        if (!inflated.TrimStart().StartsWith('<'))
        {
            throw new InvalidOperationException("Decompressed draw.io diagram is not XML.");
        }

        return inflated;
    }

    private static string InflateBytes(byte[] bytes)
    {
        Exception? last = null;
        foreach (var stream in DecompressAttempts(bytes))
        {
            try
            {
                return ReadStream(stream);
            }
            catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or IOException)
            {
                last = exception;
            }
        }

        throw new InvalidOperationException("Could not decompress draw.io diagram payload.", last);
    }

    private static IEnumerable<Stream> DecompressAttempts(byte[] bytes)
    {
        yield return new ZLibStream(new MemoryStream(bytes), CompressionMode.Decompress);
        yield return new DeflateStream(new MemoryStream(bytes), CompressionMode.Decompress);
        yield return new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        if (bytes.Length > 2)
        {
            yield return new DeflateStream(new MemoryStream(bytes, 2, bytes.Length - 2), CompressionMode.Decompress);
        }
    }

    private static string ReadStream(Stream stream)
    {
        using (stream)
        using (var reader = new StreamReader(stream, Utf8))
        {
            return reader.ReadToEnd();
        }
    }

    private static string TryUnescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private sealed class DrawIoCell
    {
        public required string Id { get; init; }
        public required string ParentId { get; init; }
        public string? SourceId { get; init; }
        public string? TargetId { get; init; }
        public required string Value { get; init; }
        public required Dictionary<string, string> Style { get; init; }
        public bool Vertex { get; init; }
        public bool Edge { get; init; }
        public bool Visible { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public bool Relative { get; init; }
        public SvgPoint? SourcePoint { get; init; }
        public SvgPoint? TargetPoint { get; init; }
        public SvgPoint? Offset { get; init; }
        public IReadOnlyList<SvgPoint> Waypoints { get; init; } = [];
    }
}
