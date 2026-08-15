using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Xunit;

namespace Mermaid2Pptx.Tests;

public sealed class DrawIoConversionTests
{
    [Fact]
    public void LooksLikeDrawIo_detects_mxfile_and_mxGraphModel()
    {
        Assert.True(DrawIoDocumentParser.LooksLikeDrawIo("<mxfile><diagram/></mxfile>"));
        Assert.True(DrawIoDocumentParser.LooksLikeDrawIo("  \n<mxGraphModel></mxGraphModel>"));
        Assert.False(DrawIoDocumentParser.LooksLikeDrawIo("flowchart LR\nA-->B"));
        Assert.False(DrawIoDocumentParser.LooksLikeDrawIo(""));
    }

    [Fact]
    public void Parses_vertices_edges_html_labels_and_group_offsets()
    {
        var scenes = new DrawIoDocumentParser().Parse(SampleMarkup());
        var scene = Assert.Single(scenes);

        var ellipse = Assert.Single(scene.Elements.OfType<SvgPresetShapeElement>(), shape => shape.PresetGeometry == "ellipse" && shape.X == 80);
        Assert.Equal("#d5e8d4", ellipse.Style.Fill);
        Assert.Equal("#82b366", ellipse.Style.Stroke);

        var diamond = Assert.Single(scene.Elements.OfType<SvgPresetShapeElement>(), shape => shape.PresetGeometry == "diamond");
        Assert.Equal(70, diamond.X);

        var rounded = Assert.Single(scene.Elements.OfType<SvgPresetShapeElement>(), shape => shape.PresetGeometry == "roundRect");
        Assert.Equal("#dae8fc", rounded.Style.Fill);

        Assert.Equal(3, scene.Elements.OfType<SvgLineElement>().Count());
        var dashed = Assert.Single(scene.Elements.OfType<SvgLineElement>(), line => line.Style.StrokeDashArray is not null);
        Assert.Equal("classic", dashed.Style.MarkerEnd);

        var startLabel = Assert.Single(scene.Elements.OfType<SvgTextElement>(), text => text.Lines.Any(line => line.Text == "Start"));
        Assert.Equal("center", startLabel.Style.TextAlign);

        var htmlLabel = Assert.Single(scene.Elements.OfType<SvgTextElement>(), text => text.Lines.Any(line => line.Text.Contains("Line two", StringComparison.Ordinal)));
        Assert.Equal("Hello", htmlLabel.Lines[0].Text);
        Assert.Equal("Line two", htmlLabel.Lines[1].Text);

        var child = Assert.Single(scene.Elements.OfType<SvgPresetShapeElement>(), shape => shape.ElementId == "child");
        Assert.Equal(50, child.X);
        Assert.Equal(50, child.Y);
    }

    [Fact]
    public void Parses_compressed_diagram_pages_and_bare_mxGraphModel()
    {
        var inner = """
<mxGraphModel>
  <root>
    <mxCell id="0"/>
    <mxCell id="1" parent="0"/>
    <mxCell id="n1" value="Compressed" style="rounded=1;whiteSpace=wrap;html=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
      <mxGeometry x="20" y="20" width="120" height="40" as="geometry"/>
    </mxCell>
  </root>
</mxGraphModel>
""";
        var compressedFile = $"<mxfile><diagram name=\"Page\">{CompressDiagram(inner)}</diagram></mxfile>";
        var compressedScene = Assert.Single(new DrawIoDocumentParser().Parse(compressedFile));
        Assert.Contains(compressedScene.Elements.OfType<SvgPresetShapeElement>(), shape => shape.PresetGeometry == "roundRect");
        Assert.Contains(compressedScene.Elements.OfType<SvgTextElement>(), text => text.Lines.Any(line => line.Text == "Compressed"));

        var bareScene = Assert.Single(new DrawIoDocumentParser().Parse(inner));
        Assert.Contains(bareScene.Elements.OfType<SvgTextElement>(), text => text.Lines.Any(line => line.Text == "Compressed"));
    }

    [Fact]
    public void Multiple_diagram_pages_become_multiple_slides()
    {
        var xml = """
<mxfile>
  <diagram name="One">
    <mxGraphModel>
      <root>
        <mxCell id="0"/><mxCell id="1" parent="0"/>
        <mxCell id="a" value="A" style="whiteSpace=wrap;html=1;" vertex="1" parent="1">
          <mxGeometry x="10" y="10" width="80" height="40" as="geometry"/>
        </mxCell>
      </root>
    </mxGraphModel>
  </diagram>
  <diagram name="Two">
    <mxGraphModel>
      <root>
        <mxCell id="0"/><mxCell id="1" parent="0"/>
        <mxCell id="b" value="B" style="ellipse;whiteSpace=wrap;html=1;" vertex="1" parent="1">
          <mxGeometry x="10" y="10" width="80" height="40" as="geometry"/>
        </mxCell>
      </root>
    </mxGraphModel>
  </diagram>
</mxfile>
""";
        var output = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-drawio-pages-{Guid.NewGuid():N}.pptx");
        var result = new MermaidPptxConverter().ConvertDrawIoXml(xml, output);

        Assert.Equal(2, result.SlideCount);
        Assert.True(result.NativeShapeCount >= 2);
        AssertNativeDeck(output, minimumShapes: 2, expectedSlides: 2);
    }

