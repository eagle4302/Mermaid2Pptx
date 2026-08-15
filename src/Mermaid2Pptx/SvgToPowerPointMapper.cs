namespace Mermaid2Pptx;

public sealed class SvgToPowerPointMapper
{
    public PptxSlideModel MapSvgToSlide(SvgScene scene, double widthInches, double heightInches)
    {
        var viewport = UnitConversion.CreateViewportMap(scene.ViewBox, widthInches, heightInches);
        return MapSvgToSlide(scene, viewport);
    }

    public PptxSlideModel MapSvgToSlide(SvgScene scene, double widthInches, double heightInches, ExtractedSvg extracted)
    {
        var hasLayout =
            extracted.ContainerX.HasValue &&
            extracted.ContainerY.HasValue &&
            extracted.ContainerWidth is > 0 &&
            extracted.ContainerHeight is > 0 &&
            extracted.SlideWidth is > 0 &&
            extracted.SlideHeight is > 0;

        var viewport = hasLayout
            ? UnitConversion.CreateRegionMap(
                scene.ViewBox,
                widthInches,
                heightInches,
                extracted.ContainerX!.Value,
                extracted.ContainerY!.Value,
                extracted.ContainerWidth!.Value,
                extracted.ContainerHeight!.Value,
                extracted.SlideWidth!.Value,
                extracted.SlideHeight!.Value)
            : UnitConversion.CreateViewportMap(scene.ViewBox, widthInches, heightInches);

        return MapSvgToSlide(scene, viewport);
    }

    private PptxSlideModel MapSvgToSlide(SvgScene scene, SvgViewportMap viewport)
    {
        var slide = new PptxSlideModel
        {
            WidthEmu = viewport.SlideWidthEmu,
            HeightEmu = viewport.SlideHeightEmu
        };
        slide.Warnings.AddRange(scene.Warnings);

        foreach (var element in scene.Elements)
        {
            var shape = MapElement(element, viewport, slide.Shapes.Count + 2);
            if (shape is not null)
            {
                var complexStartMarker = UsesComplexMarker(element.Style.MarkerStart, scene.Markers);
                var complexEndMarker = UsesComplexMarker(element.Style.MarkerEnd, scene.Markers);
                if (complexStartMarker)
                {
                    shape.ArrowStart = false;
                }

                if (complexEndMarker)
                {
                    shape.ArrowEnd = false;
                }

                AddShape(slide, shape);
                if (complexStartMarker)
                {
                    foreach (var markerShape in BuildMarkerShapes(shape, element.Style.MarkerStart, atEnd: false, viewport, scene.Markers, slide.Shapes.Count + 2))
                    {
                        AddShape(slide, markerShape);
                    }
                }

                if (complexEndMarker)
                {
                    foreach (var markerShape in BuildMarkerShapes(shape, element.Style.MarkerEnd, atEnd: true, viewport, scene.Markers, slide.Shapes.Count + 2))
                    {
                        AddShape(slide, markerShape);
                    }
                }
            }
        }

        MergeOrExpandTextContainers(slide, viewport);
        return slide;
    }

    private static PptxShape? MapElement(SvgElement element, SvgViewportMap viewport, int shapeId) =>
        element switch
        {
            SvgRectElement rect => MapRect(rect, viewport, shapeId),
            SvgEllipseElement ellipse => MapEllipse(ellipse, viewport, shapeId),
            SvgPresetShapeElement preset => MapPresetShape(preset, viewport, shapeId),
            SvgLineElement line => MapLine(line, viewport, shapeId),
            SvgPolylineElement polyline => MapPolyline(polyline, viewport, shapeId),
            SvgPathElement path => MapPath(path, viewport, shapeId),
            SvgTextElement text => MapText(text, viewport, shapeId),
            _ => null
        };

    private static PptxShape MapRect(SvgRectElement rect, SvgViewportMap viewport, int shapeId)
    {
        var corners = new[]
        {
            rect.Transform.Transform(new SvgPoint(rect.X, rect.Y)),
            rect.Transform.Transform(new SvgPoint(rect.X + rect.Width, rect.Y)),
            rect.Transform.Transform(new SvgPoint(rect.X + rect.Width, rect.Y + rect.Height)),
            rect.Transform.Transform(new SvgPoint(rect.X, rect.Y + rect.Height))
        };
        var mapped = corners.Select(viewport.Map).ToArray();
        var bounds = Bounds(mapped);
        var axisAligned = IsAxisAligned(mapped);
        var rounded = rect.Rx > 0 || rect.Ry > 0;

        if (axisAligned)
        {
            return new PptxShape
            {
                Id = shapeId,
                Name = rounded ? $"Rounded Rect {shapeId}" : $"Rect {shapeId}",
                Kind = PptxShapeKind.Preset,
                PresetGeometry = rounded ? "roundRect" : "rect",
                PresetAdjustValue = rounded ? RoundRectAdjust(rect) : null,
                X = bounds.X,
                Y = bounds.Y,
                Cx = bounds.Cx,
                Cy = bounds.Cy,
                Style = rect.Style
            };
        }

        return BuildCustomShape(shapeId, $"Rect Path {shapeId}", rect.Style, ToClosedPolygon(corners, viewport));
    }

    private static PptxShape MapEllipse(SvgEllipseElement ellipse, SvgViewportMap viewport, int shapeId)
    {
        var points = new[]
        {
            ellipse.Transform.Transform(new SvgPoint(ellipse.Cx - ellipse.Rx, ellipse.Cy - ellipse.Ry)),
            ellipse.Transform.Transform(new SvgPoint(ellipse.Cx + ellipse.Rx, ellipse.Cy - ellipse.Ry)),
            ellipse.Transform.Transform(new SvgPoint(ellipse.Cx + ellipse.Rx, ellipse.Cy + ellipse.Ry)),
            ellipse.Transform.Transform(new SvgPoint(ellipse.Cx - ellipse.Rx, ellipse.Cy + ellipse.Ry))
        };
        var mapped = points.Select(viewport.Map).ToArray();
        var bounds = Bounds(mapped);

        return new PptxShape
        {
            Id = shapeId,
            Name = $"Ellipse {shapeId}",
            Kind = PptxShapeKind.Preset,
            PresetGeometry = "ellipse",
            X = bounds.X,
            Y = bounds.Y,
            Cx = bounds.Cx,
            Cy = bounds.Cy,
            Style = ellipse.Style
        };
    }

