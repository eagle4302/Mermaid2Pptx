using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Mermaid2Pptx;

const double SlideWidthInches = 13.333;
const double SlideHeightInches = 7.5;
const int HtmlSlideWidth = 1600;
const int HtmlSlideHeight = 900;

var mdPath = Arg(args, "--md", @"E:\project\ViewPath7X\docs\3d-smart-factory-platform-zh.md");
var outputPath = Path.GetFullPath(Arg(args, "--out", @"out\smart-factory-platform-zh.pptx"));
var outputDirectory = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
Directory.CreateDirectory(outputDirectory);

var markdown = await File.ReadAllTextAsync(mdPath, Encoding.UTF8);
var mermaidBlocks = ExtractMermaidBlocks(markdown);
if (mermaidBlocks.Count < 4)
{
    throw new InvalidOperationException($"Expected at least 4 Mermaid blocks in {mdPath}, found {mermaidBlocks.Count}.");
}

var diagramHtmlPath = Path.Combine(outputDirectory, "smart-factory-mermaid-source.html");
var diagramSlides = await BuildDiagramSlidesAsync(mermaidBlocks, diagramHtmlPath);
var deck = BuildDeck(diagramSlides);

new DrawingMlWriter().Write(deck, outputPath);
SetPackageMetadata(outputPath);
var validation = ValidatePptx(outputPath);

Console.WriteLine($"Wrote {outputPath}");
Console.WriteLine($"Slides: {deck.Slides.Count}");
Console.WriteLine($"Native shapes: {deck.Slides.Sum(slide => slide.Shapes.Count)}");
Console.WriteLine($"Mermaid source HTML: {diagramHtmlPath}");
Console.WriteLine($"Open XML validation errors: {validation.ValidationErrorCount}");
Console.WriteLine($"Image parts: {validation.ImagePartCount}");
Console.WriteLine($"Media entries: {validation.MediaEntryCount}");
if (diagramSlides.Values.SelectMany(slide => slide.Warnings).Any())
{
    Console.WriteLine("Mermaid warnings:");
    foreach (var warning in diagramSlides.Values.SelectMany(slide => slide.Warnings))
    {
        Console.WriteLine($"- {warning}");
    }
}

static string Arg(string[] args, string name, string fallback)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return fallback;
}

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

static PptxDeckModel BuildDeck(IReadOnlyDictionary<string, PptxSlideModel> diagrams)
{
    var deck = new PptxDeckModel { WidthInches = SlideWidthInches, HeightInches = SlideHeightInches };
    deck.Slides.Add(BuildCover());
    deck.Slides.Add(BuildPositioning(2));
    deck.Slides.Add(BuildPain(3));
    deck.Slides.Add(BuildOperatingLayer(4));
    deck.Slides.Add(BuildCoreValues(5));
    deck.Slides.Add(BuildStepCapability(6));
    deck.Slides.Add(BuildMotorCapability(7));
    deck.Slides.Add(BuildProductLookup(8));
    deck.Slides.Add(BuildArchitecture(9, diagrams["architecture"]));
    deck.Slides.Add(BuildDataFlow(10, diagrams["data-flow"]));
    deck.Slides.Add(BuildUseCases(11));
    deck.Slides.Add(BuildTargets(12));
    deck.Slides.Add(BuildDifferentiation(13));
    deck.Slides.Add(BuildTechLayers(14));
    deck.Slides.Add(BuildImplementation(15, diagrams["implementation"]));
    deck.Slides.Add(BuildRoi(16));
    deck.Slides.Add(BuildRoadmap(17, diagrams["roadmap"]));
    deck.Slides.Add(BuildBusinessAndClose(18));
    return deck;
}

static PptxSlideModel BuildCover()
{
    var slide = NewSlide();
    AddRect(slide, "cover-bg", 0, 0, SlideWidthInches, SlideHeightInches, Palette.Deep, "none");
    AddRect(slide, "cover-teal-field", 8.6, 0, 4.75, SlideHeightInches, Palette.Teal, "none", fillOpacity: 0.18);
    AddFactoryGrid(slide);
    AddText(slide, "cover-kicker", "產品介紹簡報", 0.72, 0.62, 2.8, 0.32, 13, Palette.Mint, "600");
    AddText(slide, "cover-title", "3D 智慧工廠\n即時平台", 0.72, 1.42, 7.2, 1.7, 48, Palette.White, "700");
    AddText(slide, "cover-subtitle", "把自動化設備、即時控制資料、工單資訊與 3D 場景整合成一個可操作的數位孿生操作層。", 0.78, 3.5, 6.7, 0.78, 21, "#CFE6E1", "400");
    AddText(slide, "cover-thesis", "從「看數據」升級為「看現場、看產品、看流程」", 0.78, 5.12, 6.9, 0.46, 20, Palette.White, "600");
    AddLine(slide, "cover-rule", 0.78, 4.78, 5.2, 4.78, Palette.Mint, 3);
    AddText(slide, "cover-date", "依據 3d-smart-factory-platform-zh.md", 0.78, 6.82, 4.8, 0.24, 10, "#91BDB5", "400");
    return slide;
}

