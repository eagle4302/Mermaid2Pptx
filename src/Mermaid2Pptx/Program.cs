namespace Mermaid2Pptx;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);
            if (options.Command == CliCommand.Help)
            {
                Console.WriteLine(CliOptions.HelpText);
                return 0;
            }
            if (options.Command == CliCommand.Setup)
            {
                return InstallPlaywrightChromium();
            }

            if (string.IsNullOrWhiteSpace(options.OutputPath))
            {
                Console.Error.WriteLine("Missing required --out argument.");
                Console.Error.WriteLine(CliOptions.HelpText);
                return 2;
            }

            var converter = new MermaidPptxConverter();
            var insertMode = !string.IsNullOrWhiteSpace(options.InsertIntoPath);
            if (insertMode)
            {
                if (string.IsNullOrWhiteSpace(options.MapSpec))
                {
                    Console.Error.WriteLine("Missing required --map argument for insert mode.");
                    Console.Error.WriteLine(CliOptions.HelpText);
                    return 2;
                }

                if (options.ResolveOutputFormat() != OutputFormat.Pptx)
                {
                    Console.Error.WriteLine("Insert mode can only write PPTX output.");
                    return 2;
                }

                var sourceSelectionError = options.ValidateSourceSelection(insertMode: true);
                if (sourceSelectionError is not null)
                {
                    Console.Error.WriteLine(sourceSelectionError);
                    Console.Error.WriteLine(CliOptions.HelpText);
                    return 2;
                }

                var sourcePptxPath = options.SourcePptxPath;
                var tempSourceDirectory = string.Empty;
                try
                {
                    if (string.IsNullOrWhiteSpace(sourcePptxPath))
                    {
                        tempSourceDirectory = Path.Combine(Path.GetTempPath(), "mermaid2pptx-insert", Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(tempSourceDirectory);
                        sourcePptxPath = Path.Combine(tempSourceDirectory, "source-diagrams.pptx");
                        await ConvertInputSourceToPptxAsync(options, converter, sourcePptxPath);
                    }

                    var insertResult = new PptxShapeInserter().Insert(
                        sourcePptxPath,
                        options.InsertIntoPath,
                        options.OutputPath,
                        PptxShapeInserter.ParseSlideMap(options.MapSpec));

                    Console.WriteLine($"Wrote {insertResult.OutputPath}");
                    Console.WriteLine($"Mapped slides: {insertResult.MappedSlideCount}");
                    Console.WriteLine($"Inserted native shapes: {insertResult.InsertedShapeCount}");
                    return 0;
                }
                finally
                {
                    TryDeleteDirectory(tempSourceDirectory);
                }
            }

            var standaloneSourceSelectionError = options.ValidateSourceSelection(insertMode: false);
            if (standaloneSourceSelectionError is not null)
            {
                Console.Error.WriteLine(standaloneSourceSelectionError);
                Console.Error.WriteLine(CliOptions.HelpText);
                return 2;
            }

            var outputFormat = options.ResolveOutputFormat();
            if (outputFormat != OutputFormat.Pptx)
            {
                if (!string.IsNullOrWhiteSpace(options.HtmlPath))
                {
                    Console.Error.WriteLine("HTML source can only convert to PPTX. Use a Mermaid or draw.io source for --to mermaid/drawio.");
                    return 2;
                }

                var interopPath = await ConvertInteropAsync(options, outputFormat);
                Console.WriteLine($"Wrote {interopPath}");
                Console.WriteLine($"Format: {outputFormat.ToString().ToLowerInvariant()}");
                return 0;
            }

            var result = await ConvertInputSourceToPptxAsync(options, converter, options.OutputPath);
            WriteConversionResult(result);

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int InstallPlaywrightChromium()
    {
        Console.WriteLine("Installing Playwright Chromium...");
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode == 0)
        {
            Console.WriteLine("Playwright Chromium is ready.");
        }
        return exitCode;
    }

    private static async Task<ConversionResult> ConvertInputSourceToPptxAsync(
        CliOptions options,
        MermaidPptxConverter converter,
        string outputPath)
    {
        if (!string.IsNullOrWhiteSpace(options.HtmlPath))
        {
            return await converter.ConvertHtmlFileAsync(
                options.HtmlPath,
                outputPath,
                options.SlideSelector,
                options.SvgSelector,
                options.WidthInches,
                options.HeightInches);
        }

        if (options.IsDrawIoSource)
        {
            var drawIoXml = await ReadDrawIoMarkupAsync(options);
            return converter.ConvertDrawIoXml(
                drawIoXml,
                outputPath,
                options.WidthInches,
                options.HeightInches);
        }

        var mermaidCode = await ReadMermaidCodeAsync(options);
        return await converter.ConvertMermaidCodeAsync(
            mermaidCode,
            outputPath,
            options.WidthInches,
            options.HeightInches);
    }

    private static async Task<string> ReadDrawIoMarkupAsync(CliOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.DrawIoXml))
        {
            return options.DrawIoXml;
        }

        if (!string.IsNullOrWhiteSpace(options.DrawIoFilePath))
        {
            return await File.ReadAllTextAsync(options.DrawIoFilePath);
        }

        if (options.ReadDrawIoFromStdIn)
        {
            return await Console.In.ReadToEndAsync();
        }

        throw new InvalidOperationException("No draw.io source was provided.");
    }

    private static async Task<string> ReadMermaidCodeAsync(CliOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.MermaidCode))
        {
            return options.MermaidCode;
        }

        if (!string.IsNullOrWhiteSpace(options.MermaidFilePath))
        {
            return await File.ReadAllTextAsync(options.MermaidFilePath);
        }

        if (options.ReadMermaidFromStdIn)
        {
            return await Console.In.ReadToEndAsync();
        }

        throw new InvalidOperationException("No Mermaid source was provided.");
    }

    private static async Task<string> ConvertInteropAsync(CliOptions options, OutputFormat format)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath)) ?? ".");
        if (format == OutputFormat.DrawIo)
        {
            string xml;
            if (options.IsDrawIoSource)
            {
                xml = await ReadDrawIoMarkupAsync(options);
            }
            else
            {
                xml = FlowchartInterop.MermaidToDrawIo(await ReadMermaidCodeAsync(options));
            }

            await File.WriteAllTextAsync(options.OutputPath, xml);
            return Path.GetFullPath(options.OutputPath);
        }

        string mermaid;
        if (options.IsDrawIoSource)
        {
            mermaid = FlowchartInterop.DrawIoToMermaid(await ReadDrawIoMarkupAsync(options));
        }
        else
        {
            mermaid = MermaidFlowchartWriter.Write(new MermaidFlowchartParser().ParseAll(await ReadMermaidCodeAsync(options)));
        }

        await File.WriteAllTextAsync(options.OutputPath, mermaid);
        return Path.GetFullPath(options.OutputPath);
    }

    private static void WriteConversionResult(ConversionResult result)
    {
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
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Temporary insert sources are best-effort cleanup.
        }
    }
}

