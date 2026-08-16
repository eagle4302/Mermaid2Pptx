namespace Mermaid2Pptx;

public sealed class MermaidPptxConverter
{
    public async Task<ConversionResult> ConvertHtmlFileAsync(
        string htmlPath,
        string outputPath,
        string slideSelector = ".slide",
        string svgSelector = "svg",
        double widthInches = 13.333,
        double heightInches = 7.5,
        CancellationToken cancellationToken = default)
    {
        var extractor = new MermaidSvgExtractor();
        var extracted = await extractor.ExtractAsync(htmlPath, slideSelector, svgSelector, cancellationToken);
        if (extracted.Count == 0)
        {
            throw new InvalidOperationException("No rendered SVG was found. Check selectors or Mermaid render timing.");
        }

        var parser = new SvgDocumentParser();
        var mapper = new SvgToPowerPointMapper();
        var deck = new PptxDeckModel
        {
            WidthInches = widthInches,
            HeightInches = heightInches
        };

        foreach (var item in extracted)
        {
            var scene = parser.Parse(item.SvgMarkup);
            var slide = mapper.MapSvgToSlide(scene, widthInches, heightInches, item);
            deck.Slides.Add(slide);
            deck.Warnings.AddRange(slide.Warnings.Select(warning => $"slide {item.SourceSlideIndex + 1}, svg {item.SvgIndex + 1}: {warning}"));
        }

        new DrawingMlWriter().Write(deck, outputPath);
        return new ConversionResult(Path.GetFullPath(outputPath), deck.Slides.Count, deck.Slides.Sum(slide => slide.Shapes.Count), deck.Warnings);
    }

    public async Task<ConversionResult> ConvertMermaidCodeAsync(
        string mermaidCode,
        string outputPath,
        double widthInches = 13.333,
        double heightInches = 7.5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mermaidCode))
        {
            throw new ArgumentException("Mermaid code is required.", nameof(mermaidCode));
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "mermaid2pptx", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var htmlPath = Path.Combine(tempDirectory, "input.html");
        await File.WriteAllTextAsync(htmlPath, BuildSingleSlideHtml(mermaidCode), cancellationToken);

        try
        {
            return await ConvertHtmlFileAsync(
                htmlPath,
                outputPath,
                ".slide",
                "svg",
                widthInches,
                heightInches,
                cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    public async Task<ConversionResult> ConvertDrawIoFileAsync(
        string drawIoPath,
        string outputPath,
        double widthInches = 13.333,
        double heightInches = 7.5,
        CancellationToken cancellationToken = default)
    {
        var xml = await File.ReadAllTextAsync(drawIoPath, cancellationToken);
        return ConvertDrawIoXml(xml, outputPath, widthInches, heightInches);
    }

    public ConversionResult ConvertDrawIoXml(
        string drawIoXml,
        string outputPath,
        double widthInches = 13.333,
        double heightInches = 7.5)
    {
        if (string.IsNullOrWhiteSpace(drawIoXml))
        {
            throw new ArgumentException("Draw.io markup is required.", nameof(drawIoXml));
        }

        var scenes = new DrawIoDocumentParser().Parse(drawIoXml);
        if (scenes.Count == 0)
        {
            throw new InvalidOperationException("No draw.io diagram pages were found.");
        }

        var mapper = new SvgToPowerPointMapper();
        var deck = new PptxDeckModel
        {
            WidthInches = widthInches,
            HeightInches = heightInches
        };

        for (var index = 0; index < scenes.Count; index++)
        {
            var scene = scenes[index];
            var slide = mapper.MapSvgToSlide(scene, widthInches, heightInches);
            deck.Slides.Add(slide);
            deck.Warnings.AddRange(slide.Warnings.Select(warning => $"slide {index + 1}: {warning}"));
        }

        new DrawingMlWriter().Write(deck, outputPath);
        return new ConversionResult(
            Path.GetFullPath(outputPath),
            deck.Slides.Count,
            deck.Slides.Sum(slide => slide.Shapes.Count),
            deck.Warnings);
    }

    private static string BuildSingleSlideHtml(string mermaidCode)
    {
        var escapedCode = System.Net.WebUtility.HtmlEncode(mermaidCode);
        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <style>
    html, body { margin: 0; width: 100%; min-height: 100%; background: #ffffff; }
    .slide {
      width: 1280px;
      min-height: 720px;
      display: grid;
      place-items: center;
      background: #ffffff;
      padding: 48px;
      box-sizing: border-box;
      font-family: Arial, sans-serif;
    }
    .mermaid { width: min(1120px, 100%); }
  </style>
</head>
<body>
  <section class="slide">
    <pre class="mermaid">{{escapedCode}}</pre>
  </section>
  <script type="module">
    import mermaid from "https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs";
    mermaid.initialize({ startOnLoad: false, securityLevel: "loose" });
    await mermaid.run({ querySelector: ".mermaid" });
    window.__MERMAID_DONE__ = true;
  </script>
</body>
</html>
""";
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Temp cleanup is best-effort; conversion output has already been written.
        }
    }
}

public sealed record ConversionResult(
    string OutputPath,
    int SlideCount,
    int NativeShapeCount,
    IReadOnlyList<string> Warnings);
