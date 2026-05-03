using Mermaid2Pptx;

namespace SmartFactoryDeck;

internal static partial class SmartFactoryDeckTool
{
    static PptxSlideModel NewSlide() =>
        new()
        {
            WidthEmu = UnitConversion.InchesToEmu(SlideWidthInches),
            HeightEmu = UnitConversion.InchesToEmu(SlideHeightInches)
        };

    static PptxSlideModel StandardSlide(int index, string kicker, string title)
    {
        var slide = NewSlide();
        AddRect(slide, "slide-bg", 0, 0, SlideWidthInches, SlideHeightInches, Palette.Background, "none");
        AddText(slide, $"kicker-{index}", kicker, 0.72, 0.34, 2.4, 0.2, 10.5, Palette.Teal, "700");
        AddText(slide, $"title-{index}", title, 0.72, 0.62, 11.8, 0.43, 24.5, Palette.Ink, "700");
        AddLine(slide, $"title-rule-{index}", 0.72, 1.08, 12.55, 1.08, "#D9E4E1", 1);
        return slide;
    }

    static void AddFooter(PptxSlideModel slide, int index)
    {
        AddLine(slide, $"footer-rule-{index}", 0.72, 7.12, 12.55, 7.12, "#DDE7E3", 0.8);
        AddText(slide, $"footer-left-{index}", "3D 智慧工廠即時平台", 0.72, 7.23, 3.6, 0.16, 8.5, "#78908A", "400");
        AddText(slide, $"footer-num-{index}", $"{index:00} / 18", 11.52, 7.23, 1.0, 0.16, 8.5, "#78908A", "400", "r");
    }

    static void AddFactoryGrid(PptxSlideModel slide)
    {
        for (var i = 0; i < 6; i++)
        {
            AddLine(slide, $"cover-grid-h-{i}", 7.3, 1.0 + i * 0.86, 12.9, 0.55 + i * 0.86, "#72BFB7", 0.8);
            AddLine(slide, $"cover-grid-v-{i}", 7.8 + i * 0.85, 0.72, 7.25 + i * 0.85, 6.6, "#72BFB7", 0.8);
        }
        for (var i = 0; i < 5; i++)
        {
            AddRect(slide, $"cover-node-{i}", 8.05 + i * 0.82, 2.2 + (i % 2) * 0.68, 0.42, 0.42, i % 2 == 0 ? Palette.Mint : Palette.SoftAmber, "none", radius: true);
        }
    }

    static void AddSimpleTable(PptxSlideModel slide, double x, double y, double width, IReadOnlyList<double> colWidths, IReadOnlyList<(string, string, string)> rows, IReadOnlyList<string> header)
    {
        var rowHeight = 1.05;
        var headerHeight = 0.55;
        var currentX = x;
        for (var c = 0; c < header.Count; c++)
        {
            AddRect(slide, $"table-header-{c}", currentX, y, colWidths[c], headerHeight, Palette.Deep, Palette.Deep);
            AddText(slide, $"table-header-text-{c}", header[c], currentX + 0.1, y + 0.17, colWidths[c] - 0.2, 0.18, 11.5, Palette.White, "700", "ctr");
            currentX += colWidths[c];
        }
        for (var r = 0; r < rows.Count; r++)
        {
            currentX = x;
            var values = new[] { rows[r].Item1, rows[r].Item2, rows[r].Item3 };
            for (var c = 0; c < values.Length; c++)
            {
                AddRect(slide, $"table-cell-{r}-{c}", currentX, y + headerHeight + r * rowHeight, colWidths[c], rowHeight, r % 2 == 0 ? Palette.White : "#F2F7F5", "#D3E0DC");
                AddText(slide, $"table-text-{r}-{c}", values[c], currentX + 0.14, y + headerHeight + r * rowHeight + 0.18, colWidths[c] - 0.28, rowHeight - 0.28, c == 0 ? 13 : 11.5, c == 0 ? Palette.Teal : Palette.Ink, c == 0 ? "700" : "600", c == 0 ? "ctr" : "l");
                currentX += colWidths[c];
            }
        }
    }

    static void AddComparisonTable(PptxSlideModel slide, double x, double y, double width, IReadOnlyList<(string, string, string, string)> rows)
    {
        var colWidths = new[] { 1.45, 2.55, 3.15, width - 1.45 - 2.55 - 3.15 };
        var headers = new[] { "系統", "強項", "限制", "平台補足" };
        var headerHeight = 0.48;
        var rowHeight = 0.96;
        var currentX = x;
        for (var c = 0; c < headers.Length; c++)
        {
            AddRect(slide, $"diff-header-{c}", currentX, y, colWidths[c], headerHeight, Palette.Deep, Palette.Deep);
            AddText(slide, $"diff-header-text-{c}", headers[c], currentX + 0.08, y + 0.15, colWidths[c] - 0.16, 0.16, 10.5, Palette.White, "700", "ctr");
            currentX += colWidths[c];
        }
        for (var r = 0; r < rows.Count; r++)
        {
            currentX = x;
            var values = new[] { rows[r].Item1, rows[r].Item2, rows[r].Item3, rows[r].Item4 };
            for (var c = 0; c < values.Length; c++)
            {
                var fill = c == 3 ? Palette.SoftMint : r % 2 == 0 ? Palette.White : "#F2F7F5";
                AddRect(slide, $"diff-cell-{r}-{c}", currentX, y + headerHeight + r * rowHeight, colWidths[c], rowHeight, fill, "#D3E0DC");
                AddText(slide, $"diff-text-{r}-{c}", values[c], currentX + 0.13, y + headerHeight + r * rowHeight + 0.18, colWidths[c] - 0.25, rowHeight - 0.26, c == 0 ? 12.5 : 11.2, c == 0 || c == 3 ? Palette.Teal : Palette.Ink, c == 0 || c == 3 ? "700" : "600", c == 0 ? "ctr" : "l");
                currentX += colWidths[c];
            }
        }
    }