static PptxSlideModel BuildPositioning(int index)
{
    var slide = StandardSlide(index, "定位", "不是替代系統，而是現場即時操作層");
    AddText(slide, "position-claim", "平台把原本分散在 SCADA、MES、CAD 與控制器中的資訊，放回可理解的 3D 現場語境。", 0.9, 1.38, 11.5, 0.72, 26, Palette.Ink, "700");
    AddRect(slide, "position-layer-bg", 4.45, 2.72, 4.45, 1.28, Palette.Teal, Palette.Teal, radius: true);
    AddText(slide, "position-layer", "即時操作層", 4.9, 3.03, 3.55, 0.72, 31, Palette.White, "700", "ctr");

    var items = new[]
    {
        ("不是單純 3D Viewer", "3D 場景要能吃即時資料"),
        ("不是 SCADA 替代品", "警報與設備狀態仍由 SCADA 提供"),
        ("不是 MES 替代品", "工單、Routing 與紀錄仍由 MES 管理")
    };
    for (var i = 0; i < items.Length; i++)
    {
        var x = 0.82 + i * 4.18;
        AddRect(slide, $"position-note-{i}", x, 4.92, 3.55, 1.0, i == 0 ? Palette.SoftBlue : i == 1 ? Palette.SoftAmber : Palette.SoftGreen, "#CAD8D4", radius: true);
        AddText(slide, $"position-note-title-{i}", items[i].Item1, x + 0.18, 5.07, 3.18, 0.28, 15, Palette.Ink, "700", "ctr");
        AddText(slide, $"position-note-body-{i}", items[i].Item2, x + 0.22, 5.47, 3.1, 0.27, 11.2, Palette.Muted, "400", "ctr");
        AddLine(slide, $"position-line-{i}", x + 1.78, 4.76, 6.66, 4.0, Palette.Slate, 1.1, dashed: true);
    }

    AddFooter(slide, index);
    return slide;
}

static PptxSlideModel BuildPain(int index)
{
    var slide = StandardSlide(index, "現場痛點", "資料存在，但脈絡被拆散");
    AddText(slide, "pain-lead", "同一條產線通常同時存在四種真相：設備狀態、工單流程、機構模型、控制座標。問題不在資料不足，而在資料沒有一起指向現場位置。", 0.72, 1.25, 11.9, 0.72, 20, Palette.Ink, "600");

    var columns = new[]
    {
        ("SCADA", "設備監控\n資料擷取\n警報事件", "看到警報，未必知道實體位置"),
        ("MES", "工單\n製程\n生產紀錄", "看到 WIP，未必知道產品在哪一站"),
        ("CAD / STEP", "設備模型\n機構結構\n空間尺寸", "模型漂亮，但缺少即時狀態"),
        ("PLC / 通訊", "EtherCAT\nModbus\n馬達座標", "座標精準，但不容易被人理解")
    };
    for (var i = 0; i < columns.Length; i++)
    {
        var x = 0.72 + i * 3.15;
        AddText(slide, $"pain-system-{i}", columns[i].Item1, x, 2.5, 2.55, 0.35, 18, i % 2 == 0 ? Palette.Teal : Palette.Blue, "700", "ctr");
        AddRect(slide, $"pain-rail-{i}", x + 0.08, 3.03, 2.4, 1.45, i % 2 == 0 ? Palette.SoftMint : Palette.SoftBlue, "#C8DAD5", radius: true);
        AddText(slide, $"pain-data-{i}", columns[i].Item2, x + 0.25, 3.22, 2.05, 0.84, 14.5, Palette.Ink, "600", "ctr");
        AddText(slide, $"pain-gap-{i}", columns[i].Item3, x + 0.02, 5.02, 2.56, 0.55, 13, Palette.Muted, "400", "ctr");
        AddLine(slide, $"pain-rule-{i}", x + 0.54, 4.72, x + 2.0, 4.72, Palette.Amber, 2);
    }

    AddText(slide, "pain-bottom", "平台的切入點：把事件、位置、產品與流程綁在同一個操作畫面。", 1.15, 6.32, 11.0, 0.42, 21, Palette.Ink, "700", "ctr");
    AddFooter(slide, index);
    return slide;
}