public enum CliCommand
{
    Convert,
    Setup,
    Help
}

public enum OutputFormat
{
    Pptx,
    DrawIo,
    Mermaid
}

public sealed class CliOptions
{
    public string HtmlPath { get; init; } = string.Empty;
    public string MermaidCode { get; init; } = string.Empty;
    public string MermaidFilePath { get; init; } = string.Empty;
    public string DrawIoXml { get; init; } = string.Empty;
    public string DrawIoFilePath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string InsertIntoPath { get; init; } = string.Empty;
    public string SourcePptxPath { get; init; } = string.Empty;
    public string MapSpec { get; init; } = string.Empty;
    public string SlideSelector { get; init; } = ".slide";
    public string SvgSelector { get; init; } = "svg";
    public double WidthInches { get; init; } = 13.333;
    public double HeightInches { get; init; } = 7.5;
    public bool ReadMermaidFromStdIn { get; init; }
    public bool ReadDrawIoFromStdIn { get; init; }
    public string ToFormat { get; init; } = string.Empty;
    public CliCommand Command { get; init; } = CliCommand.Convert;

    public bool IsDrawIoSource =>
        !string.IsNullOrWhiteSpace(DrawIoXml) ||
        !string.IsNullOrWhiteSpace(DrawIoFilePath) ||
        ReadDrawIoFromStdIn;

