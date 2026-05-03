using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security;
using System.Text;
using System.Text.Json;
using Mermaid2Pptx;
using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

var options = QaOptions.Parse(args);
Directory.CreateDirectory(options.OutputDirectory);
CleanOutputDirectory(options.OutputDirectory);

var extractor = new MermaidSvgExtractor();
var extracted = await extractor.ExtractAsync(options.HtmlPath, options.SlideSelector, options.SvgSelector);
if (extracted.Count == 0)
{
    throw new InvalidOperationException("No rendered SVG found for visual QA.");
}

var parser = new SvgDocumentParser();
var mapper = new SvgToPowerPointMapper();
var referenceHtmlPath = Path.GetFullPath(options.HtmlPath);
var reportPath = Path.Combine(options.OutputDirectory, "report.json");
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
var items = new List<QaItem>();

for (var i = 0; i < extracted.Count; i++)
{
    var item = extracted[i];
    var scene = parser.Parse(item.SvgMarkup);
    var slide = mapper.MapSvgToSlide(scene, options.WidthInches, options.HeightInches, item);
    var slug = $"slide-{item.SourceSlideIndex + 1:00}-svg-{item.SvgIndex + 1:00}";
    var referenceSvgPath = Path.Combine(options.OutputDirectory, $"{slug}-reference.svg");
    var convertedSvgPath = Path.Combine(options.OutputDirectory, $"{slug}-converted-preview.svg");
    var convertedHtmlPath = Path.Combine(options.OutputDirectory, $"{slug}-converted-preview.html");
    var referencePngPath = Path.Combine(options.OutputDirectory, $"{slug}-reference.png");
    var convertedPngPath = Path.Combine(options.OutputDirectory, $"{slug}-converted.png");
    var diffPngPath = Path.Combine(options.OutputDirectory, $"{slug}-diff.png");
    var pptxSlideXmlPath = Path.Combine(options.OutputDirectory, $"{slug}-pptx-slide.xml");
    var xmlAuditPath = Path.Combine(options.OutputDirectory, $"{slug}-xml-audit.json");
    var previewSvg = PptxPreviewSvgWriter.Write(slide, options.ViewportWidth, options.ViewportHeight);
    var pptxSlideXml = BuildAuditSlideXml(slide, options);
    var xmlAudit = new PptxXmlAudit().Compare(scene, slide, pptxSlideXml);

    await File.WriteAllTextAsync(referenceSvgPath, item.SvgMarkup);
    await File.WriteAllTextAsync(convertedSvgPath, previewSvg);
    await File.WriteAllTextAsync(convertedHtmlPath, HtmlWrap(previewSvg));
    await File.WriteAllTextAsync(pptxSlideXmlPath, pptxSlideXml);
    await File.WriteAllTextAsync(xmlAuditPath, JsonSerializer.Serialize(xmlAudit, jsonOptions));

    items.Add(new QaItem(
        item.SourceSlideIndex,
        item.SvgIndex,
        slug,
        slide,
        scene.Warnings.ToArray(),
        referenceSvgPath,
        convertedSvgPath,
        convertedHtmlPath,
        referencePngPath,
        convertedPngPath,
        diffPngPath,
        pptxSlideXmlPath,
        xmlAuditPath,
        xmlAudit));
}

using var playwright = await Playwright.CreateAsync();
await using var browser = await LaunchBrowserAsync(playwright);
var page = await browser.NewPageAsync(new BrowserNewPageOptions
{
    ViewportSize = new ViewportSize { Width = options.ViewportWidth, Height = options.ViewportHeight },
    DeviceScaleFactor = 1
});

await page.GotoAsync(new Uri(referenceHtmlPath).AbsoluteUri, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
await page.WaitForFunctionAsync(
    @"selector => {
        const svgs = Array.from(document.querySelectorAll(selector));
        const pendingMermaid = document.querySelector('.mermaid:not([data-processed=""true""])');
        return svgs.length > 0 && !pendingMermaid;
    }",
    options.SvgSelector);
var slideLocator = page.Locator(options.SlideSelector);
var slideCount = await slideLocator.CountAsync();
var itemReports = new List<QaItemReport>();