static PptxSlideModel BuildOperatingLayer(int index)
{
    var slide = StandardSlide(index, "平台位置", "在 OT 與 IT 之間補上一個可視、可查、可操作的現場層");
    AddRect(slide, "layer-ot", 0.8, 1.45, 3.05, 4.55, Palette.SoftBlue, "#B8CADB", radius: true);
    AddRect(slide, "layer-core", 4.62, 1.25, 4.08, 4.95, Palette.SoftMint, "#A9C8C2", radius: true);
    AddRect(slide, "layer-users", 9.48, 1.45, 3.05, 4.55, Palette.SoftAmber, "#D8C49A", radius: true);
    AddText(slide, "layer-ot-title", "資料來源", 1.12, 1.78, 2.4, 0.35, 19, Palette.Blue, "700", "ctr");
    AddText(slide, "layer-core-title", "數位孿生核心", 5.0, 1.58, 3.35, 0.4, 21, Palette.Teal, "700", "ctr");
    AddText(slide, "layer-users-title", "操作對象", 9.82, 1.78, 2.4, 0.35, 19, Palette.Amber, "700", "ctr");
    AddText(slide, "layer-ot-body", "STEP / CAD\nPLC / Controller\nEtherCAT / Modbus\nMES / Routing\nSCADA / Alarm", 1.08, 2.48, 2.5, 2.05, 16, Palette.Ink, "600", "ctr");
    AddText(slide, "layer-core-body", "模型轉換\n座標映射\n狀態同步\n產品綁定\n事件定位", 5.08, 2.35, 3.0, 2.35, 19, Palette.Ink, "700", "ctr");
    AddText(slide, "layer-users-body", "工程師\n操作員\n生管 / 主管\n新人訓練\n第三方系統", 9.98, 2.48, 2.08, 2.05, 16, Palette.Ink, "600", "ctr");
    AddLine(slide, "layer-arrow-1", 3.9, 3.72, 4.55, 3.72, Palette.Slate, 2.5, arrowEnd: true);
    AddLine(slide, "layer-arrow-2", 8.76, 3.72, 9.42, 3.72, Palette.Slate, 2.5, arrowEnd: true);
    AddText(slide, "layer-message", "不是多一套看板，而是把「資料」翻成「現場語境」的操作系統。", 1.35, 6.48, 10.6, 0.42, 20, Palette.Ink, "700", "ctr");
    AddFooter(slide, index);
    return slide;
}

static PptxSlideModel BuildCoreValues(int index)
{
    var slide = StandardSlide(index, "核心價值", "五個轉換，讓工廠資料回到現場");
    var values = new[]
    {
        ("設備狀態", "空間語境", Palette.Teal),
        ("馬達座標", "3D 動作", Palette.Blue),
        ("工單資料", "產品位置", Palette.Green),
        ("異常事件", "現場物件", Palette.Red),
        ("資深經驗", "操作流程", Palette.Amber)
    };
    for (var i = 0; i < values.Length; i++)
    {
        var y = 1.35 + i * 0.98;
        AddText(slide, $"value-from-{i}", values[i].Item1, 1.12, y + 0.1, 2.1, 0.32, 18, Palette.Ink, "700", "r");
        AddLine(slide, $"value-arrow-{i}", 3.42, y + 0.28, 5.34, y + 0.28, values[i].Item3, 2.6, arrowEnd: true);
        AddText(slide, $"value-to-{i}", values[i].Item2, 5.62, y + 0.1, 2.35, 0.32, 18, values[i].Item3, "700");
        AddText(slide, $"value-desc-{i}", ValueDescription(i), 8.08, y - 0.02, 4.3, 0.55, 13.2, Palette.Muted, "400");
    }

    AddRect(slide, "value-side-field", 0.72, 6.35, 11.9, 0.54, Palette.Deep, "none", radius: true);
    AddText(slide, "value-side-text", "結果：同一個畫面同時回答「哪裡出事、哪個產品、哪道流程、下一步怎麼做」。", 1.02, 6.47, 11.3, 0.28, 16, Palette.White, "700", "ctr");
    AddFooter(slide, index);
    return slide;
}

static string ValueDescription(int index) => index switch
{
    0 => "把 alarm、running、idle、fault 等狀態落到設備位置。",
    1 => "把軸座標、速度與位置映射成可觀察的模型動作。",
    2 => "把工單、站點與 routing 綁定到產品或載具。",
    3 => "讓異常直接指向機構、站點或工件，而非只停在代碼。",
    _ => "把排查與訓練步驟沉澱成可複製的現場流程。"
};

