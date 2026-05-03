namespace Mermaid2Pptx;

public static class UnitConversion
{
    public const long EmuPerInch = 914400;
    public const double DefaultDpi = 96d;
    public const double EmuPerPixel = EmuPerInch / DefaultDpi;

    public static long InchesToEmu(double inches) => (long)Math.Round(inches * EmuPerInch);

    public static long PixelsToEmu(double pixels, double dpi = DefaultDpi) =>
        (long)Math.Round(pixels / dpi * EmuPerInch);

    public static SvgViewportMap CreateViewportMap(
        SvgRect viewBox,
        double slideWidthInches,
        double slideHeightInches,
        double marginInches = 0.35,
        bool keepAspectRatio = true)
    {
        var slideWidth = InchesToEmu(slideWidthInches);
        var slideHeight = InchesToEmu(slideHeightInches);
        var margin = InchesToEmu(marginInches);
        var availableWidth = Math.Max(1, slideWidth - margin * 2);
        var availableHeight = Math.Max(1, slideHeight - margin * 2);
        var scaleX = availableWidth / Math.Max(0.01, viewBox.Width);
        var scaleY = availableHeight / Math.Max(0.01, viewBox.Height);
        var scale = keepAspectRatio ? Math.Min(scaleX, scaleY) : scaleX;
        var contentWidth = viewBox.Width * scale;
        var contentHeight = viewBox.Height * (keepAspectRatio ? scale : scaleY);

        return new SvgViewportMap(
            viewBox,
            keepAspectRatio ? scale : scaleX,
            keepAspectRatio ? scale : scaleY,
            margin + (availableWidth - contentWidth) / 2d,
            margin + (availableHeight - contentHeight) / 2d,
            slideWidth,
            slideHeight);
    }

    public static SvgViewportMap CreateRegionMap(
        SvgRect viewBox,
        double slideWidthInches,
        double slideHeightInches,
        double regionX,
        double regionY,
        double regionWidth,
        double regionHeight,
        double sourceSlideWidth,
        double sourceSlideHeight,
        bool keepAspectRatio = true)
    {
        var slideWidth = InchesToEmu(slideWidthInches);
        var slideHeight = InchesToEmu(slideHeightInches);
        var targetX = regionX / Math.Max(1, sourceSlideWidth) * slideWidth;
        var targetY = regionY / Math.Max(1, sourceSlideHeight) * slideHeight;
        var targetWidth = Math.Max(1, regionWidth / Math.Max(1, sourceSlideWidth) * slideWidth);
        var targetHeight = Math.Max(1, regionHeight / Math.Max(1, sourceSlideHeight) * slideHeight);
        var scaleX = targetWidth / Math.Max(0.01, viewBox.Width);
        var scaleY = targetHeight / Math.Max(0.01, viewBox.Height);
        var scale = keepAspectRatio ? Math.Min(scaleX, scaleY) : scaleX;
        var contentWidth = viewBox.Width * scale;
        var contentHeight = viewBox.Height * (keepAspectRatio ? scale : scaleY);

        return new SvgViewportMap(
            viewBox,
            keepAspectRatio ? scale : scaleX,
            keepAspectRatio ? scale : scaleY,
            targetX + (targetWidth - contentWidth) / 2d,
            targetY + (targetHeight - contentHeight) / 2d,
            slideWidth,
            slideHeight);
    }
}

public sealed record SvgViewportMap(
    SvgRect ViewBox,
    double ScaleX,
    double ScaleY,
    double OffsetX,
    double OffsetY,
    long SlideWidthEmu,
    long SlideHeightEmu)
{
    public PptPoint Map(SvgPoint point) =>
        new(
            (long)Math.Round(OffsetX + (point.X - ViewBox.X) * ScaleX),
            (long)Math.Round(OffsetY + (point.Y - ViewBox.Y) * ScaleY));

    public double MapLengthX(double value) => value * ScaleX;
    public double MapLengthY(double value) => value * ScaleY;
}