    public int ConversionSourceCount =>
        CountSource(HtmlPath) +
        CountSource(MermaidCode) +
        CountSource(MermaidFilePath) +
        CountSource(DrawIoXml) +
        CountSource(DrawIoFilePath) +
        (ReadMermaidFromStdIn ? 1 : 0) +
        (ReadDrawIoFromStdIn ? 1 : 0);

    public string? ValidateSourceSelection(bool insertMode)
    {
        var hasSourcePptx = !string.IsNullOrWhiteSpace(SourcePptxPath);
        var sourceList = "--html, --mermaid, --mermaid-file, --mermaid-stdin, --drawio, --drawio-file, or --drawio-stdin";
        if (!insertMode)
        {
            if (hasSourcePptx)
            {
                return "--source-pptx can only be used with --insert-into.";
            }

            return ConversionSourceCount switch
            {
                0 => $"Missing required source argument. Use exactly one of {sourceList}.",
                1 => null,
                _ => $"Use exactly one source argument: {sourceList}."
            };
        }

        if (hasSourcePptx && ConversionSourceCount > 0)
        {
            return "Insert mode accepts either --source-pptx or one conversion source, not both.";
        }

        if (!hasSourcePptx && ConversionSourceCount == 0)
        {
            return $"Insert mode requires either --source-pptx or one of {sourceList}.";
        }

        if (ConversionSourceCount > 1)
        {
            return "Insert mode accepts exactly one conversion source when --source-pptx is not used.";
        }

        return null;
    }

    public OutputFormat ResolveOutputFormat()
    {
        if (!string.IsNullOrWhiteSpace(ToFormat))
        {
            return ToFormat.Trim().ToLowerInvariant() switch
            {
                "pptx" or "powerpoint" => OutputFormat.Pptx,
                "drawio" or "draw.io" or "dio" or "xml" => OutputFormat.DrawIo,
                "mermaid" or "mmd" => OutputFormat.Mermaid,
                _ => throw new InvalidOperationException($"Unknown --to format '{ToFormat}'. Use pptx, drawio, or mermaid.")
            };
        }

        return Path.GetExtension(OutputPath).ToLowerInvariant() switch
        {
            ".drawio" or ".dio" => OutputFormat.DrawIo,
            ".mmd" => OutputFormat.Mermaid,
            ".xml" when IsDrawIoSource || !string.IsNullOrWhiteSpace(MermaidCode) ||
                        !string.IsNullOrWhiteSpace(MermaidFilePath) || ReadMermaidFromStdIn
                => OutputFormat.DrawIo,
            _ => OutputFormat.Pptx
        };
    }

