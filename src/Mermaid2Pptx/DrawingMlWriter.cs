using System.Globalization;
using System.IO.Compression;
using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace Mermaid2Pptx;

public sealed partial class DrawingMlWriter
{
    public void Write(PptxDeckModel deck, string outputPath)
    {
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");

        using (var document = PresentationDocument.Create(outputPath, PresentationDocumentType.Presentation))
        {
            document.PackageProperties.Creator = "Mermaid2Pptx";
            document.PackageProperties.Title = "Mermaid SVG to native DrawingML";

            var presentationPart = document.AddPresentationPart();

            var masterPart = presentationPart.AddNewPart<SlideMasterPart>("rIdMaster1");
            var layoutPart = masterPart.AddNewPart<SlideLayoutPart>("rIdLayout1");
            layoutPart.AddPart(masterPart, "rIdMaster1");
            var themePart = masterPart.AddNewPart<ThemePart>("rIdTheme1");
            Feed(layoutPart, SlideLayoutXml());
            Feed(themePart, ThemeXml());
            Feed(masterPart, SlideMasterXml());

            for (var i = 0; i < deck.Slides.Count; i++)
            {
                var slide = deck.Slides[i];
                for (var shapeIndex = 0; shapeIndex < slide.Shapes.Count; shapeIndex++)
                {
                    slide.Shapes[shapeIndex].Id = shapeIndex + 2;
                }

                var slidePart = presentationPart.AddNewPart<SlidePart>($"rIdSlide{i + 1}");
                slidePart.AddPart(layoutPart, "rIdLayout1");
                Feed(slidePart, SlideXml(slide));
            }

            Feed(presentationPart, PresentationXml(deck));
        }

        NormalizeContentTypes(outputPath, deck.Slides.Count);
    }

    private static void Feed(OpenXmlPart part, string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        part.FeedData(stream);
    }

    private static void NormalizeContentTypes(string outputPath, int slideCount)
    {
        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Update);
        archive.GetEntry("[Content_Types].xml")?.Delete();
        var entry = archive.CreateEntry("[Content_Types].xml", CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(ContentTypesXml(slideCount));
    }

    private static string ContentTypesXml(int slideCount)
    {
        var slideOverrides = string.Concat(Enumerable.Range(1, slideCount)
            .Select(index => $$"""<Override PartName="/ppt/slides/slide{{index}}.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>"""));

        return $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Default Extension="psmdcp" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
  <Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>
  <Override PartName="/ppt/slideMasters/slideMaster1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml"/>
  <Override PartName="/ppt/slideLayouts/slideLayout1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml"/>
  <Override PartName="/ppt/slideMasters/theme/theme1.xml" ContentType="application/vnd.openxmlformats-officedocument.theme+xml"/>
  {{slideOverrides}}
</Types>
""";
    }

    private static string PresentationXml(PptxDeckModel deck)
    {
        var width = UnitConversion.InchesToEmu(deck.WidthInches);
        var height = UnitConversion.InchesToEmu(deck.HeightInches);
        var slideIds = new StringBuilder();
        for (var i = 0; i < deck.Slides.Count; i++)
        {
            slideIds.Append(CultureInfo.InvariantCulture, $"""<p:sldId id="{256 + i}" r:id="rIdSlide{i + 1}"/>""");
        }

        return $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:presentation xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
  <p:sldMasterIdLst>
    <p:sldMasterId id="2147483648" r:id="rIdMaster1"/>
  </p:sldMasterIdLst>
  <p:sldIdLst>{{slideIds}}</p:sldIdLst>
  <p:sldSz cx="{{width}}" cy="{{height}}" type="screen16x9"/>
  <p:notesSz cx="6858000" cy="9144000"/>
  <p:defaultTextStyle>
    <a:defPPr>
      <a:defRPr lang="en-US"/>
    </a:defPPr>
  </p:defaultTextStyle>
</p:presentation>
""";
    }