    static void AddPill(PptxSlideModel slide, string name, string text, double x, double y, double width, string fill, string accent)
    {
        AddRect(slide, $"{name}-bg", x, y, width, 0.46, fill, accent, radius: true);
        AddText(slide, $"{name}-text", text, x + 0.08, y + 0.14, width - 0.16, 0.16, 10.8, Palette.Ink, "700", "ctr", noWrap: true);
    }

    static void AddDiagram(PptxSlideModel slide, PptxSlideModel diagram, double? maxBottomInches = null)
    {
        var maxBottom = maxBottomInches.HasValue ? Inches(maxBottomInches.Value) : long.MaxValue;
        foreach (var shape in diagram.Shapes)
        {
            slide.Shapes.Add(TrimDiagramShape(shape, maxBottom));
        }

        slide.Warnings.AddRange(diagram.Warnings);
    }

    static PptxShape TrimDiagramShape(PptxShape shape, long maxBottom)
    {
        if (shape.Kind != PptxShapeKind.Line || shape.Y + shape.Cy <= maxBottom)
        {
            return shape;
        }

        var trimmedCy = Math.Max(1, maxBottom - shape.Y);
        return new PptxShape
        {
            Id = shape.Id,
            Name = shape.Name,
            Kind = shape.Kind,
            X = shape.X,
            Y = shape.Y,
            Cx = shape.Cx,
            Cy = trimmedCy,
            PresetGeometry = shape.PresetGeometry,
            PresetAdjustValue = shape.PresetAdjustValue,
            PathCommands = shape.PathCommands,
            PathWidth = shape.PathWidth,
            PathHeight = shape.PathHeight,
            Style = shape.Style,
            LineWidthEmu = shape.LineWidthEmu,
            Text = shape.Text,
            TextAlignment = shape.TextAlignment,
            NoWrapText = shape.NoWrapText,
            PreferNoFill = shape.PreferNoFill,
            PreferNoLine = shape.PreferNoLine,
            ArrowStart = shape.ArrowStart,
            ArrowEnd = shape.ArrowEnd,
            FlipH = shape.FlipH,
            FlipV = shape.FlipV
        };
    }

    static void AddText(
        PptxSlideModel slide,
        string name,
        string text,
        double x,
        double y,
        double width,
        double height,
        double fontPt,
        string color,
        string weight = "400",
        string align = "l",
        bool noWrap = false)
    {
        slide.Shapes.Add(new PptxShape
        {
            Name = name,
            Kind = PptxShapeKind.Text,
            PresetGeometry = "rect",
            X = Inches(x),
            Y = Inches(y),
            Cx = Inches(width),
            Cy = Inches(height),
            Style = new SvgStyle
            {
                Fill = "none",
                Stroke = "none",
                Color = color,
                FontFamily = "Microsoft JhengHei",
                FontSize = fontPt * 96d / 72d,
                FontWeight = weight
            },
            Text = text,
            TextAlignment = align,
            NoWrapText = noWrap,
            PreferNoFill = true,
            PreferNoLine = true
        });
    }

    static void AddRect(
        PptxSlideModel slide,
        string name,
        double x,
        double y,
        double width,
        double height,
        string fill,
        string stroke,
        double strokeWidthPx = 1,
        bool radius = false,
        double fillOpacity = 1)
    {
        slide.Shapes.Add(new PptxShape
        {
            Name = name,
            Kind = PptxShapeKind.Preset,
            PresetGeometry = radius ? "roundRect" : "rect",
            PresetAdjustValue = radius ? 12000 : null,
            X = Inches(x),
            Y = Inches(y),
            Cx = Inches(width),
            Cy = Inches(height),
            Style = new SvgStyle
            {
                Fill = fill,
                Stroke = stroke,
                StrokeWidth = strokeWidthPx,
                FillOpacity = fillOpacity,
                FontFamily = "Microsoft JhengHei"
            },
            PreferNoLine = string.Equals(stroke, "none", StringComparison.OrdinalIgnoreCase)
        });
    }

    static void AddLine(
        PptxSlideModel slide,
        string name,
        double x1,
        double y1,
        double x2,
        double y2,
        string color,
        double strokeWidthPx,
        bool arrowEnd = false,
        bool dashed = false)
    {
        slide.Shapes.Add(new PptxShape
        {
            Name = name,
            Kind = PptxShapeKind.Line,
            X = Inches(Math.Min(x1, x2)),
            Y = Inches(Math.Min(y1, y2)),
            Cx = Inches(Math.Abs(x2 - x1)),
            Cy = Inches(Math.Abs(y2 - y1)),
            Style = new SvgStyle
            {
                Fill = "none",
                Stroke = color,
                StrokeWidth = strokeWidthPx,
                StrokeDashArray = dashed ? "6 6" : null
            },
            ArrowEnd = arrowEnd,
            FlipH = x2 < x1,
            FlipV = y2 < y1
        });
    }

    static long Inches(double value) => UnitConversion.InchesToEmu(value);
}