    [Fact]
    public void Converts_drawio_flowchart_to_native_drawingml()
    {
        var output = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-drawio-{Guid.NewGuid():N}.pptx");
        var result = new MermaidPptxConverter().ConvertDrawIoXml(SampleMarkup(), output);

        Assert.True(result.NativeShapeCount >= 5);
        var slideXml = AssertNativeDeck(output, minimumShapes: 5, expectedSlides: 1);
        Assert.Contains("""<a:prstGeom prst="ellipse">""", slideXml);
        Assert.Contains("""<a:prstGeom prst="diamond">""", slideXml);
        Assert.Contains("""<a:prstGeom prst="roundRect">""", slideXml);
        Assert.Contains("<p:cxnSp>", slideXml);
        Assert.Contains("""<a:tailEnd type="triangle" w="med" len="med"/>""", slideXml);
        Assert.Contains("""<a:srgbClr val="DAE8FC">""", slideXml);
        Assert.Contains("""<a:srgbClr val="FFF2CC">""", slideXml);
        Assert.Contains("""<a:prstDash val="dash"/>""", slideXml);
        Assert.Contains("Start", slideXml);
        Assert.Contains("Approved?", slideXml);
        Assert.Contains("Yes", slideXml);
        Assert.DoesNotContain("Marker ", slideXml);
    }

    [Fact]
    public void Converts_sample_drawio_file()
    {
        var sample = FindRepoPath("samples/flowchart.drawio");
        Assert.True(File.Exists(sample), $"Missing sample draw.io file at {sample}");
        var output = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-drawio-sample-{Guid.NewGuid():N}.pptx");
        var result = new MermaidPptxConverter().ConvertDrawIoFileAsync(sample, output).GetAwaiter().GetResult();
        Assert.Equal(1, result.SlideCount);
        AssertNativeDeck(output, minimumShapes: 4, expectedSlides: 1);
    }