    private static PptxShape MapPresetShape(SvgPresetShapeElement preset, SvgViewportMap viewport, int shapeId)
    {
        var corners = new[]
        {
            preset.Transform.Transform(new SvgPoint(preset.X, preset.Y)),
            preset.Transform.Transform(new SvgPoint(preset.X + preset.Width, preset.Y)),
            preset.Transform.Transform(new SvgPoint(preset.X + preset.Width, preset.Y + preset.Height)),
            preset.Transform.Transform(new SvgPoint(preset.X, preset.Y + preset.Height))
        };
        var mapped = corners.Select(viewport.Map).ToArray();
        var bounds = Bounds(mapped);
        var geometry = string.IsNullOrWhiteSpace(preset.PresetGeometry) ? "rect" : preset.PresetGeometry;

        return new PptxShape
        {
            Id = shapeId,
            Name = $"{PresetDisplayName(geometry)} {shapeId}",
            Kind = PptxShapeKind.Preset,
            PresetGeometry = geometry,
            PresetAdjustValue = preset.PresetAdjustValue,
            X = bounds.X,
            Y = bounds.Y,
            Cx = bounds.Cx,
            Cy = bounds.Cy,
            Style = preset.Style
        };
    }

    private static string PresetDisplayName(string geometry) => geometry switch
    {
        "roundRect" => "Rounded Rect",
        "ellipse" => "Ellipse",
        "diamond" => "Diamond",
        "parallelogram" => "Parallelogram",
        "hexagon" => "Hexagon",
        "triangle" => "Triangle",
        "cloud" => "Cloud",
        "can" => "Cylinder",
        "chevron" => "Chevron",
        _ when geometry.StartsWith("flowchart", StringComparison.OrdinalIgnoreCase) =>
            "Flowchart " + geometry["flowchart".Length..],
        _ => "Rect"
    };

    private static PptxShape MapLine(SvgLineElement line, SvgViewportMap viewport, int shapeId)
    {
        var points = new[]
        {
            line.Transform.Transform(new SvgPoint(line.X1, line.Y1)),
            line.Transform.Transform(new SvgPoint(line.X2, line.Y2))
        }.Select(viewport.Map).ToArray();

        return BuildLineShape(shapeId, $"Line {shapeId}", line.Style, points[0], points[1]);
    }

    private static PptxShape MapPolyline(SvgPolylineElement polyline, SvgViewportMap viewport, int shapeId)
    {
        var transformed = polyline.Points.Select(polyline.Transform.Transform).ToArray();
        if (transformed.Length == 0)
        {
            return EmptyShape(shapeId);
        }

        var segments = new List<SvgPathSegment> { new MoveTo(transformed[0]) };
        segments.AddRange(transformed.Skip(1).Select(point => new LineTo(point)));
        if (polyline.Closed)
        {
            segments.Add(new ClosePath());
        }

        return BuildCustomShape(
            shapeId,
            polyline.Closed ? $"Polygon {shapeId}" : $"Polyline {shapeId}",
            polyline.Style,
            segments,
            viewport,
            preferNoFill: !polyline.Closed && IsNone(polyline.Style.Fill));
    }

    private static PptxShape MapPath(SvgPathElement path, SvgViewportMap viewport, int shapeId)
    {
        var transformed = TransformSegments(path.Segments, path.Transform);
        return BuildCustomShape(shapeId, MermaidName(path, shapeId), path.Style, transformed, viewport, preferNoFill: IsNone(path.Style.Fill));
    }

    private static PptxShape MapText(SvgTextElement text, SvgViewportMap viewport, int shapeId)
    {
        var textValue = string.Join(Environment.NewLine, text.Lines.Select(line => line.Text));
        var fontSize = Math.Max(1, text.Style.FontSize);
        if (text.TextBox is { } box)
        {
            var corners = new[]
            {
                text.Transform.Transform(new SvgPoint(box.X, box.Y)),
                text.Transform.Transform(new SvgPoint(box.X + box.Width, box.Y)),
                text.Transform.Transform(new SvgPoint(box.X + box.Width, box.Y + box.Height)),
                text.Transform.Transform(new SvgPoint(box.X, box.Y + box.Height))
            }.Select(viewport.Map).ToArray();
            var boxBounds = Bounds(corners);
            var align = text.Style.TextAlign?.ToLowerInvariant() ?? "center";
            var minWidth = (long)Math.Round(viewport.MapLengthX(EstimatedTextWidth(text.Lines, text.Style) + 10));
            var minHeight = (long)Math.Round(viewport.MapLengthY(Math.Max(
                text.Style.FontSize * 1.3,
                text.Lines.Count * text.Style.FontSize * 1.25)));
            var cx = Math.Max(boxBounds.Cx, Math.Max(1, minWidth));
            var cy = Math.Max(boxBounds.Cy, Math.Max(1, minHeight));
            var boxX = boxBounds.X;
            var extraWidth = cx - boxBounds.Cx;
            if (extraWidth > 0)
            {
                boxX = align switch
                {
                    "center" => boxBounds.X - extraWidth / 2,
                    "right" => boxBounds.X - extraWidth,
                    _ => boxBounds.X
                };
            }

            return new PptxShape
            {
                Id = shapeId,
                Name = $"Text {shapeId}",
                Kind = PptxShapeKind.Text,
                PresetGeometry = "rect",
                X = boxX,
                Y = boxBounds.Y,
                Cx = cx,
                Cy = cy,
                Style = text.Style,
                Text = textValue,
                TextAlignment = align switch
                {
                    "center" => "ctr",
                    "right" => "r",
                    _ => "l"
                },
                NoWrapText = true,
                PreferNoFill = true,
                PreferNoLine = true
            };
        }

        var transformedLines = text.Lines.Select(line => text.Transform.Transform(new SvgPoint(line.X, line.Y))).ToArray();
        var mapped = transformedLines.Select(viewport.Map).ToArray();
        var anchor = text.Style.TextAnchor?.ToLowerInvariant() ?? "start";
        var maxChars = text.Lines.Select(line => line.Text.Length).DefaultIfEmpty(1).Max();
        var widthSvg = Math.Max(fontSize * 2, maxChars * fontSize * 0.62);
        var heightSvg = Math.Max(fontSize * 1.3, text.Lines.Count * fontSize * 1.25);
        var width = (long)Math.Round(viewport.MapLengthX(widthSvg));
        var height = (long)Math.Round(viewport.MapLengthY(heightSvg));
        var x = mapped.Min(point => point.X);
        var y = mapped.Min(point => point.Y) - (long)Math.Round(viewport.MapLengthY(fontSize));

        if (anchor == "middle")
        {
            x -= width / 2;
        }
        else if (anchor == "end")
        {
            x -= width;
        }

        return new PptxShape
        {
            Id = shapeId,
            Name = $"Text {shapeId}",
            Kind = PptxShapeKind.Text,
            PresetGeometry = "rect",
            X = x,
            Y = y,
            Cx = Math.Max(1, width),
            Cy = Math.Max(1, height),
            Style = text.Style,
            Text = textValue,
            TextAlignment = anchor switch
            {
                "middle" => "ctr",
                "end" => "r",
                _ => "l"
            },
            NoWrapText = true,
            PreferNoFill = true,
            PreferNoLine = true
        };
    }