static PptxSlideModel BuildStepCapability(int index)
{
    var slide = StandardSlide(index, "核心能力 1", "STEP / CAD 轉成可互動的 3D 場景");
    AddText(slide, "step-lead", "平台先把設備模型變成產線數位孿生的骨架，之後每一筆狀態、座標與工單資料才有空間落點。", 0.78, 1.2, 11.8, 0.55, 20, Palette.Ink, "600");
    var steps = new[] { "STEP / CAD 檔", "3D Mesh 轉換", "物件層級管理", "互動式場景" };
    for (var i = 0; i < steps.Length; i++)
    {
        var x = 0.85 + i * 3.08;
        AddRect(slide, $"step-box-{i}", x, 2.55, 2.42, 1.12, i == 0 ? Palette.SoftBlue : i == 3 ? Palette.SoftMint : Palette.White, "#BFD2CE", radius: true);
        AddText(slide, $"step-text-{i}", steps[i], x + 0.18, 2.88, 2.06, 0.34, 16, Palette.Ink, "700", "ctr");
        if (i < steps.Length - 1)
        {
            AddLine(slide, $"step-arrow-{i}", x + 2.48, 3.11, x + 3.02, 3.11, Palette.Slate, 2, arrowEnd: true);
        }
    }

    AddText(slide, "step-target-title", "適用設備", 0.9, 4.62, 2.0, 0.35, 18, Palette.Teal, "700");
    var targets = new[] { "滑台", "輸送線", "機械手臂", "檢測站", "組裝設備" };
    for (var i = 0; i < targets.Length; i++)
    {
        AddPill(slide, $"step-pill-{i}", targets[i], 0.9 + i * 2.28, 5.22, 1.75, Palette.SoftMint, Palette.Teal);
    }

    AddText(slide, "step-note", "關鍵不是「有 3D」，而是每個模型物件都能對應設備、產品、站點與事件。", 1.05, 6.42, 11.2, 0.42, 19, Palette.Ink, "700", "ctr");
    AddFooter(slide, index);
    return slide;
}

static PptxSlideModel BuildMotorCapability(int index)
{
    var slide = StandardSlide(index, "核心能力 2", "馬達座標同步 3D 動作");
    AddText(slide, "motor-lead", "控制器資料不只被記錄，而是被翻譯成模型上的位置、速度與狀態變化。", 0.75, 1.18, 11.9, 0.45, 20, Palette.Ink, "600");
    AddRect(slide, "motor-data", 0.9, 2.0, 4.0, 3.9, Palette.SoftBlue, "#C1D5E5", radius: true);
    AddText(slide, "motor-data-title", "即時控制資料", 1.22, 2.28, 3.36, 0.35, 19, Palette.Blue, "700", "ctr");
    AddText(slide, "motor-data-body", "軸座標 X / Y / Z\n馬達位置\n速度與加速度\nRunning / Idle / Fault\n通訊品質", 1.35, 3.0, 3.1, 1.85, 17, Palette.Ink, "600", "ctr");
    AddLine(slide, "motor-arrow", 5.1, 3.95, 7.65, 3.95, Palette.Teal, 3, arrowEnd: true);
    AddText(slide, "motor-map", "座標映射\n狀態同步", 5.35, 3.35, 2.0, 0.85, 18, Palette.Teal, "700", "ctr");
    AddRect(slide, "motor-scene", 8.0, 2.0, 4.35, 3.9, Palette.SoftMint, "#B6D4CF", radius: true);
    AddText(slide, "motor-scene-title", "3D 現場可見", 8.4, 2.28, 3.55, 0.35, 19, Palette.Teal, "700", "ctr");
    AddText(slide, "motor-scene-body", "哪個軸正在移動\n設備目前在哪裡\n動作是否符合預期\n異常是否卡在特定機構", 8.45, 3.08, 3.45, 1.45, 17, Palette.Ink, "600", "ctr");
    AddText(slide, "motor-bottom", "對工程師而言，這是從「看座標」變成「看機構正在怎麼動」。", 1.1, 6.45, 11.0, 0.4, 20, Palette.Ink, "700", "ctr");
    AddFooter(slide, index);
    return slide;
}