foreach (var item in items)
{
    var locator = slideCount > item.SourceSlideIndex
        ? slideLocator.Nth(item.SourceSlideIndex)
        : page.Locator("body");
    var diagramName = await ReadDiagramNameAsync(locator, item);

    await locator.ScreenshotAsync(new LocatorScreenshotOptions { Path = item.ReferencePngPath });
    await page.GotoAsync(new Uri(Path.GetFullPath(item.ConvertedHtmlPath)).AbsoluteUri, new PageGotoOptions { WaitUntil = WaitUntilState.Load });
    await page.ScreenshotAsync(new PageScreenshotOptions { Path = item.ConvertedPngPath, FullPage = false });
    await page.GotoAsync(new Uri(referenceHtmlPath).AbsoluteUri, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
    await page.WaitForFunctionAsync(
        @"selector => {
            const svgs = Array.from(document.querySelectorAll(selector));
            const pendingMermaid = document.querySelector('.mermaid:not([data-processed=""true""])');
            return svgs.length > 0 && !pendingMermaid;
        }",
        options.SvgSelector);
    slideLocator = page.Locator(options.SlideSelector);

    var diff = ImageDiff.Compare(item.ReferencePngPath, item.ConvertedPngPath, item.DiffPngPath);
    itemReports.Add(new QaItemReport(
        item.SourceSlideIndex + 1,
        item.SvgIndex + 1,
        diagramName,
        item.ReferencePngPath,
        item.ReferenceSvgPath,
        item.ConvertedPngPath,
        item.ConvertedSvgPath,
        item.DiffPngPath,
        item.PptxSlideXmlPath,
        item.XmlAuditPath,
        diff.MeanDelta,
        diff.ChangedPixelRatio,
        diff.MaxDelta,
        item.Slide.Shapes.Count,
        item.XmlAudit.FailureCount,
        AuditFailureSummaries(item.XmlAudit),
        item.Warnings));

    Console.WriteLine($"{diagramName}: changedPixelRatio={diff.ChangedPixelRatio:P2}, meanDelta={diff.MeanDelta:F4}, shapes={item.Slide.Shapes.Count}, xmlAuditFailures={item.XmlAudit.FailureCount}");
}

var report = new QaReport(
    referenceHtmlPath,
    itemReports.Count,
    itemReports.Count == 0 ? 0 : itemReports.Average(item => item.MeanDelta),
    itemReports.Count == 0 ? 0 : itemReports.Average(item => item.ChangedPixelRatio),
    itemReports.Count == 0 ? 0 : itemReports.Max(item => item.ChangedPixelRatio),
    itemReports.Sum(item => item.NativeShapes),
    itemReports);
await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, jsonOptions));

Console.WriteLine($"items: {itemReports.Count}");
Console.WriteLine($"averageChangedPixelRatio: {report.AverageChangedPixelRatio:P2}");
Console.WriteLine($"worstChangedPixelRatio: {report.WorstChangedPixelRatio:P2}");
Console.WriteLine($"nativeShapes: {report.NativeShapes}");
Console.WriteLine($"report: {reportPath}");

static string BuildAuditSlideXml(PptxSlideModel slide, QaOptions options)
{
    var deck = new PptxDeckModel { WidthInches = options.WidthInches, HeightInches = options.HeightInches };
    deck.Slides.Add(slide);
    var tempPptx = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-audit-{Guid.NewGuid():N}.pptx");
    try
    {
        new DrawingMlWriter().Write(deck, tempPptx);
        using var zip = ZipFile.OpenRead(tempPptx);
        return ReadZipEntry(zip, "ppt/slides/slide1.xml");
    }
    finally
    {
        if (File.Exists(tempPptx))
        {
            File.Delete(tempPptx);
        }
    }
}