    [Fact]
    public void Maps_flowchart_stencils_to_powerpoint_presets()
    {
        var xml = """
<mxGraphModel>
  <root>
    <mxCell id="0"/><mxCell id="1" parent="0"/>
    <mxCell id="term" value="Term" style="shape=mxgraph.flowchart.terminator;whiteSpace=wrap;html=1;" vertex="1" parent="1">
      <mxGeometry x="10" y="10" width="120" height="40" as="geometry"/>
    </mxCell>
    <mxCell id="prep" value="Prep" style="shape=mxgraph.flowchart.preparation;whiteSpace=wrap;html=1;" vertex="1" parent="1">
      <mxGeometry x="10" y="70" width="120" height="40" as="geometry"/>
    </mxCell>
    <mxCell id="doc" value="Doc" style="shape=mxgraph.flowchart.document;whiteSpace=wrap;html=1;" vertex="1" parent="1">
      <mxGeometry x="10" y="130" width="120" height="40" as="geometry"/>
    </mxCell>
  </root>
</mxGraphModel>
""";
        var scene = Assert.Single(new DrawIoDocumentParser().Parse(xml));
        var presets = scene.Elements.OfType<SvgPresetShapeElement>().Select(shape => shape.PresetGeometry).ToArray();
        Assert.Contains("flowchartTerminator", presets);
        Assert.Contains("flowchartPreparation", presets);
        Assert.Contains("flowchartDocument", presets);

        var output = Path.Combine(Path.GetTempPath(), $"mermaid2pptx-drawio-stencils-{Guid.NewGuid():N}.pptx");
        new MermaidPptxConverter().ConvertDrawIoXml(xml, output);
        var slideXml = AssertNativeDeck(output, minimumShapes: 3, expectedSlides: 1);
        Assert.Contains("""prst="flowchartTerminator"""", slideXml);
        Assert.Contains("""prst="flowchartPreparation"""", slideXml);
        Assert.Contains("""prst="flowchartDocument"""", slideXml);
    }

    [Fact]
    public void Object_wrapper_uses_label_and_id()
    {
        var xml = """
<mxGraphModel>
  <root>
    <mxCell id="0"/><mxCell id="1" parent="0"/>
    <object label="Wrapped" id="obj-1">
      <mxCell style="ellipse;whiteSpace=wrap;html=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
        <mxGeometry x="30" y="30" width="100" height="40" as="geometry"/>
      </mxCell>
    </object>
  </root>
</mxGraphModel>
""";
        var scene = Assert.Single(new DrawIoDocumentParser().Parse(xml));
        Assert.Contains(scene.Elements.OfType<SvgPresetShapeElement>(), shape => shape.ElementId == "obj-1" && shape.PresetGeometry == "ellipse");
        Assert.Contains(scene.Elements.OfType<SvgTextElement>(), text => text.Lines.Any(line => line.Text == "Wrapped"));
    }

    private static string SampleMarkup() => """
<mxfile host="app.diagrams.net">
  <diagram id="flow-1" name="Approval">
    <mxGraphModel>
      <root>
        <mxCell id="0"/>
        <mxCell id="1" parent="0"/>
        <mxCell id="start" value="Start" style="ellipse;whiteSpace=wrap;html=1;fillColor=#d5e8d4;strokeColor=#82b366;" vertex="1" parent="1">
          <mxGeometry x="80" y="40" width="120" height="60" as="geometry"/>
        </mxCell>
        <mxCell id="decision" value="Approved?" style="rhombus;whiteSpace=wrap;html=1;fillColor=#fff2cc;strokeColor=#d6b656;" vertex="1" parent="1">
          <mxGeometry x="70" y="160" width="140" height="80" as="geometry"/>
        </mxCell>
        <mxCell id="process" value="Native PPTX shapes" style="rounded=1;whiteSpace=wrap;html=1;fillColor=#dae8fc;strokeColor=#6c8ebf;" vertex="1" parent="1">
          <mxGeometry x="300" y="170" width="180" height="60" as="geometry"/>
        </mxCell>
        <mxCell id="html" value="Hello&lt;br&gt;Line two" style="rounded=0;whiteSpace=wrap;html=1;fillColor=#ffffff;strokeColor=#000000;" vertex="1" parent="1">
          <mxGeometry x="300" y="40" width="140" height="50" as="geometry"/>
        </mxCell>
        <mxCell id="group" value="" style="group" vertex="1" parent="1">
          <mxGeometry x="40" y="40" width="200" height="80" as="geometry"/>
        </mxCell>
        <mxCell id="child" value="Child" style="rounded=0;whiteSpace=wrap;html=1;fillColor=#f5f5f5;strokeColor=#666666;" vertex="1" parent="group">
          <mxGeometry x="10" y="10" width="80" height="40" as="geometry"/>
        </mxCell>
        <mxCell id="e1" style="endArrow=classic;html=1;exitX=0.5;exitY=1;entryX=0.5;entryY=0;strokeWidth=2;" edge="1" parent="1" source="start" target="decision">
          <mxGeometry relative="1" as="geometry"/>
        </mxCell>
        <mxCell id="e2" value="Yes" style="endArrow=classic;html=1;exitX=1;exitY=0.5;entryX=0;entryY=0.5;strokeWidth=2;" edge="1" parent="1" source="decision" target="process">
          <mxGeometry relative="1" as="geometry"/>
        </mxCell>
        <mxCell id="e3" style="endArrow=classic;html=1;dashed=1;dashPattern=8 8;exitX=0.5;exitY=1;entryX=0.5;entryY=0;strokeWidth=2;" edge="1" parent="1" source="process" target="html">
          <mxGeometry relative="1" as="geometry"/>
        </mxCell>
      </root>
    </mxGraphModel>
  </diagram>
</mxfile>
""";

    private static string FindRepoPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate '{relativePath}' from {AppContext.BaseDirectory}.");
    }

    private static string CompressDiagram(string xml)
    {
        var encoded = Uri.EscapeDataString(xml);
        var bytes = Encoding.UTF8.GetBytes(encoded);
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(bytes);
        }

        return Convert.ToBase64String(output.ToArray());
    }

    private static string AssertNativeDeck(string output, int minimumShapes, int expectedSlides)
    {
        using var document = PresentationDocument.Open(output, false);
        Assert.NotNull(document.PresentationPart);
        Assert.Empty(document.PresentationPart!.GetPartsOfType<ImagePart>());
        Assert.Equal(expectedSlides, document.PresentationPart.SlideParts.Count());
        var validationErrors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(document).ToArray();
        Assert.True(validationErrors.Length == 0, string.Join(Environment.NewLine, validationErrors.Select(error => error.Description)));

        using var zip = ZipFile.OpenRead(output);
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase));

        var slideXml = ReadZipEntry(zip, "ppt/slides/slide1.xml");
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var xml = XDocument.Parse(slideXml);
        var nativeShapeCount = xml.Descendants(p + "sp").Count() + xml.Descendants(p + "cxnSp").Count();
        Assert.True(nativeShapeCount >= minimumShapes);
        Assert.DoesNotContain(xml.Descendants(p + "pic"), _ => true);
        return slideXml;
    }

    private static string ReadZipEntry(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new InvalidOperationException($"Missing zip entry {path}.");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