static PptxSlideModel BuildProductLookup(int index)
{
    var slide = StandardSlide(index, "核心能力 3", "點擊產品，直接看工單與 Routing");
    AddText(slide, "lookup-lead", "產品、載具或工件在 3D 場景中不再只是模型，而是能打開生產資訊的互動物件。", 0.75, 1.18, 11.8, 0.45, 20, Palette.Ink, "600");
    AddRect(slide, "lookup-scene", 0.88, 2.0, 6.1, 4.25, "#EEF6F3", "#BFD2CE", radius: true);
    AddText(slide, "lookup-scene-label", "3D 產線場景", 1.18, 2.28, 2.2, 0.3, 15, Palette.Teal, "700");
    for (var i = 0; i < 4; i++)
    {
        AddRect(slide, $"lookup-line-{i}", 1.28 + i * 1.18, 3.05 + (i % 2) * 0.62, 1.7, 0.22, i == 2 ? Palette.Amber : "#A9C8C2", "none", radius: true);
    }
    AddRect(slide, "lookup-product", 3.32, 4.35, 0.9, 0.48, Palette.Teal, Palette.Teal, radius: true);
    AddText(slide, "lookup-product-text", "Product", 3.42, 4.48, 0.7, 0.16, 8.5, Palette.White, "700", "ctr");
    AddLine(slide, "lookup-click-line", 4.25, 4.58, 7.22, 3.08, Palette.Amber, 2.5, arrowEnd: true);
    AddText(slide, "lookup-click", "Click", 5.18, 3.58, 0.88, 0.25, 12, Palette.Amber, "700", "ctr");
    AddRect(slide, "lookup-panel", 7.45, 2.0, 4.95, 4.25, Palette.White, "#BFD2CE", radius: true);
    AddText(slide, "lookup-panel-title", "產品資訊面板", 7.78, 2.35, 4.25, 0.34, 19, Palette.Ink, "700");
    var fields = new[] { "工單編號", "目前站點", "下一站 Routing", "WIP 狀態", "製程紀錄" };
    for (var i = 0; i < fields.Length; i++)
    {
        var y = 3.0 + i * 0.52;
        AddText(slide, $"lookup-field-{i}", fields[i], 7.82, y, 1.72, 0.24, 12.5, Palette.Muted, "600");
        AddLine(slide, $"lookup-field-line-{i}", 9.5, y + 0.14, 11.92, y + 0.14, i == 1 ? Palette.Teal : "#CBD8D5", 1.3);
    }
    AddText(slide, "lookup-bottom", "現場決策少一次跨系統查詢，就少一次資訊斷點。", 1.0, 6.62, 11.3, 0.32, 18, Palette.Ink, "700", "ctr");
    AddFooter(slide, index);
    return slide;
}

static PptxSlideModel BuildArchitecture(int index, PptxSlideModel diagram)
{
    var slide = StandardSlide(index, "系統架構", "STEP、OT、IT 資料匯入後，由數位孿生核心提供 3D 操作介面與整合 API");
    AddDiagram(slide, diagram);
    AddArchitectureConnectors(slide);
    AddText(slide, "arch-note", "Mermaid 原始架構圖已透過本專案 SVG DOM → DrawingML 流程轉成可編輯 PowerPoint 原生圖形。", 0.88, 6.86, 11.65, 0.24, 10.5, Palette.Muted, "400", "ctr");
    AddFooter(slide, index);
    return slide;
}

static PptxSlideModel BuildDataFlow(int index, PptxSlideModel diagram)
{
    var slide = StandardSlide(index, "即時資料流程", "設備狀態、控制座標與 MES 工單同步進入平台，再回應使用者點擊查詢");
    AddDiagram(slide, diagram, maxBottomInches: 6.33);
    AddRect(slide, "flow-bottom-mask", 0.55, 6.35, 12.25, 1.14, Palette.Background, "none");
    AddText(slide, "flow-note", "這張 sequence diagram 保留為原生文字、線條與箭頭，方便後續在 PPT 內直接改標籤。", 0.88, 6.86, 11.65, 0.24, 10.5, Palette.Muted, "400", "ctr");
    AddFooter(slide, index);
    return slide;
}

static void AddArchitectureConnectors(PptxSlideModel slide)
{
    var color = Palette.Slate;
    AddLine(slide, "arch-manual-step-mesh", 2.72, 1.78, 3.48, 1.78, color, 1.4, arrowEnd: true);
    AddLine(slide, "arch-manual-mesh-core", 5.2, 1.8, 5.72, 3.42, color, 1.2, arrowEnd: true);
    AddLine(slide, "arch-manual-plc-ot", 2.82, 2.9, 3.58, 3.42, color, 1.2, arrowEnd: true);
    AddLine(slide, "arch-manual-ether-ot", 2.95, 3.98, 3.58, 3.42, color, 1.2, arrowEnd: true);
    AddLine(slide, "arch-manual-mes-it", 3.0, 5.08, 3.62, 5.58, color, 1.2, arrowEnd: true);
    AddLine(slide, "arch-manual-scada-it", 2.96, 6.12, 3.62, 5.58, color, 1.2, arrowEnd: true);
    AddLine(slide, "arch-manual-ot-core", 5.15, 3.42, 5.72, 3.42, color, 1.4, arrowEnd: true);
    AddLine(slide, "arch-manual-it-core", 5.1, 5.58, 5.72, 3.42, color, 1.2, arrowEnd: true);
    AddLine(slide, "arch-manual-core-ui", 7.38, 3.3, 7.9, 2.9, color, 1.2, arrowEnd: true);
    AddLine(slide, "arch-manual-core-api", 7.38, 3.52, 8.2, 3.98, color, 1.2, arrowEnd: true);
    AddLine(slide, "arch-manual-ui-user", 9.78, 2.9, 10.28, 2.9, color, 1.4, arrowEnd: true);
}

