using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Xunit;

namespace Mermaid2Pptx.Tests;

public sealed class ConversionTests
{
    [Theory]
    [MemberData(nameof(SvgCases))]
    public void Converts_visible_svg_elements_to_native_shapes(string name, string body, int minimumShapes)
    {
        var svg = Svg(body);
        var output = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-{name}-{Guid.NewGuid():N}.pptx");

        var scene = new SvgDocumentParser().Parse(svg);
        var slide = new SvgToPowerPointMapper().MapSvgToSlide(scene, 13.333, 7.5);
        var deck = new PptxDeckModel { WidthInches = 13.333, HeightInches = 7.5 };
        deck.Slides.Add(slide);

        new DrawingMlWriter().Write(deck, output);

        using var document = PresentationDocument.Open(output, false);
        Assert.NotNull(document.PresentationPart);
        Assert.Empty(document.PresentationPart!.GetPartsOfType<ImagePart>());
        Assert.Single(document.PresentationPart.SlideParts);
        var validationErrors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(document).ToArray();
        Assert.True(validationErrors.Length == 0, string.Join(Environment.NewLine, validationErrors.Select(error => error.Description)));

        var slidePart = document.PresentationPart.SlideParts.Single();
        using var stream = slidePart.GetStream();
        var xml = XDocument.Load(stream);
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";

        var nativeShapeCount = xml.Descendants(p + "sp").Count() + xml.Descendants(p + "cxnSp").Count();
        Assert.True(nativeShapeCount >= minimumShapes);
        Assert.DoesNotContain(xml.Descendants(p + "pic"), _ => true);
        Assert.Contains(xml.Descendants(a + "prstGeom").Concat(xml.Descendants(a + "custGeom")), _ => true);

        using var zip = ZipFile.OpenRead(output);
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(zip.Entries, entry => entry.FullName == "ppt/slideLayouts/_rels/slideLayout1.xml.rels");

        var contentTypes = ReadZipEntry(zip, "[Content_Types].xml");
        Assert.Contains("""<Default Extension="xml" ContentType="application/xml"/>""", contentTypes);
        Assert.Contains("PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"", contentTypes);

        var layoutRelationships = ReadZipEntry(zip, "ppt/slideLayouts/_rels/slideLayout1.xml.rels");
        Assert.Contains("officeDocument/2006/relationships/slideMaster", layoutRelationships);
    }

    [Fact]
    public void Path_parser_normalizes_relative_smooth_and_arc_commands()
    {
        var parser = new SvgPathParser();
        var segments = parser.Parse("m 10 10 h 80 v 20 s 20 20 40 0 t 20 0 a 20 10 0 0 1 30 20 z");

        Assert.IsType<MoveTo>(segments[0]);
        Assert.Contains(segments, segment => segment is LineTo);
        Assert.Contains(segments, segment => segment is CubicBezierTo);
        Assert.Contains(segments, segment => segment is QuadraticBezierTo);
        Assert.IsType<ClosePath>(segments[^1]);
    }

    [Fact]
    public void Css_class_and_nested_transform_are_applied()
    {
        var scene = new SvgDocumentParser().Parse(Svg("""
<style>
  .node rect { fill: #ffeeaa; stroke: rgb(10, 20, 30); stroke-width: 3; }
</style>
<g class="node" transform="translate(10,20) scale(2)">
  <rect x="5" y="6" width="10" height="11"/>
</g>
"""));

        var rect = Assert.IsType<SvgRectElement>(Assert.Single(scene.Elements));
        Assert.Equal("#ffeeaa", rect.Style.Fill);
        Assert.Equal("rgb(10, 20, 30)", rect.Style.Stroke);
        Assert.Equal(3, rect.Style.StrokeWidth);
        Assert.Equal(new SvgPoint(20, 32), rect.Transform.Transform(new SvgPoint(5, 6)));
    }

    [Fact]
    public void Scoped_mermaid_css_colors_are_preserved_in_drawingml()
    {
        var svg = """
<svg id="mermaid-test" xmlns="http://www.w3.org/2000/svg" width="300" height="160" viewBox="0 0 300 160">
  <style>
    #mermaid-test .node rect { fill: #ECECFF; stroke: #9370DB; stroke-width: 2px; }
    #mermaid-test .edgePath path { fill: none; stroke: #333333; stroke-width: 3px; }
    #mermaid-test .nodeLabel { color: #111827; font-size: 16px; }
  </style>
  <g class="node" transform="translate(70,50)">
    <rect x="-40" y="-20" width="80" height="40" rx="4" ry="4"/>
    <foreignObject x="-30" y="-10" width="60" height="24">
      <div xmlns="http://www.w3.org/1999/xhtml"><span class="nodeLabel">Start</span></div>
    </foreignObject>
  </g>
  <g class="edgePath"><path d="M 120 50 C 150 50, 170 50, 200 50"/></g>
</svg>
""";

        var output = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-colors-{Guid.NewGuid():N}.pptx");
        var scene = new SvgDocumentParser().Parse(svg);
        var slide = new SvgToPowerPointMapper().MapSvgToSlide(scene, 13.333, 7.5);
        var deck = new PptxDeckModel { WidthInches = 13.333, HeightInches = 7.5 };
        deck.Slides.Add(slide);
        new DrawingMlWriter().Write(deck, output);

        using var zip = ZipFile.OpenRead(output);
        var slideXml = ReadZipEntry(zip, "ppt/slides/slide1.xml");
        Assert.Contains("""<a:srgbClr val="ECECFF">""", slideXml);
        Assert.Contains("""<a:srgbClr val="9370DB">""", slideXml);
        Assert.Contains("""<a:srgbClr val="333333">""", slideXml);
        Assert.Contains("""<a:srgbClr val="111827">""", slideXml);
    }

    [Fact]
    public void Marker_end_is_written_as_native_powerpoint_line_arrowhead()
    {
        var svg = Svg("""
<defs>
  <marker id="arrow" markerWidth="10" markerHeight="10" refX="8" refY="5" orient="auto">
    <path d="M 0 0 L 10 5 L 0 10 z"/>
  </marker>
</defs>
<path d="M 20 80 C 80 20, 140 20, 200 80" fill="none" stroke="#333333" stroke-width="2" marker-end="url(#arrow)"/>
""");
        var output = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-arrow-{Guid.NewGuid():N}.pptx");
        var scene = new SvgDocumentParser().Parse(svg);
        var slide = new SvgToPowerPointMapper().MapSvgToSlide(scene, 13.333, 7.5);
        Assert.Single(slide.Shapes);
        Assert.True(slide.Shapes[0].ArrowEnd);

        var deck = new PptxDeckModel { WidthInches = 13.333, HeightInches = 7.5 };
        deck.Slides.Add(slide);
        new DrawingMlWriter().Write(deck, output);

        using var zip = ZipFile.OpenRead(output);
        var slideXml = ReadZipEntry(zip, "ppt/slides/slide1.xml");
        Assert.Contains("<a:custGeom>", slideXml);
        Assert.Contains("""<a:tailEnd type="triangle" w="med" len="med"/>""", slideXml);
        Assert.DoesNotContain("""<a:headEnd type="triangle" w="med" len="med"/>""", slideXml);
        Assert.DoesNotContain("Marker ", slideXml);
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Svg_line_is_written_as_native_connector_with_dash_and_arrowhead()
    {
        var svg = Svg("""
<style>
  .messageLine1 { stroke: #333333; stroke-width: 2px; stroke-dasharray: 3, 3; }
</style>
<defs>
  <marker id="arrow" markerWidth="10" markerHeight="10" refX="8" refY="5" orient="auto">
    <path d="M 0 0 L 10 5 L 0 10 z"/>
  </marker>
</defs>
<line class="messageLine1" x1="220" y1="80" x2="30" y2="80" stroke="none" marker-end="url(#arrow)"/>
""");
        var output = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-dashed-line-{Guid.NewGuid():N}.pptx");
        var scene = new SvgDocumentParser().Parse(svg);
        var line = Assert.IsType<SvgLineElement>(Assert.Single(scene.Elements));
        Assert.Equal("#333333", line.Style.Stroke);
        Assert.Equal("3, 3", line.Style.StrokeDashArray);

        var slide = new SvgToPowerPointMapper().MapSvgToSlide(scene, 13.333, 7.5);
        var lineShape = Assert.Single(slide.Shapes);
        Assert.Equal(PptxShapeKind.Line, lineShape.Kind);
        Assert.True(lineShape.ArrowEnd);
        Assert.True(lineShape.FlipH);

        var deck = new PptxDeckModel { WidthInches = 13.333, HeightInches = 7.5 };
        deck.Slides.Add(slide);
        new DrawingMlWriter().Write(deck, output);

        using var zip = ZipFile.OpenRead(output);
        var slideXml = ReadZipEntry(zip, "ppt/slides/slide1.xml");
        Assert.Contains("<p:cxnSp>", slideXml);
        Assert.Contains("""<a:prstGeom prst="line"><a:avLst/></a:prstGeom>""", slideXml);
        Assert.Contains("""<a:prstDash val="dash"/>""", slideXml);
        Assert.Contains("""<a:tailEnd type="triangle" w="med" len="med"/>""", slideXml);
        Assert.DoesNotContain("""<a:headEnd type="triangle" w="med" len="med"/>""", slideXml);
        Assert.Contains("""<a:srgbClr val="333333">""", slideXml);
        Assert.DoesNotContain("Marker ", slideXml);
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Left_to_right_svg_line_marker_end_uses_powerpoint_tail_end()
    {
        var svg = Svg("""
<defs>
  <marker id="arrow" markerWidth="10" markerHeight="10" refX="8" refY="5" orient="auto">
    <path d="M 0 0 L 10 5 L 0 10 z"/>
  </marker>
</defs>
<line x1="30" y1="80" x2="220" y2="80" stroke="#333333" stroke-width="2" marker-end="url(#arrow)"/>
""");
        var output = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-ltr-line-{Guid.NewGuid():N}.pptx");
        var scene = new SvgDocumentParser().Parse(svg);
        var slide = new SvgToPowerPointMapper().MapSvgToSlide(scene, 13.333, 7.5);
        var lineShape = Assert.Single(slide.Shapes);
        Assert.Equal(PptxShapeKind.Line, lineShape.Kind);
        Assert.False(lineShape.FlipH);
        Assert.True(lineShape.ArrowEnd);

        var deck = new PptxDeckModel { WidthInches = 13.333, HeightInches = 7.5 };
        deck.Slides.Add(slide);
        new DrawingMlWriter().Write(deck, output);

        using var zip = ZipFile.OpenRead(output);
        var slideXml = ReadZipEntry(zip, "ppt/slides/slide1.xml");
        Assert.Contains("""<a:tailEnd type="triangle" w="med" len="med"/>""", slideXml);
        Assert.DoesNotContain("""<a:headEnd type="triangle" w="med" len="med"/>""", slideXml);
    }

    [Fact]
    public void Pptx_xml_audit_maps_svg_lines_to_connector_xml_endpoints()
    {
        var svg = Svg("""
<style>
  .messageLine0 { stroke: #333333; stroke-width: 2px; }
  .messageLine1 { stroke: #333333; stroke-width: 2px; stroke-dasharray: 3, 3; }
</style>
<defs>
  <marker id="arrow" markerWidth="10" markerHeight="10" refX="8" refY="5" orient="auto">
    <path d="M 0 0 L 10 5 L 0 10 z"/>
  </marker>
</defs>
<line class="messageLine0" x1="30" y1="60" x2="220" y2="60" marker-end="url(#arrow)"/>
<line class="messageLine1" x1="220" y1="100" x2="30" y2="100" marker-end="url(#arrow)"/>
""");
        var output = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-xml-audit-{Guid.NewGuid():N}.pptx");
        var scene = new SvgDocumentParser().Parse(svg);
        var slide = new SvgToPowerPointMapper().MapSvgToSlide(scene, 13.333, 7.5);
        var deck = new PptxDeckModel { WidthInches = 13.333, HeightInches = 7.5 };
        deck.Slides.Add(slide);
        new DrawingMlWriter().Write(deck, output);

        using var zip = ZipFile.OpenRead(output);
        var slideXml = ReadZipEntry(zip, "ppt/slides/slide1.xml");
        var audit = new PptxXmlAudit().Compare(scene, slide, slideXml);
        Assert.Equal(0, audit.FailureCount);

        var lineItems = audit.Items.Where(item => item.Svg.Tag == "line").ToArray();
        Assert.Equal(2, lineItems.Length);
        Assert.True(lineItems[0].Pptx?.HasTailEnd);
        Assert.False(lineItems[0].Pptx?.HasHeadEnd);
        Assert.False(lineItems[0].Pptx?.FlipH);
        Assert.False(lineItems[1].Pptx?.HasHeadEnd);
        Assert.True(lineItems[1].Pptx?.HasTailEnd);
        Assert.True(lineItems[1].Pptx?.FlipH);
        Assert.True(lineItems[1].Pptx?.HasDash);
    }

    [Fact]
    public void Er_cardinality_markers_remain_native_shapes_instead_of_triangle_arrowheads()
    {
        var svg = Svg("""
<style>
  .relationshipLine { stroke: #333333; stroke-width: 1px; fill: none; }
  .marker { fill: none; stroke: #333333; stroke-width: 1px; }
</style>
<defs>
  <marker id="er-onlyOneStart" class="marker onlyOne er" refX="0" refY="9" markerWidth="18" markerHeight="18" orient="auto">
    <path d="M9,0 L9,18 M15,0 L15,18"/>
  </marker>
  <marker id="er-zeroOrMoreEnd" class="marker zeroOrMore er" refX="39" refY="18" markerWidth="57" markerHeight="36" orient="auto">
    <circle fill="white" cx="9" cy="18" r="6"/>
    <path d="M21,18 Q39,0 57,18 Q39,36 21,18"/>
  </marker>
</defs>
<path class="relationshipLine" d="M 120 20 L 120 140" marker-start="url(#er-onlyOneStart)" marker-end="url(#er-zeroOrMoreEnd)"/>
""");
        var output = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-er-markers-{Guid.NewGuid():N}.pptx");
        var scene = new SvgDocumentParser().Parse(svg);
        var slide = new SvgToPowerPointMapper().MapSvgToSlide(scene, 13.333, 7.5);
        Assert.True(slide.Shapes.Count >= 4);
        Assert.False(slide.Shapes[0].ArrowStart);
        Assert.False(slide.Shapes[0].ArrowEnd);
        Assert.Contains(slide.Shapes.Skip(1), shape => shape.Name.StartsWith("Marker ", StringComparison.Ordinal));

        var deck = new PptxDeckModel { WidthInches = 13.333, HeightInches = 7.5 };
        deck.Slides.Add(slide);
        new DrawingMlWriter().Write(deck, output);

        using var zip = ZipFile.OpenRead(output);
        var slideXml = ReadZipEntry(zip, "ppt/slides/slide1.xml");
        Assert.Contains("Marker ", slideXml);
        Assert.Contains("""<a:prstGeom prst="ellipse">""", slideXml);
        Assert.Contains("<a:custGeom>", slideXml);
        Assert.DoesNotContain("<a:headEnd", slideXml);
        Assert.DoesNotContain("<a:tailEnd", slideXml);
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Class_extension_markers_remain_hollow_native_marker_shapes()
    {
        var svg = Svg("""
<style>
  .relation { stroke: #333333; stroke-width: 1px; fill: none; }
  .extension { fill: transparent; stroke: #333333; stroke-width: 1px; }
</style>
<defs>
  <marker id="class-extensionStart" class="marker extension class" refX="18" refY="7" markerWidth="20" markerHeight="28" orient="auto" markerUnits="userSpaceOnUse">
    <path d="M 1,7 L18,13 V 1 Z"/>
  </marker>
</defs>
<path class="relation" d="M 100 40 L 100 140" marker-start="url(#class-extensionStart)"/>
""");
        var output = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-class-extension-{Guid.NewGuid():N}.pptx");
        var scene = new SvgDocumentParser().Parse(svg);
        var slide = new SvgToPowerPointMapper().MapSvgToSlide(scene, 13.333, 7.5);
        Assert.True(slide.Shapes.Count >= 2);
        Assert.False(slide.Shapes[0].ArrowStart);
        var markerShape = Assert.Single(slide.Shapes.Skip(1), shape => shape.Name.StartsWith("Marker ", StringComparison.Ordinal));
        var markerTip = Assert.IsType<PptxMoveTo>(markerShape.PathCommands[0]).Point;
        Assert.True(markerTip.Y < markerShape.Cy / 2);

        var deck = new PptxDeckModel { WidthInches = 13.333, HeightInches = 7.5 };
        deck.Slides.Add(slide);
        new DrawingMlWriter().Write(deck, output);

        using var zip = ZipFile.OpenRead(output);
        var slideXml = ReadZipEntry(zip, "ppt/slides/slide1.xml");
        Assert.Contains("Marker ", slideXml);
        Assert.Contains("<a:noFill/>", slideXml);
        Assert.DoesNotContain("<a:headEnd", slideXml);
        Assert.DoesNotContain("<a:tailEnd", slideXml);
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Text_boxes_disable_powerpoint_auto_wrap()
    {
        var svg = Svg("""
<foreignObject x="110" y="70" width="28" height="20">
  <div xmlns="http://www.w3.org/1999/xhtml" style="text-align: center; font-size: 16px; color: #111111;">
    <span>contains</span>
  </div>
</foreignObject>
""");
        var output = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-nowrap-{Guid.NewGuid():N}.pptx");
        var scene = new SvgDocumentParser().Parse(svg);
        var slide = new SvgToPowerPointMapper().MapSvgToSlide(scene, 13.333, 7.5);
        var textShape = Assert.Single(slide.Shapes, shape => shape.Kind == PptxShapeKind.Text);
        var originalMappedWidth = UnitConversion.CreateViewportMap(scene.ViewBox, 13.333, 7.5).MapLengthX(28);
        Assert.True(textShape.Cx > originalMappedWidth);

        var deck = new PptxDeckModel { WidthInches = 13.333, HeightInches = 7.5 };
        deck.Slides.Add(slide);
        new DrawingMlWriter().Write(deck, output);

        using var zip = ZipFile.OpenRead(output);
        var slideXml = ReadZipEntry(zip, "ppt/slides/slide1.xml");
        Assert.Contains("<a:bodyPr wrap=\"none\"", slideXml);
        Assert.Contains("<a:noAutofit/>", slideXml);
        Assert.DoesNotContain("wrap=\"square\"", slideXml);
        Assert.Contains("txBox=\"1\"", slideXml);
    }

    [Fact]
    public void Single_node_label_merges_into_parent_shape_text_body()
    {
        var svg = Svg("""
<g class="node" transform="translate(150,80)">
  <rect x="-14" y="-10" width="28" height="20" fill="#ececff" stroke="#333333"/>
  <foreignObject x="-14" y="-10" width="28" height="20">
    <div xmlns="http://www.w3.org/1999/xhtml" style="text-align: center; font-size: 16px; color: #111111;">
      <span>contains</span>
    </div>
  </foreignObject>
</g>
""");

        var output = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-merge-text-{Guid.NewGuid():N}.pptx");
        var scene = new SvgDocumentParser().Parse(svg);
        var slide = new SvgToPowerPointMapper().MapSvgToSlide(scene, 13.333, 7.5);

        var nodeShape = Assert.Single(slide.Shapes);
        Assert.Equal(PptxShapeKind.Preset, nodeShape.Kind);
        Assert.Equal("rect", nodeShape.PresetGeometry);
        Assert.Equal("contains", nodeShape.Text);
        Assert.DoesNotContain(slide.Shapes, shape => shape.Kind == PptxShapeKind.Text);

        var originalMappedWidth = UnitConversion.CreateViewportMap(scene.ViewBox, 13.333, 7.5).MapLengthX(28);
        Assert.True(nodeShape.Cx > originalMappedWidth);

        var deck = new PptxDeckModel { WidthInches = 13.333, HeightInches = 7.5 };
        deck.Slides.Add(slide);
        new DrawingMlWriter().Write(deck, output);

        using var zip = ZipFile.OpenRead(output);
        var slideXml = ReadZipEntry(zip, "ppt/slides/slide1.xml");
        Assert.Contains("<p:txBody>", slideXml);
        Assert.Contains(">contains</a:t>", slideXml);
        Assert.Contains("""<a:srgbClr val="111111">""", slideXml);
        Assert.Contains("""<a:srgbClr val="ECECFF">""", slideXml);
        Assert.DoesNotContain("txBox=\"1\"", slideXml);

        var masterXml = ReadZipEntry(zip, "ppt/slideMasters/slideMaster1.xml");
        var themeXml = ReadZipEntry(zip, "ppt/slideMasters/theme/theme1.xml");
        Assert.DoesNotContain("Microsoft JhengHei", masterXml);
        Assert.DoesNotContain("Microsoft JhengHei", themeXml);
        Assert.Contains("""typeface="Arial""", masterXml);
        Assert.Contains("""typeface="Arial""", themeXml);
    }

    [Fact]
    public void Multiple_labels_in_same_container_remain_separate_text_boxes()
    {
        var svg = Svg("""
<g class="node" transform="translate(150,80)">
  <rect x="-60" y="-40" width="120" height="80" fill="#ececff" stroke="#333333"/>
  <foreignObject x="-50" y="-30" width="100" height="24">
    <div xmlns="http://www.w3.org/1999/xhtml" style="text-align: center; font-size: 14px; color: #111111;">
      <span>Title</span>
    </div>
  </foreignObject>
  <foreignObject x="-50" y="0" width="100" height="24">
    <div xmlns="http://www.w3.org/1999/xhtml" style="text-align: center; font-size: 14px; color: #111111;">
      <span>Member</span>
    </div>
  </foreignObject>
</g>
""");

        var scene = new SvgDocumentParser().Parse(svg);
        var slide = new SvgToPowerPointMapper().MapSvgToSlide(scene, 13.333, 7.5);

        var nodeShape = Assert.Single(slide.Shapes, shape => shape.Kind == PptxShapeKind.Preset);
        Assert.True(string.IsNullOrEmpty(nodeShape.Text));
        var textShapes = slide.Shapes.Where(shape => shape.Kind == PptxShapeKind.Text).ToArray();
        Assert.Equal(2, textShapes.Length);
        Assert.Contains(textShapes, shape => shape.Text == "Title");
        Assert.Contains(textShapes, shape => shape.Text == "Member");
    }

    [Fact]
    public void Font_family_fallback_writes_distinct_latin_and_east_asian_typefaces()
    {
        var svg = Svg("""
<foreignObject x="10" y="20" width="180" height="40">
  <div xmlns="http://www.w3.org/1999/xhtml" style="font-family: Arial, 'Noto Sans TC', sans-serif; font-size: 16px; color: #111111;">
    <span>Label</span>
  </div>
</foreignObject>
""");
        var output = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-font-fallback-{Guid.NewGuid():N}.pptx");
        var scene = new SvgDocumentParser().Parse(svg);
        var slide = new SvgToPowerPointMapper().MapSvgToSlide(scene, 13.333, 7.5);
        var deck = new PptxDeckModel { WidthInches = 13.333, HeightInches = 7.5 };
        deck.Slides.Add(slide);
        new DrawingMlWriter().Write(deck, output);

        using var zip = ZipFile.OpenRead(output);
        var slideXml = ReadZipEntry(zip, "ppt/slides/slide1.xml");
        Assert.Contains("""<a:latin typeface="Arial"/>""", slideXml);
        Assert.Contains("""<a:ea typeface="Noto Sans TC"/>""", slideXml);
    }

    [Fact]
    public void Browser_serialized_foreignobject_void_tags_are_normalized()
    {
        var scene = new SvgDocumentParser().Parse(Svg("""
<foreignObject x="10" y="20" width="180" height="50">
  <div xmlns="http://www.w3.org/1999/xhtml">
    <p>On effectiveness<br>and features</p>
  </div>
</foreignObject>
"""));

        var text = Assert.Single(scene.Elements.OfType<SvgTextElement>());
        Assert.Equal(["On effectiveness", "and features"], text.Lines.Select(line => line.Text).ToArray());
    }

    [Fact]
    public void Text_dx_dy_offsets_apply_to_direct_text_and_tspan_with_em_units()
    {
        var scene = new SvgDocumentParser().Parse(Svg("""
<text x="10" y="20" dx="0.5em" dy="1em" style="font-size: 16px;">Hello</text>
<text x="10" y="20" style="font-size: 16px;"><tspan x="10" dy="0.5em">Half</tspan></text>
<text x="10" y="-10.1" style="font-size: 16px;"><tspan x="0" y="-0.1em" dy="1.1em">Architecture</tspan></text>
"""));

        var texts = scene.Elements.OfType<SvgTextElement>().ToArray();
        Assert.Equal(3, texts.Length);
        Assert.Equal(18, texts[0].Lines.Single().X, 3);
        Assert.Equal(36, texts[0].Lines.Single().Y, 3);
        Assert.Equal(28, texts[1].Lines.Single().Y, 3);
        Assert.Equal(16, texts[2].Lines.Single().Y, 3);
    }

    [Fact]
    public void Inserts_native_shapes_into_existing_deck_without_media_or_duplicate_ids()
    {
        var source = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-insert-source-{Guid.NewGuid():N}.pptx");
        var target = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-insert-target-{Guid.NewGuid():N}.pptx");
        var output = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-insert-output-{Guid.NewGuid():N}.pptx");

        var sourceDeck = new PptxDeckModel { WidthInches = 13.333, HeightInches = 7.5 };
        sourceDeck.Slides.Add(NativeSlideFromSvg(Svg("""
<style>
  .messageLine1 { stroke: #333333; stroke-width: 2px; stroke-dasharray: 3, 3; }
</style>
<defs>
  <marker id="arrow" markerWidth="10" markerHeight="10" refX="8" refY="5" orient="auto">
    <path d="M 0 0 L 10 5 L 0 10 z"/>
  </marker>
</defs>
<line class="messageLine1" x1="30" y1="80" x2="220" y2="80" stroke="none" marker-end="url(#arrow)"/>
""")));
        sourceDeck.Slides.Add(NativeSlideFromSvg(Svg("""
<style>
  .relationshipLine { stroke: #333333; stroke-width: 1px; fill: none; }
  .marker { fill: none; stroke: #333333; stroke-width: 1px; }
</style>
<defs>
  <marker id="er-onlyOneStart" class="marker onlyOne er" refX="0" refY="9" markerWidth="18" markerHeight="18" orient="auto">
    <path d="M9,0 L9,18 M15,0 L15,18"/>
  </marker>
</defs>
<path class="relationshipLine" d="M 120 20 L 120 140" marker-start="url(#er-onlyOneStart)"/>
""")));
        new DrawingMlWriter().Write(sourceDeck, source);

        var targetDeck = new PptxDeckModel { WidthInches = 13.333, HeightInches = 7.5 };
        targetDeck.Slides.Add(NativeSlideFromSvg(Svg("""<rect x="20" y="20" width="80" height="30" fill="#ffffff" stroke="#111111"/>""")));
        targetDeck.Slides.Add(NativeSlideFromSvg(Svg("""<rect x="30" y="30" width="70" height="35" fill="#ffffff" stroke="#111111"/>""")));
        new DrawingMlWriter().Write(targetDeck, target);

        var result = new PptxShapeInserter().Insert(
            source,
            target,
            output,
            new Dictionary<int, int> { [1] = 1, [2] = 2 });

        Assert.Equal(2, result.MappedSlideCount);
        Assert.True(result.InsertedShapeCount >= 3);

        using var document = PresentationDocument.Open(output, false);
        Assert.NotNull(document.PresentationPart);
        Assert.Empty(document.PresentationPart!.GetPartsOfType<ImagePart>());
        Assert.Equal(2, document.PresentationPart.Presentation.SlideIdList!.Count());
        var validationErrors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(document).ToArray();
        Assert.True(validationErrors.Length == 0, string.Join(Environment.NewLine, validationErrors.Select(error => error.Description)));

        using var zip = ZipFile.OpenRead(output);
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase));
        var slide1Xml = ReadZipEntry(zip, "ppt/slides/slide1.xml");
        var slide2Xml = ReadZipEntry(zip, "ppt/slides/slide2.xml");
        Assert.Contains("<p:cxnSp>", slide1Xml);
        Assert.Contains("<a:prstDash val=\"dash\"", slide1Xml);
        Assert.Contains("<a:tailEnd type=\"triangle\"", slide1Xml);
        Assert.Contains("Marker ", slide2Xml);
        Assert.DoesNotContain("<p:pic", slide1Xml + slide2Xml);

        AssertUniqueShapeIds(slide1Xml);
        AssertUniqueShapeIds(slide2Xml);
    }

    [Fact]
    public void Slide_map_uses_target_equals_source_pairs()
    {
        var map = PptxShapeInserter.ParseSlideMap("5=1, 14=6");

        Assert.Equal(1, map[5]);
        Assert.Equal(6, map[14]);
    }

    public static IEnumerable<object[]> SvgCases()
    {
        yield return ["simple-rect", """<rect x="10" y="20" width="120" height="60" fill="#ececff" stroke="#333"/>""", 1];
        yield return ["rounded-rect", """<rect x="10" y="20" width="120" height="60" rx="8" ry="8" fill="#fff4dd" stroke="#333"/>""", 1];
        yield return ["circle", """<circle cx="80" cy="80" r="40" fill="white" stroke="black"/>""", 1];
        yield return ["ellipse", """<ellipse cx="90" cy="70" rx="70" ry="30" fill="#e0f2fe" stroke="#0369a1"/>""", 1];
        yield return ["line", """<line x1="10" y1="10" x2="190" y2="100" stroke="#333" stroke-width="2"/>""", 1];
        yield return ["polyline", """<polyline points="10,10 80,20 160,90" fill="none" stroke="#333"/>""", 1];
        yield return ["polygon", """<polygon points="50,10 120,80 10,80" fill="#dcfce7" stroke="#166534"/>""", 1];
        yield return ["cubic-path", """<path d="M 10 100 C 40 10, 160 10, 190 100" fill="none" stroke="#991b1b" stroke-width="3"/>""", 1];
        yield return ["arc-path", """<path d="M 40 90 A 50 30 0 0 1 160 90" fill="none" stroke="#7c3aed" stroke-width="3"/>""", 1];
        yield return ["mermaid-flowchart", MermaidFlowchartSvgBody, 4];
    }

    private const string MermaidFlowchartSvgBody = """
<style>
  .node rect { fill: #ECECFF; stroke: #9370DB; stroke-width: 1px; }
  .edgePath path { fill: none; stroke: #333333; stroke-width: 2px; }
  .edgeLabel text { fill: #111111; font-size: 14px; }
</style>
<g class="nodes">
  <g class="node" transform="translate(80,40)">
    <rect x="-45" y="-20" width="90" height="40" rx="5" ry="5"/>
    <text text-anchor="middle" dominant-baseline="central"><tspan x="0" y="5">Start</tspan></text>
  </g>
  <g class="node" transform="translate(220,40)">
    <rect x="-45" y="-20" width="90" height="40" rx="5" ry="5"/>
    <text text-anchor="middle"><tspan x="0" y="5">End</tspan></text>
  </g>
</g>
<g class="edgePaths">
  <g class="edgePath"><path d="M125 40 C155 40, 175 40, 175 40 S195 40, 175 40"/></g>
</g>
<g class="edgeLabels">
  <g class="edgeLabel" transform="translate(150,25)">
    <text text-anchor="middle"><tspan x="0" y="0">ok</tspan></text>
  </g>
</g>
""";

    private static string Svg(string body) =>
        $$"""
<svg xmlns="http://www.w3.org/2000/svg" width="300" height="160" viewBox="0 0 300 160">
{{body}}
</svg>
""";

    private static PptxSlideModel NativeSlideFromSvg(string svg)
    {
        var scene = new SvgDocumentParser().Parse(svg);
        return new SvgToPowerPointMapper().MapSvgToSlide(scene, 13.333, 7.5);
    }

    private static void AssertUniqueShapeIds(string slideXml)
    {
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var ids = XDocument.Parse(slideXml)
            .Descendants(p + "cNvPr")
            .Select(element => (string?)element.Attribute("id"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    private static string ReadZipEntry(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new InvalidOperationException($"Missing zip entry {path}.");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
