using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace SmartFactoryDeck;

internal static partial class SmartFactoryDeckTool
{
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

    internal sealed record PptxValidation(int ValidationErrorCount, int ImagePartCount, int MediaEntryCount);
}