static PptxSlideModel BuildUseCases(int index)
{
    var slide = StandardSlide(index, "典型使用場景", "同一個平台同時服務排查、透明化與訓練");
    var rows = new[]
    {
        ("異常排查", "警報直接定位到 3D 場景中的設備或機構", "減少人工查找、降低停線時間、降低對資深工程師依賴"),
        ("WIP 透明化", "在 3D 場景中查看產品位置與下一站 routing", "提升生產透明度、降低跨系統查詢成本、加快現場決策"),
        ("新人訓練", "透過 3D 場景理解產線結構、設備動作與製程路徑", "縮短上手時間、降低訓練成本、把經驗變成可視流程")
    };
    AddSimpleTable(slide, 0.72, 1.55, 11.9, new[] { 2.0, 4.55, 5.35 }, rows, new[] { "場景", "平台做什麼", "預期效益" });
    AddText(slide, "use-case-bottom", "平台價值不是多一個查詢入口，而是把問題定位到「人可以行動」的位置。", 1.0, 6.58, 11.3, 0.35, 18, Palette.Ink, "700", "ctr");
    AddFooter(slide, index);
    return slide;
}

static PptxSlideModel BuildTargets(int index)
{
    var slide = StandardSlide(index, "目標客戶", "適合需要即時監控、精準定位與多站流程可視化的產線");
    AddText(slide, "target-criteria", "導入判斷：現場設備動作複雜、工件流動路徑長、異常定位依賴經驗、且管理系統與控制資料分散。", 0.78, 1.16, 11.8, 0.5, 20, Palette.Ink, "600");
    var targets = new[]
    {
        ("滑台自動化產線", Palette.Teal),
        ("輸送與搬運系統", Palette.Blue),
        ("機械手臂工作站", Palette.Green),
        ("檢測與量測設備", Palette.Amber),
        ("多站式組裝線", Palette.Red),
        ("高混線 / 頻繁換線", Palette.Slate)
    };
    for (var i = 0; i < targets.Length; i++)
    {
        var col = i % 3;
        var row = i / 3;
        var x = 0.85 + col * 4.1;
        var y = 2.55 + row * 1.55;
        AddRect(slide, $"target-card-{i}", x, y, 3.35, 1.02, Palette.White, "#CAD8D4", radius: true);
        AddRect(slide, $"target-accent-{i}", x, y, 0.13, 1.02, targets[i].Item2, "none");
        AddText(slide, $"target-text-{i}", targets[i].Item1, x + 0.32, y + 0.34, 2.75, 0.28, 16, Palette.Ink, "700", "ctr");
    }
    AddText(slide, "target-foot", "優先落地：高停線成本、Debug 時間長、跨部門協作頻繁的自動化設備與產線。", 1.0, 6.42, 11.2, 0.4, 19, Palette.Ink, "700", "ctr");
    AddFooter(slide, index);
    return slide;
}

static PptxSlideModel BuildDifferentiation(int index)
{
    var slide = StandardSlide(index, "與既有系統的差異", "平台補足的是「空間語境」與「整線操作層」");
    var rows = new[]
    {
        ("SCADA", "即時設備監控", "缺少 3D 空間語境", "把數據映射到設備位置"),
        ("MES", "工單與流程管理", "介面多為表格", "把工單連到產品位置"),
        ("3D Viewer", "可視化模型", "缺少即時生產資料", "加入 OT 與 MES 資料"),
        ("傳統 HMI", "操作單機設備", "難掌握整線狀態", "建立整線 3D 操作層")
    };
    AddComparisonTable(slide, 0.62, 1.35, 12.1, rows);
    AddText(slide, "diff-bottom", "定位重點：不取代既有系統，而是讓它們的資訊在現場同一張圖上可以被操作。", 0.95, 6.65, 11.45, 0.34, 17.5, Palette.Ink, "700", "ctr");
    AddFooter(slide, index);
    return slide;
}