static string ReadZipEntry(ZipArchive zip, string path)
{
    var entry = zip.GetEntry(path) ?? throw new InvalidOperationException($"Missing zip entry {path}.");
    using var stream = entry.Open();
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

static IReadOnlyList<string> AuditFailureSummaries(PptxXmlAudit.PptxXmlAuditReport audit) =>
    audit.Items
        .Where(item => !item.Passed)
        .Select(item => $"svg[{item.Svg.Index}] <{item.Svg.Tag}> -> {item.PptxShapeName ?? "(missing)"}: {string.Join("; ", item.Failures)}")
        .ToArray();

static async Task<string> ReadDiagramNameAsync(ILocator locator, QaItem item)
{
    try
    {
        var name = await locator.EvaluateAsync<string?>(
            "el => el.getAttribute('data-diagram') || el.querySelector('.mermaid')?.getAttribute('data-diagram') || ''");
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }
    }
    catch
    {
        // A missing locator should not stop visual QA; the indexed fallback is enough for reports.
    }

    return item.Slug;
}

static async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright)
{
    try
    {
        return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }
    catch (PlaywrightException exception) when (LooksLikeMissingBundledChromium(exception))
    {
        foreach (var channel in new[] { "msedge", "chrome" })
        {
            try
            {
                Console.Error.WriteLine($"Bundled Playwright Chromium was not found; trying local browser channel '{channel}'.");
                return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    Channel = channel
                });
            }
            catch (PlaywrightException)
            {
                // Try the next local browser channel.
            }
        }

        throw;
    }
}

static bool LooksLikeMissingBundledChromium(PlaywrightException exception)
{
    var message = exception.Message;
    return message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) ||
           message.Contains("Please run the following command to download new browsers", StringComparison.OrdinalIgnoreCase);
}

static string HtmlWrap(string svg) =>
    $$"""
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <style>
    html, body { margin: 0; width: 100%; height: 100%; overflow: hidden; background: white; }
    svg { display: block; width: 100vw; height: 100vh; }
  </style>
</head>
<body>
{{svg}}
</body>
</html>
""";

static void CleanOutputDirectory(string outputDirectory)
{
    foreach (var path in Directory.EnumerateFiles(outputDirectory))
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith("slide-", StringComparison.OrdinalIgnoreCase) ||
            name is "reference.png" or "converted.png" or "diff.png" or "reference.svg" or
                "converted-preview.svg" or "converted-preview.html" or "report.json")
        {
            File.Delete(path);
        }
    }
}

internal sealed record QaOptions(
    string HtmlPath,
    string OutputDirectory,
    string SlideSelector,
    string SvgSelector,
    double WidthInches,
    double HeightInches,
    int ViewportWidth,
    int ViewportHeight)
{
    public static QaOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = args[i][2..];
            values[key] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : "true";
        }

        return new QaOptions(
            Value(values, "html", "samples/sample.html"),
            Value(values, "out", "out/visual-qa"),
            Value(values, "slide-selector", ".slide"),
            Value(values, "svg-selector", "svg"),
            DoubleValue(values, "width", 13.333),
            DoubleValue(values, "height", 7.5),
            IntValue(values, "viewport-width", 1280),
            IntValue(values, "viewport-height", 720));
    }

    private static string Value(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) ? value : fallback;

    private static double DoubleValue(IReadOnlyDictionary<string, string> values, string key, double fallback) =>
        values.TryGetValue(key, out var value) && double.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static int IntValue(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
}

internal sealed record QaItem(
    int SourceSlideIndex,
    int SvgIndex,
    string Slug,
    PptxSlideModel Slide,
    IReadOnlyList<string> Warnings,
    string ReferenceSvgPath,
    string ConvertedSvgPath,
    string ConvertedHtmlPath,
    string ReferencePngPath,
    string ConvertedPngPath,
    string DiffPngPath,
    string PptxSlideXmlPath,
    string XmlAuditPath,
    PptxXmlAudit.PptxXmlAuditReport XmlAudit);

internal sealed record QaItemReport(
    int Slide,
    int Svg,
    string Diagram,
    string Reference,
    string ReferenceSvg,
    string Converted,
    string ConvertedSvg,
    string Diff,
    string PptxSlideXml,
    string XmlAudit,
    double MeanDelta,
    double ChangedPixelRatio,
    double MaxDelta,
    int NativeShapes,
    int XmlAuditFailures,
    IReadOnlyList<string> XmlAuditFailureSummaries,
    IReadOnlyList<string> Warnings);

