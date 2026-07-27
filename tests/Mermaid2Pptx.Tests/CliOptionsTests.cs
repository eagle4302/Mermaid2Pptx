using Xunit;

namespace Mermaid2Pptx.Tests;

public sealed class CliOptionsTests
{
    [Fact]
    public void Parses_setup_command()
    {
        var options = CliOptions.Parse(["setup"]);

        Assert.Equal(CliCommand.Setup, options.Command);
        Assert.Equal(0, options.ConversionSourceCount);
    }

    [Fact]
    public void Parses_inline_mermaid_source()
    {
        var options = CliOptions.Parse(["--mermaid", "graph TD; A-->B", "--out", "diagram.pptx"]);

        Assert.Equal("graph TD; A-->B", options.MermaidCode);
        Assert.Equal("diagram.pptx", options.OutputPath);
        Assert.Equal(1, options.ConversionSourceCount);
        Assert.Null(options.ValidateSourceSelection(insertMode: false));
    }

    [Fact]
    public void Parses_mermaid_file_source()
    {
        var options = CliOptions.Parse(["--mermaid-file", "diagram.mmd", "--out", "diagram.pptx"]);

        Assert.Equal("diagram.mmd", options.MermaidFilePath);
        Assert.Equal(1, options.ConversionSourceCount);
        Assert.Null(options.ValidateSourceSelection(insertMode: false));
    }

    [Fact]
    public void Parses_mermaid_stdin_source()
    {
        var options = CliOptions.Parse(["--mermaid-stdin", "--out", "diagram.pptx"]);

        Assert.True(options.ReadMermaidFromStdIn);
        Assert.Equal(1, options.ConversionSourceCount);
        Assert.Null(options.ValidateSourceSelection(insertMode: false));
    }

    [Fact]
    public void Parses_existing_html_and_insert_options()
    {
        var options = CliOptions.Parse([
            "--html",
            "diagrams.html",
            "--insert-into",
            "base.pptx",
            "--map",
            "5=1,6=2",
            "--out",
            "final.pptx",
            "--slide-selector",
            ".deck-slide",
            "--svg-selector",
            "svg.diagram",
            "--width",
            "10",
            "--height",
            "5.625"
        ]);

        Assert.Equal("diagrams.html", options.HtmlPath);
        Assert.Equal("base.pptx", options.InsertIntoPath);
        Assert.Equal("5=1,6=2", options.MapSpec);
        Assert.Equal("final.pptx", options.OutputPath);
        Assert.Equal(".deck-slide", options.SlideSelector);
        Assert.Equal("svg.diagram", options.SvgSelector);
        Assert.Equal(10, options.WidthInches);
        Assert.Equal(5.625, options.HeightInches);
        Assert.Equal(1, options.ConversionSourceCount);
        Assert.Null(options.ValidateSourceSelection(insertMode: true));
    }

    [Fact]
    public void Rejects_missing_standalone_source()
    {
        var options = CliOptions.Parse(["--out", "diagram.pptx"]);

        Assert.NotNull(options.ValidateSourceSelection(insertMode: false));
    }

    [Fact]
    public void Rejects_multiple_conversion_sources()
    {
        var options = CliOptions.Parse([
            "--html",
            "input.html",
            "--mermaid",
            "graph TD; A-->B",
            "--out",
            "diagram.pptx"
        ]);

        Assert.Equal(2, options.ConversionSourceCount);
        Assert.NotNull(options.ValidateSourceSelection(insertMode: false));
    }

    [Fact]
    public void Allows_mermaid_input_in_insert_mode()
    {
        var options = CliOptions.Parse([
            "--mermaid-file",
            "diagram.mmd",
            "--insert-into",
            "base.pptx",
            "--map",
            "5=1",
            "--out",
            "final.pptx"
        ]);

        Assert.Null(options.ValidateSourceSelection(insertMode: true));
    }

    [Fact]
    public void Rejects_source_pptx_combined_with_conversion_source()
    {
        var options = CliOptions.Parse([
            "--source-pptx",
            "diagrams.pptx",
            "--mermaid-file",
            "diagram.mmd",
            "--insert-into",
            "base.pptx",
            "--map",
            "5=1",
            "--out",
            "final.pptx"
        ]);

        Assert.NotNull(options.ValidateSourceSelection(insertMode: true));
    }
}
