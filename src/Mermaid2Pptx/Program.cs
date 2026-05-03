namespace Mermaid2Pptx;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);
            if (options.ShowHelp)
            {
                Console.WriteLine(CliOptions.HelpText);
                return 0;
            }

            if (string.IsNullOrWhiteSpace(options.HtmlPath) || string.IsNullOrWhiteSpace(options.OutputPath))
            {
                Console.Error.WriteLine("Missing required --html or --out argument.");
                Console.Error.WriteLine(CliOptions.HelpText);
                return 2;
            }

            var converter = new MermaidPptxConverter();
            var result = await converter.ConvertHtmlFileAsync(
                options.HtmlPath,
                options.OutputPath,
                options.SlideSelector,
                options.SvgSelector,
                options.WidthInches,
                options.HeightInches);

            Console.WriteLine($"Wrote {result.OutputPath}");
            Console.WriteLine($"Slides: {result.SlideCount}");
            Console.WriteLine($"Native shapes: {result.NativeShapeCount}");
            if (result.Warnings.Count > 0)
            {
                Console.WriteLine("Warnings:");
                foreach (var warning in result.Warnings)
                {
                    Console.WriteLine($"- {warning}");
                }
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}

public sealed class CliOptions
{
    public string HtmlPath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string SlideSelector { get; init; } = ".slide";
    public string SvgSelector { get; init; } = "svg";
    public double WidthInches { get; init; } = 13.333;
    public double HeightInches { get; init; } = 7.5;
    public bool ShowHelp { get; init; }

    public static string HelpText =>
        """
Usage:
  Mermaid2Pptx --html input.html --out output.pptx [options]

Options:
  --slide-selector ".slide"   CSS selector for HTML slides. Default: .slide
  --svg-selector "svg"        CSS selector for rendered SVGs inside each slide. Default: svg
  --width 13.333              Slide width in inches. Default: 13.333
  --height 7.5                Slide height in inches. Default: 7.5
""";

    public static CliOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help" or "/?")
            {
                return new CliOptions { ShowHelp = true };
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = arg[2..];
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[key] = "true";
                continue;
            }

            values[key] = args[++i];
        }

        return new CliOptions
        {
            HtmlPath = Value(values, "html"),
            OutputPath = Value(values, "out"),
            SlideSelector = Value(values, "slide-selector", ".slide"),
            SvgSelector = Value(values, "svg-selector", "svg"),
            WidthInches = DoubleValue(values, "width", 13.333),
            HeightInches = DoubleValue(values, "height", 7.5)
        };
    }

    private static string Value(IReadOnlyDictionary<string, string> values, string key, string fallback = "") =>
        values.TryGetValue(key, out var value) ? value : fallback;

    private static double DoubleValue(IReadOnlyDictionary<string, string> values, string key, double fallback) =>
        values.TryGetValue(key, out var value) && double.TryParse(value, out var result) ? result : fallback;
}