internal sealed record QaReport(
    string Html,
    int ItemCount,
    double AverageMeanDelta,
    double AverageChangedPixelRatio,
    double WorstChangedPixelRatio,
    int NativeShapes,
    IReadOnlyList<QaItemReport> Items);

internal static class PptxPreviewSvgWriter
{
    public static string Write(PptxSlideModel slide, int width, int height)
    {
        var scaleX = width / (double)slide.WidthEmu;
        var scaleY = height / (double)slide.HeightEmu;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">""");
        sb.Append("""<rect x="0" y="0" width="100%" height="100%" fill="#ffffff"/>""");

        foreach (var shape in slide.Shapes)
        {
            sb.Append(Shape(shape, scaleX, scaleY));
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string Shape(PptxShape shape, double scaleX, double scaleY)
    {
        var x = shape.X * scaleX;
        var y = shape.Y * scaleY;
        var rawCx = shape.Cx * scaleX;
        var rawCy = shape.Cy * scaleY;
        var cx = Math.Max(1, rawCx);
        var cy = Math.Max(1, rawCy);
        var paint = Paint(shape);

        if (shape.Kind == PptxShapeKind.Text)
        {
            return Text(shape, x, y, cx, cy);
        }

        if (shape.Kind == PptxShapeKind.Line)
        {
            return Line(shape, x, y, rawCx, rawCy, paint);
        }

        if (shape.Kind == PptxShapeKind.Preset)
        {
            return shape.PresetGeometry switch
            {
                "ellipse" => Svg(
                    "ellipse",
                    $$"""cx="{{F(x + cx / 2)}}" cy="{{F(y + cy / 2)}}" rx="{{F(cx / 2)}}" ry="{{F(cy / 2)}}" {{paint}}"""),
                "roundRect" => Svg(
                    "rect",
                    $$"""x="{{F(x)}}" y="{{F(y)}}" width="{{F(cx)}}" height="{{F(cy)}}" rx="{{F(Math.Min(cx, cy) * (shape.PresetAdjustValue ?? 16667) / 100000d)}}" ry="{{F(Math.Min(cx, cy) * (shape.PresetAdjustValue ?? 16667) / 100000d)}}" {{paint}}"""),
                _ => Svg("rect", $$"""x="{{F(x)}}" y="{{F(y)}}" width="{{F(cx)}}" height="{{F(cy)}}" {{paint}}""")
            };
        }

        return $$"""<path d="{{PathData(shape, x, y, scaleX, scaleY)}}" {{paint}}/>{{Arrow(shape, x, y, scaleX, scaleY)}}""";
    }

    private static string Line(PptxShape shape, double x, double y, double cx, double cy, string paint)
    {
        var (start, end) = LineVector(shape, x, y, cx, cy);
        var arrows = new StringBuilder();
        if (shape.ArrowEnd)
        {
            arrows.Append(ArrowPolygon(shape, start, end));
        }

        if (shape.ArrowStart)
        {
            arrows.Append(ArrowPolygon(shape, end, start));
        }

        return $$"""<line x1="{{F(start.X)}}" y1="{{F(start.Y)}}" x2="{{F(end.X)}}" y2="{{F(end.Y)}}" {{paint}}/>{{arrows}}""";
    }

    private static (SvgPoint Start, SvgPoint End) LineVector(PptxShape shape, double x, double y, double cx, double cy)
    {
        var start = new SvgPoint(
            shape.FlipH ? x + cx : x,
            shape.FlipV ? y + cy : y);
        var end = new SvgPoint(
            shape.FlipH ? x : x + cx,
            shape.FlipV ? y : y + cy);
        return (start, end);
    }

    private static string Text(PptxShape shape, double x, double y, double cx, double cy)
    {
        var fontPx = Math.Max(8, shape.Style.FontSize);
        var color = ResolvePaint(shape.Style.Color) ?? ResolvePaint(shape.Style.Fill) ?? "#000000";
        var anchor = shape.TextAlignment switch
        {
            "ctr" => "middle",
            "r" => "end",
            _ => "start"
        };
        var textX = shape.TextAlignment switch
        {
            "ctr" => x + cx / 2,
            "r" => x + cx,
            _ => x
        };
        var lines = (shape.Text ?? string.Empty).Split(Environment.NewLine);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"""<text x="{F(textX)}" y="{F(y + fontPx)}" fill="{color}" font-size="{F(fontPx)}" font-family="{Esc(shape.Style.FontFamily ?? "Arial")}" text-anchor="{anchor}">""");
        for (var i = 0; i < lines.Length; i++)
        {
            var dy = i == 0 ? 0 : fontPx * 1.2;
            sb.Append(CultureInfo.InvariantCulture, $"""<tspan x="{F(textX)}" dy="{F(dy)}">{Esc(lines[i])}</tspan>""");
        }
        sb.Append("</text>");
        return sb.ToString();
    }