    private static string SlideXml(PptxSlideModel slide)
    {
        var shapes = string.Concat(slide.Shapes.Select(ShapeXml));
        return $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
  <p:cSld>
    <p:spTree>
      <p:nvGrpSpPr>
        <p:cNvPr id="1" name=""/>
        <p:cNvGrpSpPr/>
        <p:nvPr/>
      </p:nvGrpSpPr>
      <p:grpSpPr>
        <a:xfrm>
          <a:off x="0" y="0"/>
          <a:ext cx="{{slide.WidthEmu}}" cy="{{slide.HeightEmu}}"/>
          <a:chOff x="0" y="0"/>
          <a:chExt cx="{{slide.WidthEmu}}" cy="{{slide.HeightEmu}}"/>
        </a:xfrm>
      </p:grpSpPr>
      {{shapes}}
    </p:spTree>
  </p:cSld>
  <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
</p:sld>
""";
    }

    private static string ShapeXml(PptxShape shape)
    {
        if (shape.Kind == PptxShapeKind.Line)
        {
            return ConnectorXml(shape);
        }

        var txBox = shape.Kind == PptxShapeKind.Text ? " txBox=\"1\"" : string.Empty;
        var textBody = shape.Kind == PptxShapeKind.Text ? TextBodyXml(shape) : string.Empty;
        return $$"""
      <p:sp>
        <p:nvSpPr>
          <p:cNvPr id="{{shape.Id}}" name="{{Esc(shape.Name)}}"/>
          <p:cNvSpPr{{txBox}}/>
          <p:nvPr/>
        </p:nvSpPr>
        <p:spPr>
          <a:xfrm>
            <a:off x="{{shape.X}}" y="{{shape.Y}}"/>
            <a:ext cx="{{Math.Max(1, shape.Cx)}}" cy="{{Math.Max(1, shape.Cy)}}"/>
          </a:xfrm>
          {{GeometryXml(shape)}}
          {{FillXml(shape)}}
          {{LineXml(shape)}}
        </p:spPr>
        {{textBody}}
      </p:sp>
""";
    }

    private static string ConnectorXml(PptxShape shape)
    {
        var flipH = shape.FlipH ? " flipH=\"1\"" : string.Empty;
        var flipV = shape.FlipV ? " flipV=\"1\"" : string.Empty;
        return $$"""
      <p:cxnSp>
        <p:nvCxnSpPr>
          <p:cNvPr id="{{shape.Id}}" name="{{Esc(shape.Name)}}"/>
          <p:cNvCxnSpPr/>
          <p:nvPr/>
        </p:nvCxnSpPr>
        <p:spPr>
          <a:xfrm{{flipH}}{{flipV}}>
            <a:off x="{{shape.X}}" y="{{shape.Y}}"/>
            <a:ext cx="{{Math.Max(1, shape.Cx)}}" cy="{{Math.Max(1, shape.Cy)}}"/>
          </a:xfrm>
          <a:prstGeom prst="line"><a:avLst/></a:prstGeom>
          {{LineXml(shape)}}
        </p:spPr>
      </p:cxnSp>
""";
    }

    private static string GeometryXml(PptxShape shape)
    {
        if (shape.Kind is PptxShapeKind.Preset or PptxShapeKind.Text)
        {
            var adjustment = shape.PresetAdjustValue.HasValue
                ? $$"""<a:gd name="adj" fmla="val {{shape.PresetAdjustValue.Value}}"/>"""
                : string.Empty;
            return $$"""<a:prstGeom prst="{{Esc(shape.PresetGeometry ?? "rect")}}"><a:avLst>{{adjustment}}</a:avLst></a:prstGeom>""";
        }

        var commands = string.Concat(shape.PathCommands.Select(PathCommandXml));
        return $$"""
<a:custGeom>
  <a:avLst/>
  <a:gdLst/>
  <a:ahLst/>
  <a:cxnLst/>
  <a:rect l="0" t="0" r="{{Math.Max(1, shape.PathWidth)}}" b="{{Math.Max(1, shape.PathHeight)}}"/>
  <a:pathLst>
    <a:path w="{{Math.Max(1, shape.PathWidth)}}" h="{{Math.Max(1, shape.PathHeight)}}">
      {{commands}}
    </a:path>
  </a:pathLst>
</a:custGeom>
""";
    }

    private static string PathCommandXml(PptxPathCommand command) =>
        command switch
        {
            PptxMoveTo move => PointCommandXml("moveTo", [move.Point]),
            PptxLineTo line => PointCommandXml("lnTo", [line.Point]),
            PptxCubicBezierTo cubic => PointCommandXml("cubicBezTo", [cubic.Control1, cubic.Control2, cubic.Point]),
            PptxQuadraticBezierTo quadratic => PointCommandXml("quadBezTo", [quadratic.Control, quadratic.Point]),
            PptxClosePath => "<a:close/>",
            _ => string.Empty
        };

    private static string PointCommandXml(string elementName, IReadOnlyList<PptPoint> points)
    {
        var pointXml = string.Concat(points.Select(point => $"""<a:pt x="{point.X}" y="{point.Y}"/>"""));
        return $"<a:{elementName}>{pointXml}</a:{elementName}>";
    }

    private static string FillXml(PptxShape shape)
    {
        if (shape.PreferNoFill || IsNone(shape.Style.Fill) || IsUrl(shape.Style.Fill))
        {
            return "<a:noFill/>";
        }

        var color = ResolvePaint(shape.Style.Fill, shape.Style);
        if (color is null)
        {
            return "<a:noFill/>";
        }

        return SolidFillXml(color.Value, shape.Style.Opacity * shape.Style.FillOpacity);
    }

    private static string LineXml(PptxShape shape)
    {
        var width = shape.LineWidthEmu > 0
            ? shape.LineWidthEmu
            : Math.Max(1, UnitConversion.PixelsToEmu(Math.Max(0.25, shape.Style.StrokeWidth)));
        var arrowXml = ArrowXml(shape);
        var dashXml = DashXml(shape.Style.StrokeDashArray);

        if (shape.PreferNoLine || IsNone(shape.Style.Stroke))
        {
            return $$"""<a:ln w="{{width}}"><a:noFill/>{{dashXml}}{{arrowXml}}</a:ln>""";
        }

        var strokePaint = IsUrl(shape.Style.Stroke) ? shape.Style.Color ?? "#315B69" : shape.Style.Stroke;
        var color = ResolvePaint(strokePaint, shape.Style);
        if (color is null)
        {
            return $$"""<a:ln w="{{width}}"><a:noFill/>{{dashXml}}{{arrowXml}}</a:ln>""";
        }

        return $$"""<a:ln w="{{width}}">{{SolidFillXml(color.Value, shape.Style.Opacity * shape.Style.StrokeOpacity)}}{{dashXml}}{{arrowXml}}</a:ln>""";
    }

    private static string DashXml(string? dashArray)
    {
        if (string.IsNullOrWhiteSpace(dashArray) || dashArray.Trim() == "0" || dashArray.Trim() == "0 0")
        {
            return string.Empty;
        }

        var normalized = dashArray.Replace(',', ' ');
        var values = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => SvgNumber.Parse(value, double.NaN))
            .Where(value => !double.IsNaN(value))
            .ToArray();
        if (values.Length == 0 || values.All(value => value <= 0))
        {
            return string.Empty;
        }

        var preset = values.Length >= 2 && values[0] <= 1.1 && values[1] <= 2.1 ? "dot" : "dash";
        return $"""<a:prstDash val="{preset}"/>""";
    }

    private static string ArrowXml(PptxShape shape)
    {
        var head = shape.ArrowStart
            ? """<a:headEnd type="triangle" w="med" len="med"/>"""
            : string.Empty;
        var tail = shape.ArrowEnd
            ? """<a:tailEnd type="triangle" w="med" len="med"/>"""
            : string.Empty;
        return head + tail;
    }

    private static string TextBodyXml(PptxShape shape)
    {
        var fontSizePt = Math.Max(1, shape.Style.FontSize * 72d / 96d);
        var fontSize = (int)Math.Round(fontSizePt * 100);
        var fontFamily = Esc((shape.Style.FontFamily ?? "Arial").Split(',')[0].Trim(' ', '\'', '"'));
        var bold = IsBold(shape.Style.FontWeight) ? " b=\"1\"" : string.Empty;
        var italic = shape.Style.FontStyle?.Equals("italic", StringComparison.OrdinalIgnoreCase) == true ? " i=\"1\"" : string.Empty;
        var fontPaint = !IsNone(shape.Style.Color)
            ? shape.Style.Color
            : !IsNone(shape.Style.Fill)
                ? shape.Style.Fill
                : "000000";
        var fontColor = ResolvePaint(fontPaint, shape.Style) ?? new SvgColor(0, 0, 0, 1);
        var fill = SolidFillXml(fontColor, shape.Style.Opacity * shape.Style.FillOpacity);

        var paragraphs = string.Concat((shape.Text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => $$"""
          <a:p>
            <a:pPr algn="{{shape.TextAlignment}}"/>
            <a:r>
              <a:rPr lang="en-US" sz="{{fontSize}}"{{bold}}{{italic}}>
                {{fill}}
                <a:latin typeface="{{fontFamily}}"/>
                <a:ea typeface="{{fontFamily}}"/>
                <a:cs typeface="{{fontFamily}}"/>
              </a:rPr>
              <a:t>{{Esc(line)}}</a:t>
            </a:r>
            <a:endParaRPr lang="en-US" sz="{{fontSize}}"/>
          </a:p>
"""));

        return $$"""
        <p:txBody>
          <a:bodyPr wrap="{{(shape.NoWrapText ? "none" : "square")}}" rtlCol="0" lIns="0" tIns="0" rIns="0" bIns="0" anchor="ctr">
            <a:noAutofit/>
          </a:bodyPr>
          <a:lstStyle/>
          {{paragraphs}}
        </p:txBody>
""";
    }

    private static string SolidFillXml(SvgColor color, double opacity)
    {
        var alpha = (int)Math.Round(Math.Max(0, Math.Min(1, opacity * color.Alpha)) * 100000);
        var alphaXml = alpha < 100000 ? $"""<a:alpha val="{alpha}"/>""" : string.Empty;
        return $"""<a:solidFill><a:srgbClr val="{color.R:X2}{color.G:X2}{color.B:X2}">{alphaXml}</a:srgbClr></a:solidFill>""";
    }

    private static bool IsBold(string? fontWeight)
    {
        if (string.IsNullOrWhiteSpace(fontWeight))
        {
            return false;
        }

        if (fontWeight.Equals("bold", StringComparison.OrdinalIgnoreCase) ||
            fontWeight.Equals("bolder", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return int.TryParse(fontWeight, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight) && weight >= 600;
    }

    private static bool IsNone(string? color) =>
        string.IsNullOrWhiteSpace(color) ||
        color.Equals("none", StringComparison.OrdinalIgnoreCase) ||
        color.Equals("transparent", StringComparison.OrdinalIgnoreCase);

    private static bool IsUrl(string? color) => color?.TrimStart().StartsWith("url(", StringComparison.OrdinalIgnoreCase) == true;

    private static SvgColor? ResolvePaint(string? paint, SvgStyle style)
    {
        if (string.IsNullOrWhiteSpace(paint))
        {
            return null;
        }

        var value = paint.Trim();
        if (value.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
        {
            return SvgColor.Parse(style.Color) ?? new SvgColor(0, 0, 0, 1);
        }

        if (value.Equals("inherit", StringComparison.OrdinalIgnoreCase))
        {
            return SvgColor.Parse(style.Color) ?? null;
        }

        return SvgColor.Parse(value);
    }

    private static string Esc(string? value) => SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;

    private static string SlideLayoutXml() =>
        """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sldLayout xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" type="blank" preserve="1">
  <p:cSld name="Blank">
    <p:spTree>
      <p:nvGrpSpPr>
        <p:cNvPr id="1" name=""/>
        <p:cNvGrpSpPr/>
        <p:nvPr/>
      </p:nvGrpSpPr>
      <p:grpSpPr/>
    </p:spTree>
  </p:cSld>
  <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
</p:sldLayout>
""";

    private static string SlideMasterXml() =>
        """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sldMaster xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
  <p:cSld>
    <p:spTree>
      <p:nvGrpSpPr>
        <p:cNvPr id="1" name=""/>
        <p:cNvGrpSpPr/>
        <p:nvPr/>
      </p:nvGrpSpPr>
      <p:grpSpPr/>
    </p:spTree>
  </p:cSld>
  <p:clrMap bg1="lt1" tx1="dk1" bg2="lt2" tx2="dk2" accent1="accent1" accent2="accent2" accent3="accent3" accent4="accent4" accent5="accent5" accent6="accent6" hlink="hlink" folHlink="folHlink"/>
  <p:sldLayoutIdLst>
    <p:sldLayoutId id="2147483649" r:id="rIdLayout1"/>
  </p:sldLayoutIdLst>
  <p:txStyles>
    <p:titleStyle>
      <a:lvl1pPr algn="l">
        <a:defRPr sz="3200" kern="1200">
          <a:solidFill><a:schemeClr val="tx1"/></a:solidFill>
          <a:latin typeface="Microsoft JhengHei"/>
          <a:ea typeface="Microsoft JhengHei"/>
          <a:cs typeface="Microsoft JhengHei"/>
        </a:defRPr>
      </a:lvl1pPr>
    </p:titleStyle>
    <p:bodyStyle>
      <a:lvl1pPr marL="0" indent="0" algn="l">
        <a:defRPr sz="1800" kern="1200">
          <a:solidFill><a:schemeClr val="tx1"/></a:solidFill>
          <a:latin typeface="Microsoft JhengHei"/>
          <a:ea typeface="Microsoft JhengHei"/>
          <a:cs typeface="Microsoft JhengHei"/>
        </a:defRPr>
      </a:lvl1pPr>
    </p:bodyStyle>
    <p:otherStyle>
      <a:lvl1pPr marL="0" indent="0" algn="l">
        <a:defRPr sz="1800" kern="1200">
          <a:solidFill><a:schemeClr val="tx1"/></a:solidFill>
          <a:latin typeface="Microsoft JhengHei"/>
          <a:ea typeface="Microsoft JhengHei"/>
          <a:cs typeface="Microsoft JhengHei"/>
        </a:defRPr>
      </a:lvl1pPr>
    </p:otherStyle>
  </p:txStyles>
</p:sldMaster>
""";

    private static string ThemeXml() =>
        """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Mermaid2Pptx">
  <a:themeElements>
    <a:clrScheme name="Mermaid2Pptx">
      <a:dk1><a:srgbClr val="111111"/></a:dk1>
      <a:lt1><a:srgbClr val="FFFFFF"/></a:lt1>
      <a:dk2><a:srgbClr val="444444"/></a:dk2>
      <a:lt2><a:srgbClr val="EEEEEE"/></a:lt2>
      <a:accent1><a:srgbClr val="2563EB"/></a:accent1>
      <a:accent2><a:srgbClr val="16A34A"/></a:accent2>
      <a:accent3><a:srgbClr val="DC2626"/></a:accent3>
      <a:accent4><a:srgbClr val="F59E0B"/></a:accent4>
      <a:accent5><a:srgbClr val="7C3AED"/></a:accent5>
      <a:accent6><a:srgbClr val="0891B2"/></a:accent6>
      <a:hlink><a:srgbClr val="2563EB"/></a:hlink>
      <a:folHlink><a:srgbClr val="7C3AED"/></a:folHlink>
    </a:clrScheme>
    <a:fontScheme name="Mermaid2Pptx">
      <a:majorFont><a:latin typeface="Arial"/><a:ea typeface=""/><a:cs typeface=""/></a:majorFont>
      <a:minorFont><a:latin typeface="Arial"/><a:ea typeface=""/><a:cs typeface=""/></a:minorFont>
    </a:fontScheme>
    <a:fmtScheme name="Mermaid2Pptx">
      <a:fillStyleLst>
        <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
        <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
        <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
      </a:fillStyleLst>
      <a:lnStyleLst>
        <a:ln w="9525"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln>
        <a:ln w="25400"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln>
        <a:ln w="38100"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln>
      </a:lnStyleLst>
      <a:effectStyleLst>
        <a:effectStyle><a:effectLst/></a:effectStyle>
        <a:effectStyle><a:effectLst/></a:effectStyle>
        <a:effectStyle><a:effectLst/></a:effectStyle>
      </a:effectStyleLst>
      <a:bgFillStyleLst>
        <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
        <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
        <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
      </a:bgFillStyleLst>
    </a:fmtScheme>
  </a:themeElements>
  <a:objectDefaults/>
  <a:extraClrSchemeLst/>
</a:theme>
""";

    private readonly record struct SvgColor(int R, int G, int B, double Alpha)
    {
        private static readonly IReadOnlyDictionary<string, SvgColor> Named = new Dictionary<string, SvgColor>(StringComparer.OrdinalIgnoreCase)
        {
            ["black"] = new(0, 0, 0, 1),
            ["white"] = new(255, 255, 255, 1),
            ["red"] = new(255, 0, 0, 1),
            ["green"] = new(0, 128, 0, 1),
            ["blue"] = new(0, 0, 255, 1),
            ["yellow"] = new(255, 255, 0, 1),
            ["orange"] = new(255, 165, 0, 1),
            ["purple"] = new(128, 0, 128, 1),
            ["gray"] = new(128, 128, 128, 1),
            ["grey"] = new(128, 128, 128, 1),
            ["transparent"] = new(0, 0, 0, 0)
        };

        public static SvgColor? Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var color = value.Trim().Trim('"', '\'');
            if (color.StartsWith("#", StringComparison.Ordinal))
            {
                return ParseHex(color[1..]);
            }

            var rgb = RgbRegex().Match(color);
            if (rgb.Success)
            {
                var parts = SplitRgbParts(rgb.Groups["args"].Value);
                if (parts.Length >= 3)
                {
                    return new SvgColor(
                        ParseColorPart(parts[0]),
                        ParseColorPart(parts[1]),
                        ParseColorPart(parts[2]),
                        parts.Length >= 4 ? SvgNumber.Parse(parts[3], 1) : 1);
                }
            }

            var hsl = HslRegex().Match(color);
            if (hsl.Success)
            {
                var parts = SplitRgbParts(hsl.Groups["args"].Value);
                if (parts.Length >= 3)
                {
                    return FromHsl(
                        SvgNumber.Parse(parts[0]),
                        SvgNumber.Parse(parts[1]) / 100d,
                        SvgNumber.Parse(parts[2]) / 100d,
                        parts.Length >= 4 ? SvgNumber.Parse(parts[3], 1) : 1);
                }
            }

            return Named.TryGetValue(color, out var named) ? named : null;
        }

        private static SvgColor FromHsl(double hue, double saturation, double lightness, double alpha)
        {
            hue = ((hue % 360) + 360) % 360 / 360d;
            saturation = Math.Max(0, Math.Min(1, saturation));
            lightness = Math.Max(0, Math.Min(1, lightness));

            if (saturation == 0)
            {
                var gray = ToByte(lightness);
                return new SvgColor(gray, gray, gray, alpha);
            }

            var q = lightness < 0.5
                ? lightness * (1 + saturation)
                : lightness + saturation - lightness * saturation;
            var p = 2 * lightness - q;
            return new SvgColor(
                ToByte(HueToRgb(p, q, hue + 1d / 3d)),
                ToByte(HueToRgb(p, q, hue)),
                ToByte(HueToRgb(p, q, hue - 1d / 3d)),
                alpha);
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0)
            {
                t += 1;
            }
            if (t > 1)
            {
                t -= 1;
            }
            if (t < 1d / 6d)
            {
                return p + (q - p) * 6 * t;
            }
            if (t < 1d / 2d)
            {
                return q;
            }
            return t < 2d / 3d ? p + (q - p) * (2d / 3d - t) * 6 : p;
        }

        private static int ToByte(double value) => (int)Math.Round(Math.Max(0, Math.Min(1, value)) * 255);

        private static string[] SplitRgbParts(string args)
        {
            if (args.Contains(','))
            {
                return args.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            return args
                .Replace("/", " ", StringComparison.Ordinal)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static SvgColor? ParseHex(string hex)
        {
            if (hex.Length == 3)
            {
                hex = string.Concat(hex.Select(ch => new string(ch, 2)));
            }

            if (hex.Length < 6)
            {
                return null;
            }

            return int.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
                   int.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
                   int.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
                ? new SvgColor(r, g, b, 1)
                : null;
        }

        private static int ParseColorPart(string value)
        {
            value = value.Trim();
            if (value.EndsWith('%'))
            {
                return (int)Math.Round(Math.Max(0, Math.Min(100, SvgNumber.Parse(value))) * 255d / 100d);
            }

            return (int)Math.Round(Math.Max(0, Math.Min(255, SvgNumber.Parse(value))));
        }
    }

    [GeneratedRegex(@"rgba?\((?<args>[^)]*)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RgbRegex();

    [GeneratedRegex(@"hsla?\((?<args>[^)]*)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HslRegex();
}