static PptxSlideModel BuildTechLayers(int index)
{
    var slide = StandardSlide(index, "技術組成", "四層能力，讓資料從模型、控制、生產一路抵達應用");
    var layers = new[]
    {
        ("模型層", "STEP 檔解析\nMesh 轉換\n3D 場景建構\n物件管理", Palette.Teal),
        ("控制資料層", "EtherCAT 接入\nModbus 接入\n馬達座標讀取\n設備狀態同步", Palette.Blue),
        ("生產資料層", "MES 工單整合\nRouting 整合\nWIP 追蹤\n事件紀錄", Palette.Green),
        ("應用層", "3D 即時監控\n產品查詢\n異常定位\n角色介面", Palette.Amber)
    };
    for (var i = 0; i < layers.Length; i++)
    {
        var x = 0.72 + i * 3.12;
        AddRect(slide, $"tech-layer-{i}", x, 1.68, 2.62, 4.35, i % 2 == 0 ? Palette.SoftMint : Palette.SoftBlue, "#C5D8D4", radius: true);
        AddText(slide, $"tech-title-{i}", layers[i].Item1, x + 0.18, 2.02, 2.26, 0.35, 18, layers[i].Item3, "700", "ctr");
        AddLine(slide, $"tech-rule-{i}", x + 0.55, 2.55, x + 2.05, 2.55, layers[i].Item3, 2.2);
        AddText(slide, $"tech-body-{i}", layers[i].Item2, x + 0.28, 3.05, 2.06, 1.55, 15.2, Palette.Ink, "600", "ctr");
    }
    AddText(slide, "tech-bottom", "架構設計目標：任何資料進來，都要能定位到模型物件、產品位置、流程狀態或操作事件。", 0.95, 6.55, 11.45, 0.34, 17.5, Palette.Ink, "700", "ctr");
    AddFooter(slide, index);
    return slide;
}

static PptxSlideModel BuildImplementation(int index, PptxSlideModel diagram)
{
    var slide = StandardSlide(index, "導入方式", "從盤點資料源開始，最後模板化複製到其他產線");
    AddDiagram(slide, diagram);
    AddText(slide, "implementation-note", "導入節奏先求一條線跑通，再把設備模板、資料映射與操作流程複製到更多站點。", 0.88, 6.83, 11.65, 0.26, 11, Palette.Muted, "400", "ctr");
    AddFooter(slide, index);
    return slide;
}

static PptxSlideModel BuildRoi(int index)
{
    var slide = StandardSlide(index, "投資回報", "可衡量價值來自停線、排查、訓練與查詢成本下降");
    AddText(slide, "roi-lead", "ROI 不需要先假設神奇的 AI 效益；只要把現場找問題、找產品、找流程的時間縮短，就能形成直接回報。", 0.8, 1.22, 11.7, 0.52, 20, Palette.Ink, "600");
    var metrics = new[]
    {
        ("Debug 時間下降", Palette.Teal),
        ("停線回應時間縮短", Palette.Red),
        ("新人訓練週期縮短", Palette.Blue),
        ("WIP 查詢時間下降", Palette.Green),
        ("跨部門溝通成本降低", Palette.Amber),
        ("產線透明度提升", Palette.Slate)
    };
    for (var i = 0; i < metrics.Length; i++)
    {
        var col = i % 3;
        var row = i / 3;
        var x = 0.9 + col * 4.08;
        var y = 2.5 + row * 1.36;
        AddText(slide, $"roi-num-{i}", $"{i + 1:00}", x, y, 0.75, 0.48, 24, metrics[i].Item2, "700");
        AddText(slide, $"roi-metric-{i}", metrics[i].Item1, x + 0.78, y + 0.12, 2.88, 0.28, 15.5, Palette.Ink, "700");
        AddLine(slide, $"roi-rule-{i}", x + 0.82, y + 0.58, x + 3.5, y + 0.58, "#D5E0DC", 1.1);
    }
    AddRect(slide, "roi-bottom-field", 0.82, 6.18, 11.7, 0.74, Palette.Deep, "none", radius: true);
    AddText(slide, "roi-bottom", "衡量方式：以導入前後的排查時間、停線回應、查詢步驟與訓練週期做 baseline comparison。", 1.08, 6.39, 11.15, 0.26, 14.5, Palette.White, "700", "ctr");
    AddFooter(slide, index);
    return slide;
}