    private static string Paint(PptxShape shape)
    {
        var fill = shape.PreferNoFill ? "none" : ResolvePaint(shape.Style.Fill) ?? "none";
        var stroke = shape.PreferNoLine ? "none" : ResolvePaint(shape.Style.Stroke) ?? "none";
        var strokeWidth = Math.Max(0.5, shape.Style.StrokeWidth);
        var dash = string.IsNullOrWhiteSpace(shape.Style.StrokeDashArray) || shape.Style.StrokeDashArray.Trim() is "0" or "0 0"
            ? string.Empty
            : $" stroke-dasharray=\"{Esc(shape.Style.StrokeDashArray)}\"";
        return $"fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{F(strokeWidth)}\"{dash}";
    }

    private static string PathData(PptxShape shape, double x, double y, double scaleX, double scaleY)
    {
        var sb = new StringBuilder();
        foreach (var command in shape.PathCommands)
        {
            switch (command)
            {
                case PptxMoveTo move:
                    sb.Append(CultureInfo.InvariantCulture, $"M {F(x + move.Point.X * scaleX)} {F(y + move.Point.Y * scaleY)} ");
                    break;
                case PptxLineTo line:
                    sb.Append(CultureInfo.InvariantCulture, $"L {F(x + line.Point.X * scaleX)} {F(y + line.Point.Y * scaleY)} ");
                    break;
                case PptxCubicBezierTo cubic:
                    sb.Append(CultureInfo.InvariantCulture, $"C {F(x + cubic.Control1.X * scaleX)} {F(y + cubic.Control1.Y * scaleY)} {F(x + cubic.Control2.X * scaleX)} {F(y + cubic.Control2.Y * scaleY)} {F(x + cubic.Point.X * scaleX)} {F(y + cubic.Point.Y * scaleY)} ");
                    break;
                case PptxQuadraticBezierTo quadratic:
                    sb.Append(CultureInfo.InvariantCulture, $"Q {F(x + quadratic.Control.X * scaleX)} {F(y + quadratic.Control.Y * scaleY)} {F(x + quadratic.Point.X * scaleX)} {F(y + quadratic.Point.Y * scaleY)} ");
                    break;
                case PptxClosePath:
                    sb.Append("Z ");
                    break;
            }
        }

        return sb.ToString().Trim();
    }

    private static string Arrow(PptxShape shape, double x, double y, double scaleX, double scaleY)
    {
        if (!shape.ArrowEnd || shape.PathCommands.LastOrDefault(command => command is PptxLineTo or PptxCubicBezierTo or PptxQuadraticBezierTo) is not { } last)
        {
            return string.Empty;
        }

        var (from, to) = LastVector(shape.PathCommands);
        var end = new SvgPoint(x + to.X * scaleX, y + to.Y * scaleY);
        var start = new SvgPoint(x + from.X * scaleX, y + from.Y * scaleY);
        return ArrowPolygon(shape, start, end);
    }