    public static string HelpText =>
        """
Usage:
  mermaid2pptx setup
  mermaid2pptx --html input.html --out output.pptx [options]
  mermaid2pptx --mermaid "graph TD; A-->B" --out diagram.pptx [options]
  mermaid2pptx --mermaid-file diagram.mmd --out diagram.pptx [options]
  Get-Content diagram.mmd -Raw | mermaid2pptx --mermaid-stdin --out diagram.pptx [options]
  mermaid2pptx --drawio-file diagram.drawio --out diagram.pptx [options]
  mermaid2pptx --drawio "<mxfile>...</mxfile>" --out diagram.pptx [options]
  Get-Content diagram.drawio -Raw | mermaid2pptx --drawio-stdin --out diagram.pptx [options]
  mermaid2pptx --html diagrams.html --insert-into base.pptx --map "5=1,6=2" --out final.pptx [options]
  mermaid2pptx --mermaid-file diagram.mmd --insert-into base.pptx --map "5=1" --out final.pptx [options]
  mermaid2pptx --drawio-file diagram.drawio --insert-into base.pptx --map "5=1" --out final.pptx [options]
  mermaid2pptx --source-pptx diagrams.pptx --insert-into base.pptx --map "5=1,6=2" --out final.pptx
  mermaid2pptx --mermaid-file diagram.mmd --out diagram.drawio
  mermaid2pptx --drawio-file diagram.drawio --out diagram.mmd
  mermaid2pptx --mermaid "flowchart LR; A-->B" --to drawio --out diagram.drawio

Options:
  setup                       Install Playwright Chromium for deterministic rendering
  --html input.html           HTML file containing rendered Mermaid SVG
  --mermaid "graph TD; A-->B" Inline Mermaid code for a single-slide diagram deck
  --mermaid-file diagram.mmd  File containing Mermaid code for a single-slide diagram deck
  --mermaid-stdin             Read Mermaid code from standard input
  --drawio "<mxfile>..."      Inline draw.io / diagrams.net XML for native-shape conversion
  --drawio-file diagram.drawio File containing draw.io XML (.drawio, .xml)
  --drawio-stdin              Read draw.io XML from standard input
  --to pptx|drawio|mermaid    Output format. Inferred from --out extension when omitted
  --slide-selector ".slide"   CSS selector for HTML slides. Default: .slide
  --svg-selector "svg"        CSS selector for rendered SVGs inside each slide. Default: svg
  --width 13.333              Slide width in inches. Default: 13.333
  --height 7.5                Slide height in inches. Default: 7.5
  --insert-into base.pptx      Insert native shapes into an existing PPTX instead of writing a standalone diagram deck
  --source-pptx diagrams.pptx  Use an existing native-shape diagram PPTX as the insert source
  --map "5=1,6=2"             1-based target=source slide mapping for insert mode
""";

    public static CliOptions Parse(string[] args)
    {
        if (args.Any(arg => arg is "-h" or "--help" or "/?"))
        {
            return new CliOptions { Command = CliCommand.Help };
        }
        if (args.Length == 1 &&
            args[0].Equals("setup", StringComparison.OrdinalIgnoreCase))
        {
            return new CliOptions { Command = CliCommand.Setup };
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
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
            MermaidCode = Value(values, "mermaid"),
            MermaidFilePath = Value(values, "mermaid-file"),
            DrawIoXml = Value(values, "drawio"),
            DrawIoFilePath = Value(values, "drawio-file"),
            OutputPath = Value(values, "out"),
            InsertIntoPath = Value(values, "insert-into"),
            SourcePptxPath = Value(values, "source-pptx"),
            MapSpec = Value(values, "map"),
            SlideSelector = Value(values, "slide-selector", ".slide"),
            SvgSelector = Value(values, "svg-selector", "svg"),
            WidthInches = DoubleValue(values, "width", 13.333),
            HeightInches = DoubleValue(values, "height", 7.5),
            ReadMermaidFromStdIn = BoolValue(values, "mermaid-stdin"),
            ReadDrawIoFromStdIn = BoolValue(values, "drawio-stdin"),
            ToFormat = Value(values, "to")
        };
    }

    private static int CountSource(string value) =>
        string.IsNullOrWhiteSpace(value) ? 0 : 1;

    private static string Value(IReadOnlyDictionary<string, string> values, string key, string fallback = "") =>
        values.TryGetValue(key, out var value) ? value : fallback;

    private static double DoubleValue(IReadOnlyDictionary<string, string> values, string key, double fallback) =>
        values.TryGetValue(key, out var value) && double.TryParse(value, out var result) ? result : fallback;

    private static bool BoolValue(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return false;
        }

        return !bool.TryParse(value, out var result) || result;
    }
}