    private static double EstimatedTextWidth(IReadOnlyList<SvgTextLine> lines, SvgStyle style)
    {
        var fontSize = Math.Max(1, style.FontSize);
        return lines
            .Select(line => line.Text.Sum(ch => CharacterWidthFactor(ch)) * fontSize)
            .DefaultIfEmpty(fontSize)
            .Max();
    }

    private static double CharacterWidthFactor(char ch)
    {
        if (char.IsWhiteSpace(ch))
        {
            return 0.35;
        }

        if (char.IsUpper(ch))
        {
            return 0.68;
        }

        if (char.IsDigit(ch))
        {
            return 0.56;
        }

        return ch switch
        {
            'i' or 'l' or 'I' or '|' or '.' or ',' or ':' or ';' or '!' => 0.28,
            'm' or 'w' or 'M' or 'W' => 0.86,
            '(' or ')' or '[' or ']' or '{' or '}' or '+' or '-' or '_' => 0.45,
            _ => 0.58
        };
    }

    private static PptxShape BuildCustomShape(
        int shapeId,
        string name,
        SvgStyle style,
        IReadOnlyList<SvgPathSegment> segments,
        SvgViewportMap viewport,
        bool preferNoFill = false,
        bool preferNoLine = false)
    {
        var mappedSegments = MapSegments(segments, viewport);
        return BuildCustomShape(shapeId, name, style, mappedSegments, preferNoFill, preferNoLine);
    }

    private static PptxShape BuildCustomShape(
        int shapeId,
        string name,
        SvgStyle style,
        IReadOnlyList<PptxPathCommand> commands,
        bool preferNoFill = false,
        bool preferNoLine = false)
    {
        var points = ExtractPoints(commands).ToArray();
        var bounds = Bounds(points);
        var normalized = NormalizeCommands(commands, bounds.X, bounds.Y);

        return new PptxShape
        {
            Id = shapeId,
            Name = name,
            Kind = PptxShapeKind.Custom,
            X = bounds.X,
            Y = bounds.Y,
            Cx = Math.Max(1, bounds.Cx),
            Cy = Math.Max(1, bounds.Cy),
            PathWidth = Math.Max(1, bounds.Cx),
            PathHeight = Math.Max(1, bounds.Cy),
            PathCommands = normalized,
            Style = style,
            PreferNoFill = preferNoFill,
            PreferNoLine = preferNoLine,
            ArrowStart = HasMarker(style.MarkerStart),
            ArrowEnd = HasMarker(style.MarkerEnd)
        };
    }

    private static PptxShape BuildLineShape(int shapeId, string name, SvgStyle style, PptPoint start, PptPoint end)
    {
        var minX = Math.Min(start.X, end.X);
        var minY = Math.Min(start.Y, end.Y);
        var maxX = Math.Max(start.X, end.X);
        var maxY = Math.Max(start.Y, end.Y);

        return new PptxShape
        {
            Id = shapeId,
            Name = name,
            Kind = PptxShapeKind.Line,
            X = minX,
            Y = minY,
            Cx = Math.Max(1, maxX - minX),
            Cy = Math.Max(1, maxY - minY),
            PresetGeometry = "line",
            Style = style,
            PreferNoFill = true,
            PreferNoLine = false,
            ArrowStart = HasMarker(style.MarkerStart),
            ArrowEnd = HasMarker(style.MarkerEnd),
            FlipH = end.X < start.X,
            FlipV = end.Y < start.Y
        };
    }

    private static void AddShape(PptxSlideModel slide, PptxShape shape)
    {
        // Keep SVG marker references on the source line as native DrawingML
        // line arrowheads instead of adding separate marker shapes.
        slide.Shapes.Add(shape);
    }