static PptxSlideModel BuildRoadmap(int index, PptxSlideModel diagram)
{
    var slide = StandardSlide(index, "發展路線", "從設備可視化走向平台化、多線模板與夥伴生態");
    AddDiagram(slide, diagram);
    var phases = new[]
    {
        ("Phase 1", "設備可視化", "STEP 轉場景\n設備狀態同步\n馬達座標映射"),
        ("Phase 2", "工單整合", "MES 工單串接\n產品與工單綁定\nRouting / WIP 顯示"),
        ("Phase 3", "平台化", "多設備模板\n多產線導入\nAPI 與第三方整合")
    };
    for (var i = 0; i < phases.Length; i++)
    {
        var y = 1.75 + i * 1.55;
        AddText(slide, $"road-phase-{i}", phases[i].Item1, 8.1, y, 1.4, 0.22, 11, Palette.Teal, "700");
        AddText(slide, $"road-title-{i}", phases[i].Item2, 8.1, y + 0.28, 3.7, 0.3, 17, Palette.Ink, "700");
        AddText(slide, $"road-body-{i}", phases[i].Item3, 8.1, y + 0.7, 3.7, 0.6, 11.5, Palette.Muted, "400");
        AddLine(slide, $"road-rule-{i}", 8.1, y + 1.34, 11.85, y + 1.34, "#D5E0DC", 1);
    }
    AddFooter(slide, index);
    return slide;
}

static PptxSlideModel BuildBusinessAndClose(int index)
{
    var slide = StandardSlide(index, "商業模式與結論", "下一代智慧工廠需要能理解現場的操作系統");
    AddText(slide, "biz-lead", "可行商模可從設備加值與導入服務切入，逐步走向模組授權與平台訂閱。", 0.8, 1.22, 11.7, 0.45, 20, Palette.Ink, "600");
    var models = new[]
    {
        "設備加值銷售",
        "站點式軟體授權",
        "模組化功能升級",
        "維護與導入服務",
        "未來 SaaS 平台訂閱"
    };
    for (var i = 0; i < models.Length; i++)
    {
        AddText(slide, $"biz-index-{i}", $"{i + 1}", 1.08, 2.1 + i * 0.58, 0.32, 0.25, 13, Palette.Teal, "700", "ctr");
        AddText(slide, $"biz-model-{i}", models[i], 1.58, 2.08 + i * 0.58, 4.1, 0.3, 16, Palette.Ink, "700");
        AddLine(slide, $"biz-line-{i}", 1.58, 2.48 + i * 0.58, 5.62, 2.48 + i * 0.58, "#D5E0DC", 1);
    }
    AddRect(slide, "close-field", 6.35, 2.02, 5.85, 3.72, Palette.Deep, "none", radius: true);
    AddText(slide, "close-title", "核心價值", 6.78, 2.45, 2.0, 0.28, 14, Palette.Mint, "700");
    AddText(slide, "close-copy", "讓工廠資料回到現場語境。\n\n把分散在 SCADA、MES、CAD 與控制器中的資訊，整合成一個可視、可查、可操作的 3D 工廠介面。", 6.78, 3.0, 4.85, 1.62, 19, Palette.White, "700");
    AddText(slide, "close-final", "不只是更多資料，而是更接近現場的操作系統。", 6.78, 5.02, 4.85, 0.32, 14.5, "#CFE6E1", "600");
    AddFooter(slide, index);
    return slide;
}

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

static void SetPackageMetadata(string pptxPath)
{
    using var document = PresentationDocument.Open(pptxPath, true);
    document.PackageProperties.Creator = "Mermaid2PPTX SmartFactoryDeck";
    document.PackageProperties.Title = "3D 智慧工廠即時平台";
    document.PackageProperties.Subject = "Detailed product introduction with native Mermaid DrawingML diagrams";
}

static PptxValidation ValidatePptx(string pptxPath)
{
    using var document = PresentationDocument.Open(pptxPath, false);
    var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(document).ToArray();
    var imagePartCount = document.PresentationPart?.GetPartsOfType<ImagePart>().Count() ?? 0;
    using var zip = ZipFile.OpenRead(pptxPath);
    var mediaEntryCount = zip.Entries.Count(entry => entry.FullName.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase));
    return new PptxValidation(errors.Length, imagePartCount, mediaEntryCount);
}

internal sealed record DiagramSpec(string Key, string Code, int LeftPx, int TopPx, int WidthPx, int HeightPx);

internal sealed record PptxValidation(int ValidationErrorCount, int ImagePartCount, int MediaEntryCount);

internal static class Palette
{
    public const string Background = "#F6FAF8";
    public const string White = "#FFFFFF";
    public const string Ink = "#17302D";
    public const string Muted = "#5F726C";
    public const string Deep = "#102825";
    public const string Teal = "#17877E";
    public const string Mint = "#95DED4";
    public const string Blue = "#2F73B7";
    public const string Green = "#4D8B4A";
    public const string Amber = "#D99025";
    public const string Red = "#C05243";
    public const string Slate = "#315B69";
    public const string SoftMint = "#DDF2EF";
    public const string SoftBlue = "#E6F0FA";
    public const string SoftAmber = "#FAF1DD";
    public const string SoftGreen = "#E8F3E4";
}
