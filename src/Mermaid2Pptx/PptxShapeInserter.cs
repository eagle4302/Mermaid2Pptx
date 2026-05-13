using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace Mermaid2Pptx;

public sealed class PptxShapeInserter
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public PptxShapeInsertResult Insert(
        string sourcePptxPath,
        string targetPptxPath,
        string outputPath,
        IReadOnlyDictionary<int, int> slideMap)
    {
        if (string.IsNullOrWhiteSpace(sourcePptxPath))
        {
            throw new ArgumentException("Source PPTX is required.", nameof(sourcePptxPath));
        }

        if (string.IsNullOrWhiteSpace(targetPptxPath))
        {
            throw new ArgumentException("Target PPTX is required.", nameof(targetPptxPath));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output PPTX is required.", nameof(outputPath));
        }

        if (slideMap.Count == 0)
        {
            throw new ArgumentException("At least one slide mapping is required.", nameof(slideMap));
        }

        var sourceFullPath = Path.GetFullPath(sourcePptxPath);
        var targetFullPath = Path.GetFullPath(targetPptxPath);
        var outputFullPath = Path.GetFullPath(outputPath);

        ValidateSourcePackage(sourceFullPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputFullPath) ?? ".");
        if (File.Exists(outputFullPath))
        {
            File.Delete(outputFullPath);
        }

        File.Copy(targetFullPath, outputFullPath);

        using var sourceDocument = PresentationDocument.Open(sourceFullPath, false);
        using var outputDocument = PresentationDocument.Open(outputFullPath, true);
        var sourcePresentation = sourceDocument.PresentationPart
            ?? throw new InvalidOperationException("Source PPTX has no presentation part.");
        var outputPresentation = outputDocument.PresentationPart
            ?? throw new InvalidOperationException("Target PPTX has no presentation part.");

        var sourceSlides = OrderedSlideParts(sourcePresentation).ToArray();
        var outputSlides = OrderedSlideParts(outputPresentation).ToArray();
        var insertedShapes = 0;

        foreach (var (targetSlideNumber, sourceSlideNumber) in slideMap.OrderBy(pair => pair.Key))
        {
            if (sourceSlideNumber < 1 || sourceSlideNumber > sourceSlides.Length)
            {
                throw new InvalidOperationException(
                    $"Source slide {sourceSlideNumber.ToString(CultureInfo.InvariantCulture)} is outside the source deck slide range 1-{sourceSlides.Length.ToString(CultureInfo.InvariantCulture)}.");
            }

            if (targetSlideNumber < 1 || targetSlideNumber > outputSlides.Length)
            {
                throw new InvalidOperationException(
                    $"Target slide {targetSlideNumber.ToString(CultureInfo.InvariantCulture)} is outside the target deck slide range 1-{outputSlides.Length.ToString(CultureInfo.InvariantCulture)}.");
            }

            insertedShapes += InsertSlideShapes(
                sourceSlides[sourceSlideNumber - 1],
                outputSlides[targetSlideNumber - 1]);
        }

        outputPresentation.Presentation.Save();
        return new PptxShapeInsertResult(outputFullPath, slideMap.Count, insertedShapes);
    }

    public static IReadOnlyDictionary<int, int> ParseSlideMap(string mapSpec)
    {
        if (string.IsNullOrWhiteSpace(mapSpec))
        {
            throw new ArgumentException("Slide map is required.", nameof(mapSpec));
        }

        var result = new Dictionary<int, int>();
        foreach (var rawPair in mapSpec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = rawPair.Split('=', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var targetSlide) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sourceSlide) ||
                targetSlide < 1 ||
                sourceSlide < 1)
            {
                throw new ArgumentException(
                    $"Invalid slide map item '{rawPair}'. Expected 1-based target=source entries such as \"5=1,6=2\".",
                    nameof(mapSpec));
            }

            if (!result.TryAdd(targetSlide, sourceSlide))
            {
                throw new ArgumentException(
                    $"Target slide {targetSlide.ToString(CultureInfo.InvariantCulture)} appears more than once in the slide map.",
                    nameof(mapSpec));
            }
        }

        if (result.Count == 0)
        {
            throw new ArgumentException("Slide map is empty.", nameof(mapSpec));
        }

        return result;
    }

    private static int InsertSlideShapes(SlidePart sourceSlide, SlidePart targetSlide)
    {
        if (sourceSlide.ExternalRelationships.Any() || sourceSlide.HyperlinkRelationships.Any())
        {
            throw new InvalidOperationException("Source slide contains relationships that cannot be copied as standalone native shapes.");
        }

        var sourceXml = ReadPartXml(sourceSlide);
        var targetXml = ReadPartXml(targetSlide);
        var sourceTree = sourceXml.Descendants(P + "spTree").FirstOrDefault()
            ?? throw new InvalidOperationException("Source slide is missing p:spTree.");
        var targetTree = targetXml.Descendants(P + "spTree").FirstOrDefault()
            ?? throw new InvalidOperationException("Target slide is missing p:spTree.");

        var sourceShapes = sourceTree
            .Elements()
            .Where(element => element.Name == P + "sp" || element.Name == P + "cxnSp")
            .Select(element => new XElement(element))
            .ToArray();

        if (sourceShapes.Length == 0)
        {
            return 0;
        }

        foreach (var shape in sourceShapes)
        {
            if (shape.Name == P + "pic" || shape.Descendants(P + "pic").Any())
            {
                throw new InvalidOperationException("Source slide contains p:pic, which cannot be inserted by the native-shape inserter.");
            }

            if (ContainsRelationshipReference(shape))
            {
                throw new InvalidOperationException("Source shape contains relationship references and cannot be copied safely.");
            }
        }

        var usedIds = targetTree
            .Descendants(P + "cNvPr")
            .Select(element => (string?)element.Attribute("id"))
            .Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToHashSet();
        var nextId = Math.Max(2, usedIds.DefaultIfEmpty(1).Max() + 1);

        foreach (var shape in sourceShapes)
        {
            foreach (var cNvPr in shape.Descendants(P + "cNvPr"))
            {
                while (usedIds.Contains(nextId))
                {
                    nextId++;
                }

                cNvPr.SetAttributeValue("id", nextId.ToString(CultureInfo.InvariantCulture));
                usedIds.Add(nextId);
                nextId++;
            }

            targetTree.Add(shape);
        }

        WritePartXml(targetSlide, targetXml);
        return sourceShapes.Length;
    }

    private static void ValidateSourcePackage(string sourcePptxPath)
    {
        using (var archive = ZipFile.OpenRead(sourcePptxPath))
        {
            if (archive.Entries.Any(entry => entry.FullName.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Source PPTX contains ppt/media entries; only native-shape decks can be inserted.");
            }
        }

        using var document = PresentationDocument.Open(sourcePptxPath, false);
        var presentationPart = document.PresentationPart
            ?? throw new InvalidOperationException("Source PPTX has no presentation part.");
        if (AllParts(presentationPart, []).OfType<ImagePart>().Any())
        {
            throw new InvalidOperationException("Source PPTX contains image parts; only native-shape decks can be inserted.");
        }

        foreach (var slidePart in OrderedSlideParts(presentationPart))
        {
            var slideXml = ReadPartXml(slidePart);
            if (slideXml.Descendants(P + "pic").Any())
            {
                throw new InvalidOperationException("Source PPTX contains p:pic; only native-shape decks can be inserted.");
            }
        }
    }

    private static IEnumerable<SlidePart> OrderedSlideParts(PresentationPart presentationPart)
    {
        var slideIds = presentationPart.Presentation.SlideIdList?.Elements<DocumentFormat.OpenXml.Presentation.SlideId>()
            ?? Enumerable.Empty<DocumentFormat.OpenXml.Presentation.SlideId>();
        foreach (var slideId in slideIds)
        {
            if (slideId.RelationshipId?.Value is not { Length: > 0 } relationshipId)
            {
                continue;
            }

            yield return (SlidePart)presentationPart.GetPartById(relationshipId);
        }
    }

    private static IEnumerable<OpenXmlPart> AllParts(OpenXmlPartContainer container, HashSet<Uri> visited)
    {
        foreach (var partReference in container.Parts)
        {
            var part = partReference.OpenXmlPart;
            if (!visited.Add(part.Uri))
            {
                continue;
            }

            yield return part;
            foreach (var childPart in AllParts(part, visited))
            {
                yield return childPart;
            }
        }
    }

    private static bool ContainsRelationshipReference(XElement element) =>
        element.DescendantsAndSelf()
            .Attributes()
            .Any(attribute => attribute.Name.Namespace == R);

    private static XDocument ReadPartXml(OpenXmlPart part)
    {
        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private static void WritePartXml(OpenXmlPart part, XDocument document)
    {
        using var stream = part.GetStream(FileMode.Create, FileAccess.Write);
        document.Save(stream, SaveOptions.DisableFormatting);
    }
}

public sealed record PptxShapeInsertResult(
    string OutputPath,
    int MappedSlideCount,
    int InsertedShapeCount);