    private static void MergeOrExpandTextContainers(PptxSlideModel slide, SvgViewportMap viewport)
    {
        var paddingX = Math.Max(1, (long)Math.Round(Math.Abs(viewport.MapLengthX(4))));
        var paddingY = Math.Max(1, (long)Math.Round(Math.Abs(viewport.MapLengthY(3))));

        var associations = new List<(int TextIndex, int ContainerIndex)>();
        for (var textIndex = 0; textIndex < slide.Shapes.Count; textIndex++)
        {
            var textShape = slide.Shapes[textIndex];
            if (textShape.Kind != PptxShapeKind.Text || string.IsNullOrWhiteSpace(textShape.Text))
            {
                continue;
            }

            if (TryFindTextContainer(slide.Shapes, textIndex, textShape, out var containerIndex))
            {
                associations.Add((textIndex, containerIndex));
            }
        }

        var textsByContainer = associations
            .GroupBy(pair => pair.ContainerIndex)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.TextIndex).ToList());

        var mergeTextIndexes = new HashSet<int>();
        foreach (var (containerIndex, textIndexes) in textsByContainer)
        {
            ExpandContainerToFitTexts(slide.Shapes[containerIndex], textIndexes.Select(index => slide.Shapes[index]), paddingX, paddingY);

            if (textIndexes.Count != 1)
            {
                continue;
            }

            var textIndex = textIndexes[0];
            MergeTextIntoContainer(slide.Shapes[containerIndex], slide.Shapes[textIndex]);
            mergeTextIndexes.Add(textIndex);
        }

        if (mergeTextIndexes.Count == 0)
        {
            return;
        }

        for (var index = slide.Shapes.Count - 1; index >= 0; index--)
        {
            if (mergeTextIndexes.Contains(index))
            {
                slide.Shapes.RemoveAt(index);
            }
        }

        for (var index = 0; index < slide.Shapes.Count; index++)
        {
            slide.Shapes[index].Id = index + 2;
        }
    }

    private static void ExpandContainerToFitTexts(
        PptxShape container,
        IEnumerable<PptxShape> textShapes,
        long paddingX,
        long paddingY)
    {
        var current = Bounds(container);
        var expanded = current;
        var changed = false;
        foreach (var textShape in textShapes)
        {
            var required = Inflate(Bounds(textShape), paddingX, paddingY);
            if (Contains(expanded, required))
            {
                continue;
            }

            expanded = Union(expanded, required);
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        container.X = expanded.Left;
        container.Y = expanded.Top;
        container.Cx = Math.Max(1, expanded.Right - expanded.Left);
        container.Cy = Math.Max(1, expanded.Bottom - expanded.Top);
    }

    private static void MergeTextIntoContainer(PptxShape container, PptxShape textShape)
    {
        container.Text = textShape.Text;
        container.TextAlignment = textShape.TextAlignment;
        container.NoWrapText = textShape.NoWrapText;
        container.Style = container.Style with
        {
            Color = ResolveMergedTextColor(textShape.Style),
            FontFamily = textShape.Style.FontFamily,
            FontSize = textShape.Style.FontSize,
            FontWeight = textShape.Style.FontWeight,
            FontStyle = textShape.Style.FontStyle,
            TextAlign = textShape.Style.TextAlign,
            TextAnchor = textShape.Style.TextAnchor
        };
    }

    private static string? ResolveMergedTextColor(SvgStyle textStyle)
    {
        // foreignObject / CSS labels usually set color; SVG <text> paints with fill.
        if (!IsNonePaint(textStyle.Color) &&
            !string.Equals(textStyle.Color, "black", StringComparison.OrdinalIgnoreCase))
        {
            return textStyle.Color;
        }

        if (!IsNonePaint(textStyle.Fill) &&
            !string.Equals(textStyle.Fill, "black", StringComparison.OrdinalIgnoreCase))
        {
            return textStyle.Fill;
        }

        return textStyle.Color ?? textStyle.Fill ?? "black";
    }

    private static bool IsNonePaint(string? paint) =>
        string.IsNullOrWhiteSpace(paint) ||
        paint.Equals("none", StringComparison.OrdinalIgnoreCase) ||
        paint.Equals("transparent", StringComparison.OrdinalIgnoreCase);

    private static bool TryFindTextContainer(
        IReadOnlyList<PptxShape> shapes,
        int textIndex,
        PptxShape textShape,
        out int containerIndex)
    {
        containerIndex = -1;
        var textBounds = Bounds(textShape);
        var centerX = textBounds.Left + (textBounds.Right - textBounds.Left) / 2;
        var centerY = textBounds.Top + (textBounds.Bottom - textBounds.Top) / 2;

        for (var shapeIndex = textIndex - 1; shapeIndex >= 0; shapeIndex--)
        {
            var candidate = shapes[shapeIndex];
            if (!CanContainText(candidate))
            {
                continue;
            }

            if (ContainsPoint(Bounds(candidate), centerX, centerY))
            {
                containerIndex = shapeIndex;
                return true;
            }
        }

        var bestArea = long.MaxValue;
        for (var shapeIndex = 0; shapeIndex < shapes.Count; shapeIndex++)
        {
            if (shapeIndex == textIndex)
            {
                continue;
            }

            var candidate = shapes[shapeIndex];
            if (!CanContainText(candidate))
            {
                continue;
            }

            var candidateBounds = Bounds(candidate);
            if (!ContainsPoint(candidateBounds, centerX, centerY))
            {
                continue;
            }

            var area = Math.Max(1, candidate.Cx) * Math.Max(1, candidate.Cy);
            if (area < bestArea)
            {
                bestArea = area;
                containerIndex = shapeIndex;
            }
        }

        return containerIndex >= 0;
    }

    private static bool CanContainText(PptxShape shape)
    {
        if (shape.Kind is PptxShapeKind.Text or PptxShapeKind.Line)
        {
            return false;
        }

        if (shape.Name.StartsWith("Marker", StringComparison.OrdinalIgnoreCase) ||
            shape.Name.StartsWith("Mermaid Edge", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return shape.Kind == PptxShapeKind.Preset ||
               shape.PathCommands.OfType<PptxClosePath>().Any();
    }

    private static ShapeBounds Bounds(PptxShape container) =>
        new(container.X, container.Y, container.X + Math.Max(1, container.Cx), container.Y + Math.Max(1, container.Cy));

    private static ShapeBounds Inflate(ShapeBounds bounds, long paddingX, long paddingY) =>
        new(bounds.Left - paddingX, bounds.Top - paddingY, bounds.Right + paddingX, bounds.Bottom + paddingY);

    private static ShapeBounds Union(ShapeBounds first, ShapeBounds second) =>
        new(
            Math.Min(first.Left, second.Left),
            Math.Min(first.Top, second.Top),
            Math.Max(first.Right, second.Right),
            Math.Max(first.Bottom, second.Bottom));

    private static bool Contains(ShapeBounds outer, ShapeBounds inner) =>
        outer.Left <= inner.Left &&
        outer.Top <= inner.Top &&
        outer.Right >= inner.Right &&
        outer.Bottom >= inner.Bottom;

    private static bool ContainsPoint(ShapeBounds bounds, long x, long y) =>
        x >= bounds.Left &&
        x <= bounds.Right &&
        y >= bounds.Top &&
        y <= bounds.Bottom;

    private readonly record struct ShapeBounds(long Left, long Top, long Right, long Bottom);

    private static IReadOnlyList<PptxShape> BuildMarkerShapes(
        PptxShape source,
        string? markerRef,
        bool atEnd,
        SvgViewportMap viewport,
        IReadOnlyDictionary<string, SvgMarker> markers,
        int shapeId)
    {
        if (TryGetMarkerPlacement(source, atEnd, out var anchor, out var angle) &&
            TryGetMarker(markers, markerRef, out var marker))
        {
            var shapes = marker.Elements
                .Select((element, index) => BuildMarkerElementShape(source, marker, element, viewport, anchor, angle, shapeId + index))
                .Where(shape => shape is not null)
                .Cast<PptxShape>()
                .ToArray();
            if (shapes.Length > 0)
            {
                return shapes;
            }
        }

        return TryBuildMarkerTriangle(source, atEnd, shapeId) is { } fallback ? [fallback] : [];
    }

    private static bool TryGetMarkerPlacement(PptxShape shape, bool atEnd, out PptPoint anchor, out double angle)
    {
        anchor = new PptPoint(0, 0);
        angle = 0;
        if (atEnd)
        {
            if (!TryGetVector(shape, atEnd: true, out var from, out var to))
            {
                return false;
            }

            anchor = to;
            angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
            return true;
        }

        if (!TryGetFirstVector(shape, out var start, out var next))
        {
            return false;
        }

        anchor = start;
        angle = Math.Atan2(next.Y - start.Y, next.X - start.X);
        return true;
    }

    private static PptxShape? BuildMarkerElementShape(
        PptxShape source,
        SvgMarker marker,
        SvgElement element,
        SvgViewportMap viewport,
        PptPoint anchor,
        double angle,
        int shapeId)
    {
        var style = MarkerStyle(element.Style, source);
        return element switch
        {
            SvgPathElement path => BuildCustomShape(
                shapeId,
                $"Marker {shapeId}",
                style,
                MarkerSegments(TransformSegments(path.Segments, path.Transform), marker, viewport, anchor, angle),
                preferNoFill: IsNone(style.Fill),
                preferNoLine: IsNone(style.Stroke)),
            SvgPolylineElement polyline => BuildCustomShape(
                shapeId,
                $"Marker {shapeId}",
                style,
                MarkerPolylineCommands(polyline, marker, viewport, anchor, angle),
                preferNoFill: !polyline.Closed && IsNone(style.Fill),
                preferNoLine: IsNone(style.Stroke)),
            SvgLineElement line => BuildCustomShape(
                shapeId,
                $"Marker {shapeId}",
                style,
                [
                    new PptxMoveTo(MarkerPoint(line.Transform.Transform(new SvgPoint(line.X1, line.Y1)), marker, viewport, anchor, angle)),
                    new PptxLineTo(MarkerPoint(line.Transform.Transform(new SvgPoint(line.X2, line.Y2)), marker, viewport, anchor, angle))
                ],
                preferNoFill: true,
                preferNoLine: IsNone(style.Stroke)),
            SvgRectElement rect => BuildCustomShape(
                shapeId,
                $"Marker {shapeId}",
                style,
                MarkerRectCommands(rect, marker, viewport, anchor, angle),
                preferNoFill: IsNone(style.Fill),
                preferNoLine: IsNone(style.Stroke)),
            SvgEllipseElement ellipse => BuildMarkerEllipse(shapeId, style, ellipse, marker, viewport, anchor, angle),
            _ => null
        };
    }

    private static PptxShape BuildMarkerEllipse(
        int shapeId,
        SvgStyle style,
        SvgEllipseElement ellipse,
        SvgMarker marker,
        SvgViewportMap viewport,
        PptPoint anchor,
        double angle)
    {
        var center = MarkerPoint(ellipse.Transform.Transform(new SvgPoint(ellipse.Cx, ellipse.Cy)), marker, viewport, anchor, angle);
        var rx = Math.Max(1, Math.Abs(viewport.MapLengthX(ellipse.Rx)));
        var ry = Math.Max(1, Math.Abs(viewport.MapLengthY(ellipse.Ry)));
        return new PptxShape
        {
            Id = shapeId,
            Name = $"Marker {shapeId}",
            Kind = PptxShapeKind.Preset,
            PresetGeometry = "ellipse",
            X = (long)Math.Round(center.X - rx),
            Y = (long)Math.Round(center.Y - ry),
            Cx = (long)Math.Round(rx * 2),
            Cy = (long)Math.Round(ry * 2),
            Style = style,
            PreferNoFill = IsNone(style.Fill),
            PreferNoLine = IsNone(style.Stroke)
        };
    }

    private static IReadOnlyList<PptxPathCommand> MarkerSegments(
        IReadOnlyList<SvgPathSegment> segments,
        SvgMarker marker,
        SvgViewportMap viewport,
        PptPoint anchor,
        double angle)
    {
        var result = new List<PptxPathCommand>(segments.Count);
        foreach (var segment in segments)
        {
            result.Add(segment switch
            {
                MoveTo move => new PptxMoveTo(MarkerPoint(move.Point, marker, viewport, anchor, angle)),
                LineTo line => new PptxLineTo(MarkerPoint(line.Point, marker, viewport, anchor, angle)),
                CubicBezierTo cubic => new PptxCubicBezierTo(
                    MarkerPoint(cubic.Control1, marker, viewport, anchor, angle),
                    MarkerPoint(cubic.Control2, marker, viewport, anchor, angle),
                    MarkerPoint(cubic.Point, marker, viewport, anchor, angle)),
                QuadraticBezierTo quadratic => new PptxQuadraticBezierTo(
                    MarkerPoint(quadratic.Control, marker, viewport, anchor, angle),
                    MarkerPoint(quadratic.Point, marker, viewport, anchor, angle)),
                ClosePath => new PptxClosePath(),
                _ => throw new InvalidOperationException($"Unsupported marker path segment {segment.GetType().Name}.")
            });
        }

        return result;
    }

    private static IReadOnlyList<PptxPathCommand> MarkerPolylineCommands(
        SvgPolylineElement polyline,
        SvgMarker marker,
        SvgViewportMap viewport,
        PptPoint anchor,
        double angle)
    {
        var points = polyline.Points.Select(polyline.Transform.Transform).ToArray();
        if (points.Length == 0)
        {
            return [];
        }

        var commands = new List<PptxPathCommand> { new PptxMoveTo(MarkerPoint(points[0], marker, viewport, anchor, angle)) };
        commands.AddRange(points.Skip(1).Select(point => new PptxLineTo(MarkerPoint(point, marker, viewport, anchor, angle))));
        if (polyline.Closed)
        {
            commands.Add(new PptxClosePath());
        }

        return commands;
    }

    private static IReadOnlyList<PptxPathCommand> MarkerRectCommands(
        SvgRectElement rect,
        SvgMarker marker,
        SvgViewportMap viewport,
        PptPoint anchor,
        double angle)
    {
        var corners = new[]
        {
            rect.Transform.Transform(new SvgPoint(rect.X, rect.Y)),
            rect.Transform.Transform(new SvgPoint(rect.X + rect.Width, rect.Y)),
            rect.Transform.Transform(new SvgPoint(rect.X + rect.Width, rect.Y + rect.Height)),
            rect.Transform.Transform(new SvgPoint(rect.X, rect.Y + rect.Height))
        };

        return
        [
            new PptxMoveTo(MarkerPoint(corners[0], marker, viewport, anchor, angle)),
            new PptxLineTo(MarkerPoint(corners[1], marker, viewport, anchor, angle)),
            new PptxLineTo(MarkerPoint(corners[2], marker, viewport, anchor, angle)),
            new PptxLineTo(MarkerPoint(corners[3], marker, viewport, anchor, angle)),
            new PptxClosePath()
        ];
    }

    private static PptPoint MarkerPoint(
        SvgPoint point,
        SvgMarker marker,
        SvgViewportMap viewport,
        PptPoint anchor,
        double angle)
    {
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var dx = viewport.MapLengthX(point.X - marker.RefX);
        var dy = viewport.MapLengthY(point.Y - marker.RefY);
        return new PptPoint(
            (long)Math.Round(anchor.X + dx * cos - dy * sin),
            (long)Math.Round(anchor.Y + dx * sin + dy * cos));
    }

    private static SvgStyle MarkerStyle(SvgStyle markerStyle, PptxShape source)
    {
        var strokeFallback = !IsNone(source.Style.Stroke) ? source.Style.Stroke : source.Style.Color;
        return markerStyle with
        {
            Color = markerStyle.Color ?? source.Style.Color,
            Stroke = IsNone(markerStyle.Stroke) && !IsNone(strokeFallback) ? strokeFallback : markerStyle.Stroke,
            StrokeWidth = markerStyle.StrokeWidth > 0 ? markerStyle.StrokeWidth : Math.Max(1, source.Style.StrokeWidth),
            MarkerStart = null,
            MarkerEnd = null
        };
    }

    private static bool TryGetMarker(
        IReadOnlyDictionary<string, SvgMarker> markers,
        string? markerRef,
        out SvgMarker marker)
    {
        marker = default!;
        var id = MarkerId(markerRef);
        return !string.IsNullOrWhiteSpace(id) && markers.TryGetValue(id, out marker!);
    }

    private static string? MarkerId(string? markerRef)
    {
        if (string.IsNullOrWhiteSpace(markerRef))
        {
            return null;
        }

        var value = markerRef.Trim();
        if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(')'))
        {
            value = value[4..^1].Trim().Trim('\'', '"');
        }

        return value.StartsWith("#", StringComparison.Ordinal) ? value[1..] : value;
    }

    private static PptxShape? TryBuildMarkerTriangle(PptxShape source, bool atEnd, int shapeId)
    {
        if (source.Kind != PptxShapeKind.Custom || source.PathCommands.Count == 0)
        {
            return null;
        }

        if (!TryGetVector(source, atEnd, out var from, out var to))
        {
            return null;
        }

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.001)
        {
            return null;
        }

        var ux = dx / length;
        var uy = dy / length;
        if (!atEnd)
        {
            ux = -ux;
            uy = -uy;
        }

        var px = -uy;
        var py = ux;
        var tipOffset = UnitConversion.PixelsToEmu(4);
        var back = UnitConversion.PixelsToEmu(6);
        var halfWidth = UnitConversion.PixelsToEmu(5);
        var tip = new PptPoint(
            (long)Math.Round(to.X + ux * tipOffset),
            (long)Math.Round(to.Y + uy * tipOffset));
        var baseCenter = new PptPoint(
            (long)Math.Round(to.X - ux * back),
            (long)Math.Round(to.Y - uy * back));
        var left = new PptPoint(
            (long)Math.Round(baseCenter.X + px * halfWidth),
            (long)Math.Round(baseCenter.Y + py * halfWidth));
        var right = new PptPoint(
            (long)Math.Round(baseCenter.X - px * halfWidth),
            (long)Math.Round(baseCenter.Y - py * halfWidth));

        var style = source.Style with
        {
            Fill = IsNone(source.Style.Stroke) ? source.Style.Color : source.Style.Stroke,
            Stroke = IsNone(source.Style.Stroke) ? source.Style.Color : source.Style.Stroke,
            StrokeWidth = 1,
            MarkerStart = null,
            MarkerEnd = null
        };

        return BuildCustomShape(
            shapeId,
            atEnd ? $"Marker End {shapeId}" : $"Marker Start {shapeId}",
            style,
            [
                new PptxMoveTo(tip),
                new PptxLineTo(left),
                new PptxLineTo(right),
                new PptxClosePath()
            ],
            preferNoFill: false,
            preferNoLine: false);
    }

    private static bool TryGetFirstVector(PptxShape shape, out PptPoint from, out PptPoint to)
    {
        from = new PptPoint(0, 0);
        to = new PptPoint(0, 0);
        if (shape.Kind == PptxShapeKind.Line)
        {
            from = LineStart(shape);
            to = LineEnd(shape);
            return true;
        }

        var current = new PptPoint(shape.X, shape.Y);
        foreach (var command in shape.PathCommands)
        {
            switch (command)
            {
                case PptxMoveTo move:
                    current = Absolute(shape, move.Point);
                    break;
                case PptxLineTo line:
                    from = current;
                    to = Absolute(shape, line.Point);
                    return true;
                case PptxCubicBezierTo cubic:
                    from = current;
                    to = FirstDistinctPoint(from, Absolute(shape, cubic.Control1), Absolute(shape, cubic.Control2), Absolute(shape, cubic.Point));
                    return true;
                case PptxQuadraticBezierTo quadratic:
                    from = current;
                    to = FirstDistinctPoint(from, Absolute(shape, quadratic.Control), Absolute(shape, quadratic.Point));
                    return true;
            }
        }

        return false;
    }

    private static PptPoint FirstDistinctPoint(PptPoint from, params PptPoint[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (Math.Abs(candidate.X - from.X) > 1 || Math.Abs(candidate.Y - from.Y) > 1)
            {
                return candidate;
            }
        }

        return candidates.Length > 0 ? candidates[^1] : from;
    }

    private static bool TryGetVector(PptxShape shape, bool atEnd, out PptPoint from, out PptPoint to)
    {
        from = new PptPoint(0, 0);
        to = new PptPoint(0, 0);
        if (shape.Kind == PptxShapeKind.Line)
        {
            var start = LineStart(shape);
            var end = LineEnd(shape);
            from = atEnd ? start : end;
            to = atEnd ? end : start;
            return true;
        }

        var current = new PptPoint(shape.X, shape.Y);
        var firstSegmentFound = false;
        var lastFrom = current;
        var lastTo = current;

        foreach (var command in shape.PathCommands)
        {
            switch (command)
            {
                case PptxMoveTo move:
                    current = Absolute(shape, move.Point);
                    break;
                case PptxLineTo line:
                    lastFrom = current;
                    lastTo = Absolute(shape, line.Point);
                    current = lastTo;
                    firstSegmentFound = CaptureFirst(atEnd, ref from, ref to, lastFrom, lastTo, firstSegmentFound);
                    break;
                case PptxCubicBezierTo cubic:
                    lastFrom = Absolute(shape, cubic.Control2);
                    lastTo = Absolute(shape, cubic.Point);
                    current = lastTo;
                    firstSegmentFound = CaptureFirst(atEnd, ref from, ref to, lastFrom, lastTo, firstSegmentFound);
                    break;
                case PptxQuadraticBezierTo quadratic:
                    lastFrom = Absolute(shape, quadratic.Control);
                    lastTo = Absolute(shape, quadratic.Point);
                    current = lastTo;
                    firstSegmentFound = CaptureFirst(atEnd, ref from, ref to, lastFrom, lastTo, firstSegmentFound);
                    break;
            }
        }

        if (atEnd && firstSegmentFound)
        {
            from = lastFrom;
            to = lastTo;
        }

        return firstSegmentFound;
    }

    private static PptPoint LineStart(PptxShape shape) =>
        new(
            shape.FlipH ? shape.X + shape.Cx : shape.X,
            shape.FlipV ? shape.Y + shape.Cy : shape.Y);

    private static PptPoint LineEnd(PptxShape shape) =>
        new(
            shape.FlipH ? shape.X : shape.X + shape.Cx,
            shape.FlipV ? shape.Y : shape.Y + shape.Cy);

    private static bool CaptureFirst(bool atEnd, ref PptPoint from, ref PptPoint to, PptPoint segmentFrom, PptPoint segmentTo, bool alreadyFound)
    {
        if (!atEnd && !alreadyFound)
        {
            from = segmentTo;
            to = segmentFrom;
        }

        return true;
    }

    private static PptPoint Absolute(PptxShape shape, PptPoint point) =>
        new(shape.X + point.X, shape.Y + point.Y);

    private static PptxShape EmptyShape(int shapeId) =>
        new()
        {
            Id = shapeId,
            Name = $"Empty {shapeId}",
            Kind = PptxShapeKind.Custom,
            X = 0,
            Y = 0,
            Cx = 1,
            Cy = 1,
            PathWidth = 1,
            PathHeight = 1,
            PathCommands = [],
            PreferNoFill = true,
            PreferNoLine = true
        };

    private static IReadOnlyList<PptxPathCommand> ToClosedPolygon(IReadOnlyList<SvgPoint> points, SvgViewportMap viewport)
    {
        var segments = new List<SvgPathSegment> { new MoveTo(points[0]) };
        segments.AddRange(points.Skip(1).Select(point => new LineTo(point)));
        segments.Add(new ClosePath());
        return MapSegments(segments, viewport);
    }

    private static IReadOnlyList<SvgPathSegment> TransformSegments(IReadOnlyList<SvgPathSegment> segments, AffineMatrix transform)
    {
        var result = new List<SvgPathSegment>(segments.Count);
        foreach (var segment in segments)
        {
            result.Add(segment switch
            {
                MoveTo move => new MoveTo(transform.Transform(move.Point)),
                LineTo line => new LineTo(transform.Transform(line.Point)),
                CubicBezierTo cubic => new CubicBezierTo(
                    transform.Transform(cubic.Control1),
                    transform.Transform(cubic.Control2),
                    transform.Transform(cubic.Point)),
                QuadraticBezierTo quadratic => new QuadraticBezierTo(
                    transform.Transform(quadratic.Control),
                    transform.Transform(quadratic.Point)),
                ClosePath => new ClosePath(),
                _ => segment
            });
        }

        return result;
    }

    private static IReadOnlyList<PptxPathCommand> MapSegments(IReadOnlyList<SvgPathSegment> segments, SvgViewportMap viewport)
    {
        var result = new List<PptxPathCommand>(segments.Count);
        foreach (var segment in segments)
        {
            result.Add(segment switch
            {
                MoveTo move => new PptxMoveTo(viewport.Map(move.Point)),
                LineTo line => new PptxLineTo(viewport.Map(line.Point)),
                CubicBezierTo cubic => new PptxCubicBezierTo(
                    viewport.Map(cubic.Control1),
                    viewport.Map(cubic.Control2),
                    viewport.Map(cubic.Point)),
                QuadraticBezierTo quadratic => new PptxQuadraticBezierTo(
                    viewport.Map(quadratic.Control),
                    viewport.Map(quadratic.Point)),
                ClosePath => new PptxClosePath(),
                _ => throw new InvalidOperationException($"Unsupported path segment {segment.GetType().Name}.")
            });
        }

        return result;
    }

    private static IEnumerable<PptPoint> ExtractPoints(IEnumerable<PptxPathCommand> commands)
    {
        foreach (var command in commands)
        {
            switch (command)
            {
                case PptxMoveTo move:
                    yield return move.Point;
                    break;
                case PptxLineTo line:
                    yield return line.Point;
                    break;
                case PptxCubicBezierTo cubic:
                    yield return cubic.Control1;
                    yield return cubic.Control2;
                    yield return cubic.Point;
                    break;
                case PptxQuadraticBezierTo quadratic:
                    yield return quadratic.Control;
                    yield return quadratic.Point;
                    break;
            }
        }
    }

    private static IReadOnlyList<PptxPathCommand> NormalizeCommands(IEnumerable<PptxPathCommand> commands, long x, long y)
    {
        static PptPoint Normalize(PptPoint point, long x, long y) => new(point.X - x, point.Y - y);

        return commands.Select<PptxPathCommand, PptxPathCommand>(command => command switch
        {
            PptxMoveTo move => new PptxMoveTo(Normalize(move.Point, x, y)),
            PptxLineTo line => new PptxLineTo(Normalize(line.Point, x, y)),
            PptxCubicBezierTo cubic => new PptxCubicBezierTo(
                Normalize(cubic.Control1, x, y),
                Normalize(cubic.Control2, x, y),
                Normalize(cubic.Point, x, y)),
            PptxQuadraticBezierTo quadratic => new PptxQuadraticBezierTo(
                Normalize(quadratic.Control, x, y),
                Normalize(quadratic.Point, x, y)),
            PptxClosePath => new PptxClosePath(),
            _ => throw new InvalidOperationException($"Unsupported path command {command.GetType().Name}.")
        }).ToArray();
    }

    private static (long X, long Y, long Cx, long Cy) Bounds(IReadOnlyList<PptPoint> points)
    {
        if (points.Count == 0)
        {
            return (0, 0, 1, 1);
        }

        var minX = points.Min(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxX = points.Max(point => point.X);
        var maxY = points.Max(point => point.Y);
        return (minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }

    private static bool IsAxisAligned(IReadOnlyList<PptPoint> points)
    {
        if (points.Count != 4)
        {
            return false;
        }

        var distinctX = points.Select(point => point.X).Distinct().Count();
        var distinctY = points.Select(point => point.Y).Distinct().Count();
        return distinctX == 2 && distinctY == 2;
    }

    private static bool IsNone(string? color) =>
        string.IsNullOrWhiteSpace(color) || color.Equals("none", StringComparison.OrdinalIgnoreCase);

    private static int RoundRectAdjust(SvgRectElement rect)
    {
        var radius = Math.Max(rect.Rx, rect.Ry);
        if (radius <= 0)
        {
            radius = Math.Min(rect.Width, rect.Height) / 6d;
        }

        var adjust = radius / Math.Max(0.01, Math.Min(rect.Width, rect.Height)) * 100000d;
        return (int)Math.Round(Math.Max(0, Math.Min(50000, adjust)));
    }

    private static bool HasMarker(string? marker) =>
        !string.IsNullOrWhiteSpace(marker) &&
        !marker.Equals("none", StringComparison.OrdinalIgnoreCase);

    private static bool UsesComplexMarker(string? markerRef, IReadOnlyDictionary<string, SvgMarker> markers) =>
        HasMarker(markerRef) &&
        TryGetMarker(markers, markerRef, out var marker) &&
        !IsSimpleTriangleMarker(marker);

    private static bool IsSimpleTriangleMarker(SvgMarker marker)
    {
        if (marker.Id.Contains("_er-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (marker.Id.Contains("_class-extension", StringComparison.OrdinalIgnoreCase) ||
            marker.Id.Contains("class-extension", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (marker.Elements.Count != 1)
        {
            return false;
        }

        return marker.Elements[0] switch
        {
            SvgPathElement path => IsTrianglePath(path.Segments),
            SvgPolylineElement polyline => polyline.Closed && polyline.Points.Count == 3,
            _ => false
        };
    }

    private static bool IsTrianglePath(IReadOnlyList<SvgPathSegment> segments) =>
        segments.Count == 4 &&
        segments[0] is MoveTo &&
        segments[1] is LineTo &&
        segments[2] is LineTo &&
        segments[3] is ClosePath;

    private static string MermaidName(SvgPathElement path, int shapeId)
    {
        if (path.Classes.Contains("edgePath") || path.Classes.Contains("flowchart-link"))
        {
            return $"Mermaid Edge {shapeId}";
        }

        return $"Path {shapeId}";
    }
}
