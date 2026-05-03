using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Mermaid2Pptx;

namespace SmartFactoryDeck;

internal static partial class SmartFactoryDeckTool
{
    static List<string> ExtractMermaidBlocks(string markdown)
    {
        var matches = Regex.Matches(markdown, @"```mermaid\s*(?<code>[\s\S]*?)```", RegexOptions.IgnoreCase);
        return matches
            .Select(match => match.Groups["code"].Value.Trim())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .ToList();
    }

    static async Task<Dictionary<string, PptxSlideModel>> BuildDiagramSlidesAsync(IReadOnlyList<string> mermaidBlocks, string htmlPath)
    {
        var specs = new[]
        {
            new DiagramSpec("architecture", mermaidBlocks[0], 80, 150, 1440, 650),
            new DiagramSpec("data-flow", mermaidBlocks[1], 100, 145, 1400, 550),
            new DiagramSpec("implementation", mermaidBlocks[2], 80, 165, 1440, 610),
            new DiagramSpec("roadmap", mermaidBlocks[3], 70, 165, 860, 580)
        };

        await File.WriteAllTextAsync(htmlPath, BuildMermaidHtml(specs), Encoding.UTF8);

        var extractor = new MermaidSvgExtractor();
        var extracted = await extractor.ExtractAsync(htmlPath, ".slide", "svg");
        if (extracted.Count != specs.Length)
        {
            throw new InvalidOperationException($"Expected {specs.Length} rendered Mermaid SVGs, found {extracted.Count}.");
        }

        var parser = new SvgDocumentParser();
        var mapper = new SvgToPowerPointMapper();
        var result = new Dictionary<string, PptxSlideModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in extracted.OrderBy(item => item.SourceSlideIndex).ThenBy(item => item.SvgIndex))
        {
            var spec = specs[item.SourceSlideIndex];
            var scene = parser.Parse(item.SvgMarkup);
            var slide = mapper.MapSvgToSlide(scene, SlideWidthInches, SlideHeightInches, item);
            result[spec.Key] = slide;
        }

        return result;
    }

    static string BuildMermaidHtml(IReadOnlyList<DiagramSpec> specs)
    {
        var sections = new StringBuilder();
        foreach (var spec in specs)
        {
            sections.AppendLine($$"""
<section class="slide">
  <pre class="mermaid" style="left:{{spec.LeftPx}}px; top:{{spec.TopPx}}px; width:{{spec.WidthPx}}px; height:{{spec.HeightPx}}px;">{{WebUtility.HtmlEncode(spec.Code)}}</pre>
</section>
""");
        }

        return $$"""
<!doctype html>
<html lang="zh-Hant">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <style>
    html, body {
      margin: 0;
      background: #f6faf8;
      font-family: "Microsoft JhengHei", "Noto Sans TC", Arial, sans-serif;
    }
    .slide {
      position: relative;
      width: {{HtmlSlideWidth}}px;
      height: {{HtmlSlideHeight}}px;
      overflow: hidden;
      background: #f6faf8;
    }
    .mermaid {
      position: absolute;
      margin: 0;
      display: flex;
      align-items: center;
      justify-content: center;
      box-sizing: border-box;
      padding: 0;
      color: #17302d;
    }
    .mermaid svg {
      width: 100% !important;
      height: 100% !important;
      max-width: none !important;
      font-family: "Microsoft JhengHei", "Noto Sans TC", Arial, sans-serif !important;
    }
  </style>
</head>
<body>
{{sections}}
  <script type="module">
    import mermaid from "https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs";
    mermaid.initialize({
      startOnLoad: false,
      securityLevel: "loose",
      theme: "base",
      themeVariables: {
        fontFamily: "Microsoft JhengHei, Noto Sans TC, Arial, sans-serif",
        primaryColor: "#DDF2EF",
        primaryTextColor: "#17302D",
        primaryBorderColor: "#17877E",
        lineColor: "#315B69",
        secondaryColor: "#E6F0FA",
        secondaryTextColor: "#17302D",
        secondaryBorderColor: "#2F73B7",
        tertiaryColor: "#FAF1DD",
        tertiaryTextColor: "#17302D",
        tertiaryBorderColor: "#D99025",
        noteBkgColor: "#FAF1DD",
        noteTextColor: "#17302D",
        actorBkg: "#DDF2EF",
        actorBorder: "#17877E",
        actorTextColor: "#17302D",
        activationBkgColor: "#E6F0FA",
        activationBorderColor: "#2F73B7",
        signalColor: "#315B69",
        signalTextColor: "#17302D",
        labelBoxBkgColor: "#FFFFFF",
        labelBoxBorderColor: "#A9C8C2",
        labelTextColor: "#17302D"
      },
      flowchart: { curve: "basis", htmlLabels: true },
      sequence: { mirrorActors: false, useMaxWidth: false },
      state: { useMaxWidth: false }
    });
    await mermaid.run({ querySelector: ".mermaid" });
    window.__MERMAID_DONE__ = true;
  </script>
</body>
</html>
""";
    }

    internal sealed record DiagramSpec(string Key, string Code, int LeftPx, int TopPx, int WidthPx, int HeightPx);
}