    private static string ArrowPolygon(PptxShape shape, SvgPoint start, SvgPoint end)
    {
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        const double size = 11;
        var p1 = end;
        var p2 = new SvgPoint(end.X - size * Math.Cos(angle - Math.PI / 7), end.Y - size * Math.Sin(angle - Math.PI / 7));
        var p3 = new SvgPoint(end.X - size * Math.Cos(angle + Math.PI / 7), end.Y - size * Math.Sin(angle + Math.PI / 7));
        var color = ResolvePaint(shape.Style.Stroke) ?? "#333333";
        return $$"""<polygon points="{{F(p1.X)}},{{F(p1.Y)}} {{F(p2.X)}},{{F(p2.Y)}} {{F(p3.X)}},{{F(p3.Y)}}" fill="{{color}}" stroke="{{color}}"/>""";
    }

    private static (PptPoint From, PptPoint To) LastVector(IReadOnlyList<PptxPathCommand> commands)
    {
        var current = new PptPoint(0, 0);
        var previous = current;
        foreach (var command in commands)
        {
            switch (command)
            {
                case PptxMoveTo move:
                    current = move.Point;
                    break;
                case PptxLineTo line:
                    previous = current;
                    current = line.Point;
                    break;
                case PptxCubicBezierTo cubic:
                    previous = cubic.Control2;
                    current = cubic.Point;
                    break;
                case PptxQuadraticBezierTo quadratic:
                    previous = quadratic.Control;
                    current = quadratic.Point;
                    break;
            }
        }
        return (previous, current);
    }

    private static string? ResolvePaint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        value = value.Trim();
        if (value.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
        {
            return "#000000";
        }

        if (value.StartsWith("#", StringComparison.Ordinal))
        {
            return value;
        }

        return value.ToLowerInvariant() switch
        {
            "black" => "#000000",
            "white" => "#ffffff",
            "transparent" => "none",
            _ => value
        };
    }

    private static string Svg(string tag, string attributes) => $"<{tag} {attributes}/>";
    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Esc(string value) => SecurityElement.Escape(value) ?? string.Empty;
}

internal static class ImageDiff
{
    public static DiffResult Compare(string referencePath, string convertedPath, string diffPath)
    {
        using var reference = Image.Load<Rgba32>(referencePath);
        using var converted = Image.Load<Rgba32>(convertedPath);
        if (reference.Width != converted.Width || reference.Height != converted.Height)
        {
            throw new InvalidOperationException("Reference and converted screenshots must have identical dimensions.");
        }

        using var diff = new Image<Rgba32>(reference.Width, reference.Height);
        double totalDelta = 0;
        double maxDelta = 0;
        var changed = 0L;
        var total = (long)reference.Width * reference.Height;

        reference.ProcessPixelRows(converted, diff, (referenceAccessor, convertedAccessor, diffAccessor) =>
        {
            for (var y = 0; y < referenceAccessor.Height; y++)
            {
                var referenceRow = referenceAccessor.GetRowSpan(y);
                var convertedRow = convertedAccessor.GetRowSpan(y);
                var diffRow = diffAccessor.GetRowSpan(y);
                for (var x = 0; x < referenceAccessor.Width; x++)
                {
                    var delta = Delta(referenceRow[x], convertedRow[x]);
                    totalDelta += delta;
                    maxDelta = Math.Max(maxDelta, delta);
                    if (delta > 0.03)
                    {
                        changed++;
                        diffRow[x] = new Rgba32(255, 0, 80, 220);
                    }
                    else
                    {
                        var gray = (byte)((referenceRow[x].R + referenceRow[x].G + referenceRow[x].B) / 3);
                        diffRow[x] = new Rgba32(gray, gray, gray, 80);
                    }
                }
            }
        });

        diff.Save(diffPath);
        return new DiffResult(totalDelta / total, changed / (double)total, maxDelta);
    }

    private static double Delta(Rgba32 a, Rgba32 b)
    {
        var dr = Math.Abs(a.R - b.R) / 255d;
        var dg = Math.Abs(a.G - b.G) / 255d;
        var db = Math.Abs(a.B - b.B) / 255d;
        var da = Math.Abs(a.A - b.A) / 255d;
        return (dr + dg + db + da) / 4d;
    }
}

internal sealed record DiffResult(double MeanDelta, double ChangedPixelRatio, double MaxDelta);
